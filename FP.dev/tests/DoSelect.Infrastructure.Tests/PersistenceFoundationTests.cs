using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

public sealed class PersistenceFoundationTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    [Fact]
    public void Model_UsesSingleContextWithIdentityAndSqlServerProvider()
    {
        using var context = CreateContext();

        Assert.Equal(
            "Microsoft.EntityFrameworkCore.SqlServer",
            context.Database.ProviderName);
        Assert.NotNull(context.Model.FindEntityType(typeof(ApplicationUser)));
        Assert.NotNull(context.Model.FindEntityType("Microsoft.AspNetCore.Identity.IdentityRole"));
        Assert.Equal(
            "AspNetUsers",
            context.Model.FindEntityType(typeof(ApplicationUser))?.GetTableName());
    }

    [Fact]
    public void AddDoSelectPersistence_RegistersDoSelectDbContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = SyntheticConnectionString,
            })
            .Build();
        var services = new ServiceCollection();

        services.AddDoSelectPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        Assert.Equal(
            "Microsoft.EntityFrameworkCore.SqlServer",
            context.Database.ProviderName);
    }

    [Fact]
    public void AddDoSelectPersistence_WhenConnectionStringIsMissing_FailsWithKeyOnly()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDoSelectPersistence(configuration));

        Assert.Equal(
            "Configuration key 'ConnectionStrings:DefaultConnection' is required.",
            exception.Message);
    }

    private static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }
}
