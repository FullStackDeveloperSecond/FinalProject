using DoSelect.Application.Shipping;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Inventory;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Shipping;

// Each test seeds its own Published ShippingProviderProfile for one of the two fixed provider
// codes (unique filtered index: at most one Published row per ProviderCode), so — same reasoning
// as EfPackageLimitVersionAdminServiceTests — this class gets its own database per test method
// instead of sharing ShippingServiceFixture's one collection-wide database.
[Trait("Category", "RequiresSqlServer")]
public sealed class EfBatchShipmentServiceTests : IAsyncLifetime
{
    private readonly string _connectionString =
        $"Server=.\\SQL2025;Database=DoSelectBatchShipmentTests_{Guid.NewGuid():N};Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    private DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(_connectionString).Options;
        return new DoSelectDbContext(options);
    }

    [Fact]
    public async Task ShipBatchAsync_WhenOrderIsFullyReady_CreatesShipmentAndConsumesTheReservation()
    {
        await using var context = CreateContext();
        var (provider, _) = await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.HomeDelivery);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(context, ShippingProviderCodes.HomeDelivery, allowsCod: true);
        var sku = await ShippingServiceFixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10, reservedQuantity: 1);
        var order = await ShippingServiceFixture.SeedShippableOrderAsync(
            context, sku, provider.Id, method.Code, reservedQuantity: 1);
        var adminUserId = await ShippingServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfBatchShipmentService(context, new EfInventoryReservationService(context));

        var result = await service.ShipBatchAsync(
            new BatchShipmentRequest([order.PublicId]), adminUserId, DateTime.UtcNow, CancellationToken.None);

        Assert.True(result.Results[0].Success);
        Assert.NotNull(result.Results[0].ShipmentPublicId);
        Assert.NotNull(result.Results[0].TrackingNumber);

        await using var verifyContext = CreateContext();
        var shippedOrder = await verifyContext.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(FulfillmentStatus.Shipped, shippedOrder.FulfillmentStatus);

        var reservation = await verifyContext.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == order.Id);
        Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status);

        var shipment = await verifyContext.Shipments.AsNoTracking().SingleAsync(s => s.OrderId == order.Id);
        Assert.Equal(FulfillmentStatus.Shipped, shipment.Status);
        Assert.Null(shipment.ConvenienceStoreId);
    }

    [Fact]
    public async Task ShipBatchAsync_WhenConvenienceStorePickup_LinksTheResolvedStore()
    {
        await using var context = CreateContext();
        var (provider, _) = await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.ConvenienceStore);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(context, ShippingProviderCodes.ConvenienceStore);
        var store = await ShippingServiceFixture.SeedConvenienceStoreAsync(context, "7-ELEVEN");
        var sku = await ShippingServiceFixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10, reservedQuantity: 1);
        var order = await ShippingServiceFixture.SeedShippableOrderAsync(
            context, sku, provider.Id, method.Code, reservedQuantity: 1, storeCode: store.StoreCode);
        var adminUserId = await ShippingServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfBatchShipmentService(context, new EfInventoryReservationService(context));

        var result = await service.ShipBatchAsync(
            new BatchShipmentRequest([order.PublicId]), adminUserId, DateTime.UtcNow, CancellationToken.None);

        Assert.True(result.Results[0].Success);

        await using var verifyContext = CreateContext();
        var shipment = await verifyContext.Shipments.AsNoTracking().SingleAsync(s => s.OrderId == order.Id);
        Assert.Equal(store.Id, shipment.ConvenienceStoreId);
    }

    [Fact]
    public async Task ShipBatchAsync_IsolatesFailuresPerOrder_OneNotReadyOrderNeverBlocksAReadyOne()
    {
        await using var context = CreateContext();
        var (provider, _) = await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.HomeDelivery);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(context, ShippingProviderCodes.HomeDelivery, allowsCod: true);
        var readySku = await ShippingServiceFixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10, reservedQuantity: 1);
        var notReadySku = await ShippingServiceFixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10, reservedQuantity: 1);
        var readyOrder = await ShippingServiceFixture.SeedShippableOrderAsync(
            context, readySku, provider.Id, method.Code, reservedQuantity: 1);
        // Payment not satisfied: AllowsCod is true here but Failed still blocks readiness.
        var notReadyOrder = await ShippingServiceFixture.SeedShippableOrderAsync(
            context, notReadySku, provider.Id, method.Code, reservedQuantity: 1,
            paymentStatus: PaymentStatus.Failed);
        var adminUserId = await ShippingServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfBatchShipmentService(context, new EfInventoryReservationService(context));

        var result = await service.ShipBatchAsync(
            new BatchShipmentRequest([notReadyOrder.PublicId, readyOrder.PublicId]),
            adminUserId, DateTime.UtcNow, CancellationToken.None);

        var notReadyResult = result.Results.Single(r => r.OrderPublicId == notReadyOrder.PublicId);
        var readyResult = result.Results.Single(r => r.OrderPublicId == readyOrder.PublicId);
        Assert.False(notReadyResult.Success);
        Assert.Equal(ShippingWriteException.ErrorCodes.ShippingOrderNotReady, notReadyResult.ErrorCode);
        Assert.True(readyResult.Success);
    }

    [Fact]
    public async Task ShipBatchAsync_WhenReservationIsNotActive_FailsWithShippingOrderNotReady()
    {
        await using var context = CreateContext();
        var (provider, _) = await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.HomeDelivery);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(context, ShippingProviderCodes.HomeDelivery, allowsCod: true);
        var sku = await ShippingServiceFixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var order = await ShippingServiceFixture.SeedShippableOrderAsync(
            context, sku, provider.Id, method.Code, reservedQuantity: 1,
            reservationStatus: InventoryReservationStatus.Released);
        var adminUserId = await ShippingServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfBatchShipmentService(context, new EfInventoryReservationService(context));

        var result = await service.ShipBatchAsync(
            new BatchShipmentRequest([order.PublicId]), adminUserId, DateTime.UtcNow, CancellationToken.None);

        Assert.False(result.Results[0].Success);
        Assert.Equal(ShippingWriteException.ErrorCodes.ShippingOrderNotReady, result.Results[0].ErrorCode);
    }

    [Fact]
    public async Task ShipBatchAsync_WhenMoreThan100OrdersRequested_ThrowsShippingBatchLimitExceeded()
    {
        await using var context = CreateContext();
        var service = new EfBatchShipmentService(context, new EfInventoryReservationService(context));
        var tooMany = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();

        var exception = await Assert.ThrowsAsync<ShippingWriteException>(() => service.ShipBatchAsync(
            new BatchShipmentRequest(tooMany), "admin-id", DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingWriteException.ErrorCodes.ShippingBatchLimitExceeded, exception.ErrorCode);
    }
}
