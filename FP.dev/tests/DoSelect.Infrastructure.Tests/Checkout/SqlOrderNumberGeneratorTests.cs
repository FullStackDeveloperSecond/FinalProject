using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Checkout;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Checkout;

[CollectionDefinition(nameof(SqlOrderNumberGeneratorCollection))]
public sealed class SqlOrderNumberGeneratorCollection
    : ICollectionFixture<SqlOrderNumberGeneratorFixture>;

[Collection(nameof(SqlOrderNumberGeneratorCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class SqlOrderNumberGeneratorTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 26, 16, 30, 0, DateTimeKind.Utc);

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task NextAsync_WithCommittedOrder_AllocatesNextTaiwanDailySequence()
    {
        await using (var context = SqlOrderNumberGeneratorFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var generator = new SqlOrderNumberGenerator(context);
            var first = await generator.NextAsync(NowUtc);
            Assert.Equal("DS202608270001", first);

            var profile = new ShippingProviderProfile(
                Guid.NewGuid(), "TEST", 1, "Published", null, null, "{}", 1, NowUtc);
            context.ShippingProviderProfiles.Add(profile);
            await context.SaveChangesAsync();
            var limit = new PackageLimitVersion(
                Guid.NewGuid(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
                null, null, NowUtc);
            context.PackageLimitVersions.Add(limit);
            await context.SaveChangesAsync();
            context.Orders.Add(CreateOrder(first, profile.Id, limit.Id));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = SqlOrderNumberGeneratorFixture.CreateContext();
        await using var verificationTransaction =
            await verification.Database.BeginTransactionAsync();
        var second = await new SqlOrderNumberGenerator(verification).NextAsync(NowUtc);

        Assert.Equal("DS202608270002", second);
    }

    private static Order CreateOrder(string orderNumber, long profileId, long limitId) =>
        Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                orderNumber,
                null,
                "guest@doselect.test",
                OrderStatus.PendingPayment,
                PaymentStatus.AwaitingPayment,
                FulfillmentStatus.Pending,
                AssemblyStatus.NotRequired,
                1_000m,
                0m,
                0m,
                0m,
                1_000m,
                "Guest",
                "0912345678",
                "guest@doselect.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "No. 1",
                null,
                "HOME",
                profileId,
                null,
                null,
                null,
                1,
                1,
                null,
                NowUtc.AddMinutes(15),
                $"checkout-{Guid.NewGuid():N}",
                null,
                1,
                1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    "guest@doselect.test",
                    null,
                    null,
                    null,
                    null),
                null,
                null,
                new OrderPackageSnapshot(limitId, 1m, 40m, 30m, 20m, 90m, 1_000m)),
            NowUtc);
}

public sealed class SqlOrderNumberGeneratorFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (!global::DoSelect.Infrastructure.Tests.Idempotency.IdempotencyExecutorFixture.IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!global::DoSelect.Infrastructure.Tests.Idempotency.IdempotencyExecutorFixture.IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(
                global::DoSelect.Infrastructure.Tests.SqlServerTestConnection.Build(
                    "DoSelectOrderNumberTests"))
            .Options);
}
