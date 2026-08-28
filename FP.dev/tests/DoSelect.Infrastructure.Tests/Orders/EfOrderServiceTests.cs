using DoSelect.Application.Auditing;
using DoSelect.Application.Orders;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Auditing;
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

    private static readonly OrderCancellationAuditContext AuditContext = new(
        "order-service-test",
        "0123456789abcdef0123456789abcdef",
        RemoteIpAddress: null);

    private static EfOrderService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System), TimeProvider.System);

    [Fact]
    public async Task GetOrderAsync_WhenCallerOwnsOrder_ReturnsOrder()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, profile.Id, OrderStatus.PendingPayment);
        var service = CreateService(context);

        var dto = await service.GetOrderAsync(
            new OrderActor.Member(memberUserId), order.PublicId, CancellationToken.None);

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
            service.GetOrderAsync(
                new OrderActor.Member(otherUserId), order.PublicId, CancellationToken.None));
        Assert.Equal(OrderWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task GetOrderAsync_WhenOrderIsDeliveredWithReturnableQuantity_ExposesRequestReturnAction()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var profile = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var deliveredAtUtc = new DateTime(2026, 8, 1, 16, 30, 0, DateTimeKind.Utc);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context,
            memberUserId,
            profile.Id,
            OrderStatus.Completed,
            fulfillmentStatus: FulfillmentStatus.Delivered,
            deliveredAtUtc: deliveredAtUtc,
            returnableQuantity: 1);
        var service = CreateService(context);

        var dto = await service.GetOrderAsync(
            new OrderActor.Member(memberUserId), order.PublicId, CancellationToken.None);

        Assert.Contains("requestReturn", dto.AvailableActions);
        Assert.Equal(
            new DateTime(2026, 8, 9, 16, 0, 0, DateTimeKind.Utc),
            dto.ReturnRequestDeadlineUtc);
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
        var (_, reservation) = await OrderServiceFixture.SeedInventoryReservationAsync(context, order);
        var (coupon, redemption) = await OrderServiceFixture.SeedCouponReservationAsync(
            context, order, memberUserId, markExhausted: true);
        var service = CreateService(context);
        var request = new CancelOrderRequest(
            OrderCancellationReasonCodes.OrderedByMistake, "重複下單", order.RowVersion);

        var dto = await service.CancelOrderAsync(
            new OrderActor.Member(memberUserId),
            order.PublicId,
            request,
            AuditContext,
            CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, dto.OrderStatus);
        Assert.Empty(dto.AvailableActions);

        var history = await context.OrderStatusHistories
            .Where(candidate => candidate.OrderId == order.Id)
            .ToListAsync();
        var cancelHistory = Assert.Single(history);
        Assert.Equal(OrderStatus.Cancelled.ToString(), cancelHistory.ToStatus);
        Assert.Equal(OrderCancellationReasonCodes.OrderedByMistake, cancelHistory.ReasonCode);
        Assert.Equal(memberUserId, cancelHistory.ActorUserId);

        var reloadedReservation = await context.InventoryReservations.SingleAsync(
            candidate => candidate.Id == reservation.Id);
        var balance = await context.InventoryBalances.SingleAsync(
            candidate => candidate.SkuId == reservation.SkuId);
        var movement = await context.InventoryMovements.SingleAsync(
            candidate => candidate.ReservationId == reservation.Id);
        Assert.Equal(InventoryReservationStatus.Released, reloadedReservation.Status);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(5, balance.AvailableQuantity);
        Assert.Equal(-1, movement.ReservedDelta);
        Assert.Equal("order_cancelled", movement.ReasonCode);

        var reloadedRedemption = await context.CouponRedemptions.SingleAsync(
            candidate => candidate.Id == redemption.Id);
        var reloadedCoupon = await context.Coupons.SingleAsync(candidate => candidate.Id == coupon.Id);
        Assert.Equal(CouponRedemptionStatus.Released, reloadedRedemption.Status);
        Assert.Equal(CouponStatus.Active, reloadedCoupon.Status);

        var audit = await context.AuditLogs.SingleAsync(
            candidate => candidate.ResourcePublicId == order.PublicId);
        Assert.Equal(AuditActions.OrderCancel, audit.Action);
        Assert.Equal(AuditActorType.Member, audit.ActorType);
        Assert.Contains("重複下單", audit.ChangedFieldsJson, StringComparison.Ordinal);
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
            service.CancelOrderAsync(
                new OrderActor.Member(memberUserId),
                order.PublicId,
                request,
                AuditContext,
                CancellationToken.None));
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
            service.CancelOrderAsync(
                new OrderActor.Member(memberUserId),
                order.PublicId,
                request,
                AuditContext,
                CancellationToken.None));
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
        var (_, reservation) = await OrderServiceFixture.SeedInventoryReservationAsync(context, order);
        var (_, redemption) = await OrderServiceFixture.SeedCouponReservationAsync(
            context, order, memberUserId, markExhausted: false);

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
            service.CancelOrderAsync(
                new OrderActor.Member(memberUserId),
                order.PublicId,
                request,
                AuditContext,
                CancellationToken.None));
        Assert.Equal(OrderWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);

        await using var verification = OrderServiceFixture.CreateContext();
        Assert.Equal(
            OrderStatus.PendingPayment,
            (await verification.Orders.SingleAsync(candidate => candidate.Id == order.Id)).OrderStatus);
        Assert.Equal(
            InventoryReservationStatus.Active,
            (await verification.InventoryReservations.SingleAsync(
                candidate => candidate.Id == reservation.Id)).Status);
        Assert.Equal(
            1,
            (await verification.InventoryBalances.SingleAsync(
                candidate => candidate.SkuId == reservation.SkuId)).ReservedQuantity);
        Assert.Equal(
            CouponRedemptionStatus.Reserved,
            (await verification.CouponRedemptions.SingleAsync(
                candidate => candidate.Id == redemption.Id)).Status);
        Assert.Empty(await verification.InventoryMovements
            .Where(candidate => candidate.ReservationId == reservation.Id)
            .ToListAsync());
        Assert.Empty(await verification.OrderStatusHistories
            .Where(candidate => candidate.OrderId == order.Id)
            .ToListAsync());
        Assert.Empty(await verification.AuditLogs
            .Where(candidate => candidate.ResourcePublicId == order.PublicId)
            .ToListAsync());
    }
}
