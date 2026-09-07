using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Seeding;
using DoSelect.Domain.Members;
using DoSelect.Domain.Payments;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests;

public sealed class DemoDataValidatorSqlServerTests
{
    [Fact]
    public async Task ValidateAsync_ExactSeed_PassesEveryCheckAndReportBaseline()
    {
        await RunWithSeededDatabaseAsync(async context =>
        {
            var result = await CreateValidator(context).ValidateAsync();

            Assert.True(result.IsValid);
            Assert.Empty(result.Failures);
            Assert.All(result.Checks, check => Assert.True(check.Value, check.Key));
            Assert.Equal(DemoSeedManifest.MainBusinessRecordTotal, result.MainBusinessRecordTotal);
            Assert.Equal(
                DemoSeedManifest.ExpectedReportBaselines.OrderBy(entry => entry.Key),
                result.ReportBaselines.OrderBy(entry => entry.Key));
            Assert.Equal(
                new DemoDataIntegritySummary(0, 0, 0, 0, 0),
                result.Integrity);
        });
    }

    [Fact]
    public async Task ValidateAsync_ExtraValidBusinessRow_FailsCountsWithNonSensitiveSummary()
    {
        await RunWithSeededDatabaseAsync(async context =>
        {
            context.Favorites.Add(new Favorite(
                "demo-member-0001",
                await context.Products
                    .OrderBy(product => product.ProductCode)
                    .Skip(1)
                    .Select(product => product.Id)
                    .FirstAsync(),
                DemoSeedManifest.PeriodEndUtc));
            await context.SaveChangesAsync();

            var result = await CreateValidator(context).ValidateAsync();

            Assert.False(result.IsValid);
            Assert.Equal(
                ["entityCounts", "mainBusinessRecordTotal"],
                result.Failures);
            Assert.False(result.Checks["entityCounts"]);
            Assert.False(result.Checks["mainBusinessRecordTotal"]);
            Assert.True(result.Checks["databaseConstraints"]);
            Assert.True(result.Checks["orphanRows"]);
            Assert.True(result.Checks["reportBaselines"]);
            Assert.Equal(DemoSeedManifest.Favorites + 1, result.Counts["favorites"]);
        });
    }

    [Fact]
    public async Task ValidateAsync_ReportMetricDrift_FailsReportBaselineWithoutCountDrift()
    {
        await RunWithSeededDatabaseAsync(async context =>
        {
            var paidStatus = PaymentAttemptStatus.Paid.ToString();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE TOP (1) [PaymentAttempts]
                SET [Amount] = [Amount] + 1
                WHERE [Status] = {paidStatus}
                """);
            context.ChangeTracker.Clear();

            var result = await CreateValidator(context).ValidateAsync();

            Assert.False(result.IsValid);
            Assert.Equal(["reportBaselines"], result.Failures);
            Assert.True(result.Checks["entityCounts"]);
            Assert.True(result.Checks["mainBusinessRecordTotal"]);
            Assert.True(result.Checks["databaseConstraints"]);
            Assert.True(result.Checks["orphanRows"]);
            Assert.False(result.Checks["reportBaselines"]);
        });
    }

    private static DemoDataValidator CreateValidator(DoSelectDbContext context) => new(context);

    private static async Task RunWithSeededDatabaseAsync(
        Func<DoSelectDbContext, Task> assertion)
    {
        var databaseName = $"DoSelectDemo_{Guid.NewGuid():N}";
        var connectionString = SqlServerTestConnection.Build(databaseName);
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(240))
            .Options;

        try
        {
            var masterBuilder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master",
            };
            await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using var createDatabase = connection.CreateCommand();
                createDatabase.CommandText = $"CREATE DATABASE [{databaseName}]";
                await createDatabase.ExecuteNonQueryAsync();
            }

            await using var context = new DoSelectDbContext(options);
            await new DemoDataSeeder(context).SeedAsync();
            await assertion(context);
        }
        finally
        {
            await using var cleanup = new DoSelectDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
