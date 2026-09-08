using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

public sealed class DemoAccountActivatorSqlServerTests
{
    [Fact]
    public async Task ActivateAsync_CompleteDemoSeed_EnablesExistingAccountsWithoutChangingManifest()
    {
        var databaseName = $"DoSelectDemo_{Guid.NewGuid():N}";
        var configuration = BuildConfiguration(databaseName);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDoSelectPersistence(configuration);

        await using var provider = services.BuildServiceProvider();
        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
                var seeded = await seeder.SeedAsync();
                Assert.Equal(10_000, seeded.MainBusinessRecordTotal);

                var activator = scope.ServiceProvider.GetRequiredService<DemoAccountActivator>();
                var result = await activator.ActivateAsync();

                Assert.True(result.AdminPasswordUpdated);
                Assert.True(result.MemberPasswordUpdated);
                Assert.Equal(2, result.RolesAssigned);
                Assert.NotEqual(Guid.Empty, result.AdminPublicId);

                var repeated = await activator.ActivateAsync();
                Assert.True(repeated.AdminPasswordUpdated);
                Assert.True(repeated.MemberPasswordUpdated);
                Assert.Equal(0, repeated.RolesAssigned);
                Assert.Equal(10_000, repeated.MainBusinessRecordTotal);
                Assert.Equal(result.AdminPublicId, repeated.AdminPublicId);
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var admin = await userManager.FindByEmailAsync(DemoAccountActivator.AdminEmail);
                var member = await userManager.FindByEmailAsync(DemoAccountActivator.MemberEmail);

                Assert.NotNull(admin);
                Assert.NotNull(member);
                Assert.Equal("demo-admin-0001", admin.Id);
                Assert.Equal("demo-member-0001", member.Id);
                Assert.Equal(AccountType.Admin, admin.AccountType);
                Assert.Equal(AccountType.Member, member.AccountType);
                Assert.True(await userManager.CheckPasswordAsync(admin, "Demo_Admin_123!"));
                Assert.True(await userManager.CheckPasswordAsync(member, "Demo_Member_123!"));
                Assert.True(await userManager.IsInRoleAsync(admin, "SuperAdmin"));
                Assert.True(await userManager.IsInRoleAsync(admin, "CustomerServiceSupervisor"));
                Assert.False(await userManager.GetTwoFactorEnabledAsync(admin));

                var validation = await scope.ServiceProvider
                    .GetRequiredService<DemoDataValidator>()
                    .ValidateAsync();
                Assert.True(validation.IsValid, string.Join(", ", validation.Failures));
                Assert.Equal(10_000, validation.MainBusinessRecordTotal);
            }
        }
        finally
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ActivateAsync_NonIsolatedDatabase_FailsBeforeConnecting()
    {
        var configuration = BuildConfiguration("DoSelectDb");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDoSelectPersistence(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var activator = scope.ServiceProvider.GetRequiredService<DemoAccountActivator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => activator.ActivateAsync());

        Assert.Contains("restricted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration BuildConfiguration(string databaseName) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = SqlServerTestConnection.Build(databaseName),
                ["Seed:AdminPassword"] = "Demo_Admin_123!",
                ["Seed:MemberPassword"] = "Demo_Member_123!",
            })
            .Build();
}
