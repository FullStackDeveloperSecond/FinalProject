using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Seeding;
using DoSelect.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

public sealed class MinimalDevelopmentSeedSqlServerTests
{
    [Fact]
    public async Task SeedAsync_NonE2eDatabase_RevokesLegacyRefundJourneyAdmin()
    {
        var connectionString = SqlServerTestConnection.Build(
            $"DoSelectSeedGuard_{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Seed:AdminPassword"] = "E2e_Admin_123!",
                ["Seed:MemberPassword"] = "E2e_Member_123!",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDoSelectPersistence(configuration);

        await using var provider = services.BuildServiceProvider();
        try
        {
            await using (var setupScope = provider.CreateAsyncScope())
            {
                var context = setupScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
                await context.Database.MigrateAsync();

                var seeder = setupScope.ServiceProvider.GetRequiredService<MinimalDevelopmentDataSeeder>();
                await seeder.SeedAsync();

                var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var legacyAdmin = ApplicationUser.CreateAdmin(
                    MinimalDevelopmentSeedDefinitions.RefundJourneyAdminPublicId,
                    MinimalDevelopmentSeedDefinitions.RefundJourneyAdminEmail,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
                legacyAdmin.ConfirmEmail(MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
                Assert.True((await userManager.CreateAsync(legacyAdmin, "E2e_Admin_123!")).Succeeded);
                Assert.True((await userManager.AddToRoleAsync(legacyAdmin, "SuperAdmin")).Succeeded);
                Assert.True((await userManager.SetAuthenticationTokenAsync(
                    legacyAdmin,
                    IdentityAdminAuthGateway.IdentityAuthenticatorLoginProvider,
                    IdentityAdminAuthGateway.IdentityAuthenticatorKeyTokenName,
                    "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP")).Succeeded);
                Assert.True((await userManager.SetTwoFactorEnabledAsync(legacyAdmin, true)).Succeeded);

                context.AdminProfiles.Add(new AdminProfile(
                    legacyAdmin.Id,
                    MinimalDevelopmentSeedDefinitions.RefundJourneyAdminPublicId,
                    "DEV-ADMIN-002",
                    "退款 E2E 管理員",
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
                await context.SaveChangesAsync();
            }

            await using (var seedScope = provider.CreateAsyncScope())
            {
                var seeder = seedScope.ServiceProvider.GetRequiredService<MinimalDevelopmentDataSeeder>();
                await seeder.SeedAsync();
            }

            await using (var verifyScope = provider.CreateAsyncScope())
            {
                var context = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
                var userManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var admin = await context.Users.SingleAsync(candidate =>
                    candidate.PublicId == MinimalDevelopmentSeedDefinitions.RefundJourneyAdminPublicId);
                var profile = await context.AdminProfiles.SingleAsync(candidate => candidate.UserId == admin.Id);

                Assert.Equal(AccountStatus.Suspended, admin.AccountStatus);
                Assert.False(profile.IsActive);
                Assert.Empty(await userManager.GetRolesAsync(admin));
                Assert.False(await userManager.GetTwoFactorEnabledAsync(admin));
                Assert.Null(await userManager.GetAuthenticationTokenAsync(
                    admin,
                    IdentityAdminAuthGateway.IdentityAuthenticatorLoginProvider,
                    IdentityAdminAuthGateway.IdentityAuthenticatorKeyTokenName));
            }
        }
        finally
        {
            await using var cleanupScope = provider.CreateAsyncScope();
            var context = cleanupScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
