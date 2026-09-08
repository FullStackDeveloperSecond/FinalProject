using DoSelect.Api.Observability;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests;

[Trait("Category", "RequiresSqlServer")]
public sealed class DatabaseSchemaReadinessSqlServerTests
{
    private const string PreviousMigration = "20260902031406_WidenCouponDiscountTypeColumns";

    [Fact]
    public async Task CheckAsync_RequiresTheDatabaseToHaveNoPendingMigrations()
    {
        var databaseName = $"DoSelectSchemaReadinessTests_{Guid.NewGuid():N}";
        var connectionString = SqlServerTestConnection.Build(databaseName);

        try
        {
            await using (var setup = CreateContext(connectionString))
            {
                await setup.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            await using (var outdatedProvider = CreateServices(connectionString))
            {
                var probe = new EfCoreDatabaseReadinessProbe(
                    outdatedProvider.GetRequiredService<IServiceScopeFactory>());

                Assert.Equal(
                    DatabaseReadinessProbeStatus.SchemaOutdated,
                    await probe.CheckAsync(CancellationToken.None));
            }

            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using (var currentProvider = CreateServices(connectionString))
            {
                var probe = new EfCoreDatabaseReadinessProbe(
                    currentProvider.GetRequiredService<IServiceScopeFactory>());

                Assert.Equal(
                    DatabaseReadinessProbeStatus.Ready,
                    await probe.CheckAsync(CancellationToken.None));
            }
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static ServiceProvider CreateServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DoSelectDbContext>(options => options.UseSqlServer(connectionString));
        return services.BuildServiceProvider();
    }

    private static DoSelectDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new DoSelectDbContext(options);
    }
}
