using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests;

public sealed class DemoDataSeedSqlServerTests
{
    [Fact]
    public void Manifest_HasExactVersionedTenThousandRecordAllocation()
    {
        Assert.Equal("implemented-features-v2", DemoSeedManifest.Version);
        Assert.Equal(20260907, DemoSeedManifest.RandomSeed);
        Assert.Equal(10_000, DemoSeedManifest.MainBusinessRecordTotal);
        Assert.Equal(10_000, DemoSeedManifest.ExpectedCounts.Values.Sum());
        Assert.Equal(130, DemoSeedManifest.Coupons + DemoSeedManifest.CouponRedemptions);
        Assert.Equal(0, DemoSeedManifest.AiSearchFunnelEvents);
        Assert.Equal(100, DemoSeedManifest.ExpectedDistributionCounts["succeededRefunds"]);
    }

    [Fact]
    public async Task SeedAsync_DisallowedDatabaseName_FailsBeforeConnecting()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SqlServerTestConnection.Build("DoSelectDb"))
            .Options;
        await using var context = new DoSelectDbContext(options);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DemoDataSeeder(context).SeedAsync());

        Assert.Contains("restricted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeedAsync_RemoteDataSource_FailsBeforeConnecting()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(
                "Server=sql.example.invalid;Database=DoSelectDemo;" +
                "Integrated Security=True;TrustServerCertificate=True")
            .Options;
        await using var context = new DoSelectDbContext(options);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DemoDataSeeder(context).SeedAsync());

        Assert.Contains("local or loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeedAsync_FreshDatabase_CreatesExactCountsAndSecondRunIsNoOp()
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

            Guid firstProductPublicId;
            Guid firstOrderPublicId;
            await using (var context = new DoSelectDbContext(options))
            {
                var first = await new DemoDataSeeder(context).SeedAsync();

                Assert.True(first.Created);
                Assert.Equal(10_000, first.MainBusinessRecordTotal);
                Assert.Equal(
                    DemoSeedManifest.ExpectedCounts.OrderBy(entry => entry.Key),
                    first.Counts.OrderBy(entry => entry.Key));
                Assert.Equal(
                    DemoSeedManifest.ExpectedDistributionCounts.OrderBy(entry => entry.Key),
                    first.Distribution.OrderBy(entry => entry.Key));
                firstProductPublicId = await context.Products
                    .OrderBy(product => product.ProductCode)
                    .Select(product => product.PublicId)
                    .FirstAsync();
                firstOrderPublicId = await context.Orders
                    .OrderBy(order => order.OrderNumber)
                    .Select(order => order.PublicId)
                    .FirstAsync();

                Assert.Equal(100, await context.InventoryBalances.CountAsync(balance =>
                    balance.AvailableQuantity <= balance.ReorderLevel));
                Assert.False(await context.InventoryBalances.AnyAsync(balance =>
                    balance.OnHandQuantity < 0 ||
                    balance.ReservedQuantity < 0 ||
                    balance.AvailableQuantity < 0));
                Assert.Equal(250, await context.Orders.CountAsync(order =>
                    order.OrderStatus == OrderStatus.Completed &&
                    order.PaymentStatus == PaymentStatus.Paid &&
                    order.FulfillmentStatus == FulfillmentStatus.Delivered));
                Assert.Equal(100, await context.Orders.CountAsync(order =>
                    order.OrderStatus == OrderStatus.Cancelled));
                Assert.False(await context.Orders.AnyAsync(order =>
                    order.OrderStatus == OrderStatus.Completed &&
                    (order.PaymentStatus != PaymentStatus.Paid ||
                     order.FulfillmentStatus != FulfillmentStatus.Delivered)));
                Assert.False(await context.Orders.AnyAsync(order =>
                    order.OrderStatus == OrderStatus.Cancelled && order.PaidAmount != 0m));
                Assert.Equal(100, await context.PaymentAttempts.CountAsync(attempt =>
                    attempt.Status == PaymentAttemptStatus.Failed));
                Assert.Equal(100, await context.PaymentAttempts.CountAsync(attempt =>
                    attempt.Status == PaymentAttemptStatus.Expired));
                Assert.False(await context.PaymentAttempts.AnyAsync(attempt =>
                    attempt.Status == PaymentAttemptStatus.Paid && attempt.PaidAtUtc == null ||
                    attempt.Status == PaymentAttemptStatus.Failed &&
                    (attempt.FailedAtUtc == null || attempt.FailureCode == null)));
                Assert.Equal(150, await context.ReturnItems.CountAsync());
                Assert.Equal(100, await context.Refunds.CountAsync(refund =>
                    refund.Status == DoSelect.Domain.Refunds.RefundStatus.Succeeded));
                Assert.Equal(100, await context.ReturnRequests.CountAsync(request =>
                    request.Status == DoSelect.Domain.Returns.ReturnRequestStatus.Completed));
                Assert.False(await context.ReturnRequests.AnyAsync(request =>
                    request.Status == DoSelect.Domain.Returns.ReturnRequestStatus.Completed &&
                    !context.Refunds.Any(refund =>
                        refund.ReturnRequestId == request.Id &&
                        refund.Status == DoSelect.Domain.Refunds.RefundStatus.Succeeded)));
                Assert.False(await context.Orders.AnyAsync(order =>
                    order.CreatedAtUtc < DemoSeedManifest.PeriodStartUtc ||
                    order.CreatedAtUtc > DemoSeedManifest.PeriodEndUtc));
                Assert.False(await context.ReturnRequests.AnyAsync(request =>
                    request.CreatedAtUtc < DemoSeedManifest.PeriodStartUtc ||
                    request.CreatedAtUtc > DemoSeedManifest.PeriodEndUtc));
            }

            await using (var context = new DoSelectDbContext(options))
            {
                var second = await new DemoDataSeeder(context).SeedAsync();

                Assert.False(second.Created);
                Assert.Equal(10_000, second.MainBusinessRecordTotal);
                Assert.Equal(
                    DemoSeedManifest.ExpectedDistributionCounts.OrderBy(entry => entry.Key),
                    second.Distribution.OrderBy(entry => entry.Key));
                Assert.Equal(firstProductPublicId, await context.Products
                    .OrderBy(product => product.ProductCode)
                    .Select(product => product.PublicId)
                    .FirstAsync());
                Assert.Equal(firstOrderPublicId, await context.Orders
                    .OrderBy(order => order.OrderNumber)
                    .Select(order => order.PublicId)
                    .FirstAsync());
            }

            await using var constraintContext = new DoSelectDbContext(options);
            await constraintContext.Database.OpenConnectionAsync();
            await using var command = constraintContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = "DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());

            await reader.DisposeAsync();
            await constraintContext.Database.CloseConnectionAsync();
            constraintContext.Favorites.Add(new DoSelect.Domain.Members.Favorite(
                "demo-member-0001",
                await constraintContext.Products
                    .OrderBy(product => product.ProductCode)
                    .Skip(1)
                    .Select(product => product.Id)
                    .FirstAsync(),
                DemoSeedManifest.PeriodEndUtc));
            await constraintContext.SaveChangesAsync();
            var countBeforeRejectedRun = await constraintContext.Favorites.CountAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new DemoDataSeeder(constraintContext).SeedAsync());
            Assert.Equal(countBeforeRejectedRun, await constraintContext.Favorites.CountAsync());
        }
        finally
        {
            await using var cleanup = new DoSelectDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
