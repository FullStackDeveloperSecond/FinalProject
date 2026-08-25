using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Orders;

[CollectionDefinition(nameof(OrderServiceCollection))]
public sealed class OrderServiceCollection : ICollectionFixture<OrderServiceFixture>;

[Collection(nameof(OrderServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfOrderServiceTests
{
    private readonly OrderServiceFixture _fixture;

    public EfOrderServiceTests(OrderServiceFixture fixture)
    {
        _fixture = fixture;
    }

    private static EfOrderService CreateService(DoSelectDbContext context) => new(context);

    [Fact]
    public async Task GetOrderAsync_WhenCallerOwnsOrder_ReturnsOrder()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, profile.Id, OrderStatus.PendingPayment);
        var service = CreateService(context);

        var dto = await service.GetOrderAsync(memberUserId, order.PublicId, CancellationToken.None);

        Assert.Equal(order.PublicId, dto.PublicId);
        Assert.Contains("cancel", dto.AvailableActions);
    }

    [Fact]
    public async Task GetOrderAsync_WhenCallerDoesNotOwnOrder_ThrowsResourceNotFound()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var ownerUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var otherUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, ownerUserId, profile.Id, OrderStatus.PendingPayment);
        var service = CreateService(context);

        var exception = await Assert.ThrowsAsync<OrderWriteException>(() =>
            service.GetOrderAsync(otherUserId, order.PublicId, CancellationToken.None));
        Assert.Equal(OrderWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task GetOrderAsync_WhenOrderIsDeliveredWithReturnableQuantity_ExposesRequestReturnAction()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context,
            memberUserId,
            profile.Id,
            OrderStatus.Completed,
            fulfillmentStatus: FulfillmentStatus.Delivered,
            deliveredAtUtc: DateTime.UtcNow.AddDays(-1),
            returnableQuantity: 1);
        var service = CreateService(context);

        var dto = await service.GetOrderAsync(memberUserId, order.PublicId, CancellationToken.None);

        Assert.Contains("requestReturn", dto.AvailableActions);
        Assert.NotNull(dto.ReturnRequestDeadlineUtc);
        var item = Assert.Single(dto.Items);
        Assert.Equal(1, item.ReturnableQuantity - item.ReturnedQuantity);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenOrderIsPendingPayment_CancelsAndRecordsHistory()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, profile.Id, OrderStatus.PendingPayment);
        var service = CreateService(context);
        var request = new CancelOrderRequest(
            OrderCancellationReasonCodes.ChangedMind, null, order.RowVersion);

        var dto = await service.CancelOrderAsync(memberUserId, order.PublicId, request, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, dto.OrderStatus);
        Assert.Empty(dto.AvailableActions);

        var history = await context.OrderStatusHistories
            .Where(candidate => candidate.OrderId == order.Id)
            .ToListAsync();
        var cancelHistory = Assert.Single(history);
        Assert.Equal(OrderStatus.Cancelled.ToString(), cancelHistory.ToStatus);
        Assert.Equal(OrderCancellationReasonCodes.ChangedMind, cancelHistory.ReasonCode);
        Assert.Equal(memberUserId, cancelHistory.ActorUserId);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenOrderIsConfirmed_ThrowsOrderCancellationNotAllowed()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, profile.Id, OrderStatus.Confirmed);
        var service = CreateService(context);
        var request = new CancelOrderRequest(
            OrderCancellationReasonCodes.ChangedMind, null, order.RowVersion);

        var exception = await Assert.ThrowsAsync<OrderWriteException>(() =>
            service.CancelOrderAsync(memberUserId, order.PublicId, request, CancellationToken.None));
        Assert.Equal(OrderWriteException.ErrorCodes.OrderCancellationNotAllowed, exception.ErrorCode);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenReasonCodeIsNotCustomerSelectable_ThrowsValidationFailed()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, profile.Id, OrderStatus.PendingPayment);
        var service = CreateService(context);
        var request = new CancelOrderRequest("merchant_initiated", null, order.RowVersion);

        var exception = await Assert.ThrowsAsync<OrderWriteException>(() =>
            service.CancelOrderAsync(memberUserId, order.PublicId, request, CancellationToken.None));
        Assert.Equal(OrderWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenRowVersionIsStale_ThrowsConcurrencyConflict()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, profile.Id, OrderStatus.PendingPayment);
        var staleRowVersion = order.RowVersion;

        // Mutate the order (via a fresh context) so the seeded RowVersion the test captured
        // above is now stale, mirroring another request having already touched this order.
        // A same-value AssemblyStatus projection still bumps the SQL Server rowversion column
        // without moving OrderStatus off PendingPayment, keeping the cancellation-allowed guard
        // out of the way so this test isolates the concurrency check.
        await using (var mutatingContext = OrderServiceFixture.CreateContext())
        {
            var tracked = await mutatingContext.Orders.SingleAsync(candidate => candidate.Id == order.Id);
            tracked.ApplyAssemblyProjection(AssemblyStatus.NotRequired, DateTime.UtcNow);
            await mutatingContext.SaveChangesAsync();
        }

        var service = CreateService(context);
        var request = new CancelOrderRequest(
            OrderCancellationReasonCodes.ChangedMind, null, staleRowVersion);

        var exception = await Assert.ThrowsAsync<OrderWriteException>(() =>
            service.CancelOrderAsync(memberUserId, order.PublicId, request, CancellationToken.None));
        Assert.Equal(OrderWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }
}
