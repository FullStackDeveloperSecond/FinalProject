using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Application.Returns;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// Customer-owner scoped order detail and cancellation. Member access is filtered by
/// MemberUserId; guest access is accepted only after the API has validated the target order with
/// GuestOrderAccessScopeAuthorizer and is additionally restricted to guest-owned orders here.
///
/// "退貨入口" remains query-only here: OrderDto supplies the existing Returns application page
/// with the owned order's RowVersion and eligible item identifiers; ReturnRequest creation stays
/// in the Returns application/API slice.
/// </summary>
public sealed class EfOrderService : IOrderService
{
    private const string CancelAction = "cancel";
    private const string RequestReturnAction = "requestReturn";
    private const string InventoryReleaseReason = "order_cancelled";

    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public EfOrderService(
        DoSelectDbContext dbContext,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<PageResult<OrderSummaryDto>> GetOrdersAsync(
        string memberUserId,
        OrderQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberUserId);
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;
        var pageNumber = Math.Clamp(query.PageNumber, 1, int.MaxValue / pageSize);

        var ordersQuery = _dbContext.Orders.AsNoTracking()
            .Where(order => order.MemberUserId == memberUserId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id);

        var totalCount = await ordersQuery.CountAsync(cancellationToken);
        var orders = await ordersQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(order => order.Id).ToArray();
        var itemCountsByOrderId = await _dbContext.OrderItems.AsNoTracking()
            .Where(item => orderIds.Contains(item.OrderId))
            .GroupBy(item => item.OrderId)
            .Select(group => new { OrderId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.OrderId, row => row.Count, cancellationToken);

        return new PageResult<OrderSummaryDto>(
            orders.Select(order => new OrderSummaryDto(
                order.PublicId,
                order.OrderNumber,
                order.OrderStatus,
                order.PaymentStatus,
                order.FulfillmentStatus,
                itemCountsByOrderId.GetValueOrDefault(order.Id, 0),
                order.GrandTotal,
                order.Currency,
                order.CreatedAtUtc,
                BuildSummaryAvailableActions(order)))
                .ToList(),
            pageNumber,
            pageSize,
            totalCount);
    }

    public async Task<OrderDto> GetOrderAsync(
        OrderActor actor,
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var order = await FindOwnedOrderAsync(actor, orderPublicId, cancellationToken);
        var items = await _dbContext.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        return MapOrder(order, items);
    }

    public async Task<OrderDto> CancelOrderAsync(
        OrderActor actor,
        Guid orderPublicId,
        CancelOrderRequest request,
        OrderCancellationAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(auditContext);

        if (!OrderCancellationReasonCodes.All.Contains(request.ReasonCode))
        {
            throw new OrderWriteException(
                OrderWriteException.ErrorCodes.ValidationFailed,
                $"'{request.ReasonCode}' is not a customer-selectable cancellation reason.");
        }

        var order = await FindOwnedOrderAsync(actor, orderPublicId, cancellationToken);

        // 狀態機設計.md：`Confirmed` 允許取消，但已付款者必須改走退款流程；本切片沒有可呼叫的
        // 退款發起 Use Case（ExecuteRefundService 是後台核准後執行，不是顧客自助入口），所以自助
        // 取消先只開放尚未付款的 PendingPayment。Confirmed 一律回 order_cancellation_not_allowed，
        // 待退款流程串接後再開放。
        if (order.OrderStatus != OrderStatus.PendingPayment)
        {
            throw new OrderWriteException(
                OrderWriteException.ErrorCodes.OrderCancellationNotAllowed,
                $"Order '{orderPublicId}' cannot be self-service cancelled from status '{order.OrderStatus}'.");
        }

        _dbContext.Entry(order).Property(candidate => candidate.RowVersion).OriginalValue =
            request.OrderRowVersion;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (actorUserId, auditActor) = await ResolveCancellationActorAsync(actor, cancellationToken);

        await ReleaseCancellationResourcesAsync(
            order,
            actorUserId,
            now,
            cancellationToken);
        order.ChangeOrderStatus(OrderStatus.Cancelled, now);

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.OrderStatus,
            fromStatus: OrderStatus.PendingPayment.ToString(),
            toStatus: OrderStatus.Cancelled.ToString(),
            reasonCode: request.ReasonCode,
            actorUserId,
            occurredAtUtc: now,
            traceId: auditContext.TraceId));

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            auditActor,
            AuditActions.OrderCancel,
            AuditResourceTypes.Order,
            order.PublicId,
            AuditResult.Success,
            errorCode: null,
            [
                AuditFieldChange.Changed("orderStatus"),
                AuditFieldChange.Changed("inventoryReservations"),
                AuditFieldChange.Changed("couponRedemptions"),
            ],
            request.ReasonCode,
            auditContext.CorrelationId,
            auditContext.TraceId,
            jobPublicId: null,
            remoteIpAddress: auditContext.RemoteIpAddress,
            note: request.Note));

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        var items = await _dbContext.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        return MapOrder(order, items);
    }

    private async Task<Order> FindOwnedOrderAsync(
        OrderActor actor,
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        // Not-found and not-owned collapse to the same resource_not_found response (API錯誤碼
        // 目錄.md: "不區分不存在與無權限") so a probing request can't learn whether the PublicId
        // belongs to someone else's order.
        var query = _dbContext.Orders.Where(candidate => candidate.PublicId == orderPublicId);
        query = actor switch
        {
            OrderActor.Member member => query.Where(candidate => candidate.MemberUserId == member.UserId),
            OrderActor.Guest => query.Where(candidate => candidate.MemberUserId == null),
            _ => throw new ArgumentOutOfRangeException(nameof(actor)),
        };
        var order = await query.FirstOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            throw new OrderWriteException(
                OrderWriteException.ErrorCodes.ResourceNotFound,
                $"Order '{orderPublicId}' was not found.");
        }

        return order;
    }

    private async Task<(string? ActorUserId, AuditActor AuditActor)> ResolveCancellationActorAsync(
        OrderActor actor,
        CancellationToken cancellationToken)
    {
        if (actor is OrderActor.Guest guest)
        {
            return (
                ActorUserId: null,
                AuditActor.Create(AuditActorType.Guest, guest.TokenPublicId, roles: []));
        }

        var member = (OrderActor.Member)actor;
        ArgumentException.ThrowIfNullOrWhiteSpace(member.UserId);
        var memberPublicId = await _dbContext.MemberProfiles
            .Where(profile => profile.UserId == member.UserId)
            .Select(profile => (Guid?)profile.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
        if (memberPublicId is null)
        {
            throw new OrderWriteException(
                OrderWriteException.ErrorCodes.OrderStateConflict,
                "The member profile required for cancellation audit was not found.");
        }

        return (
            member.UserId,
            AuditActor.Create(AuditActorType.Member, memberPublicId.Value, roles: []));
    }

    private static List<string> BuildSummaryAvailableActions(Order order)
    {
        var actions = new List<string>();
        if (order.OrderStatus == OrderStatus.PendingPayment)
        {
            actions.Add(CancelAction);
        }

        return actions;
    }

    private static OrderDto MapOrder(Order order, IReadOnlyList<OrderItem> items)
    {
        var itemDtos = items
            .Select(item => new OrderItemDto(
                item.PublicId,
                item.SkuCodeSnapshot,
                item.ProductNameSnapshot,
                item.SkuNameSnapshot,
                item.Quantity,
                item.FinalUnitPrice,
                item.LineTotal,
                item.ReturnableQuantity,
                item.ReturnedQuantity))
            .ToList();

        var actions = new List<string>();
        if (order.OrderStatus == OrderStatus.PendingPayment)
        {
            actions.Add(CancelAction);
        }

        var hasReturnableQuantity = itemDtos.Any(item => item.ReturnableQuantity > item.ReturnedQuantity);
        var isDelivered = order.FulfillmentStatus is FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp;
        if (isDelivered && hasReturnableQuantity)
        {
            actions.Add(RequestReturnAction);
        }

        var recipient = new OrderRecipientSummaryDto(
            order.RecipientName,
            order.ShippingMethodCode,
            order.StoreName);

        var amounts = new OrderAmountsDto(
            order.MerchandiseSubtotal,
            order.ItemDiscountTotal,
            order.ShippingFee,
            order.AssemblyFee,
            order.GrandTotal,
            order.PaidAmount,
            order.RefundedAmount,
            order.Currency);

        return new OrderDto(
            order.PublicId,
            order.OrderNumber,
            order.OrderStatus,
            order.PaymentStatus,
            order.FulfillmentStatus,
            order.AssemblyStatus,
            order.OrderRefundStatus,
            itemDtos,
            recipient,
            amounts,
            order.PaymentDueAtUtc,
            order.ConfirmedAtUtc,
            order.PaidAtUtc,
            order.ShippedAtUtc,
            order.DeliveredAtUtc,
            order.CompletedAtUtc,
            order.CancelledAtUtc,
            order.DeliveredAtUtc is { } deliveredAtUtc
                ? ReturnEligibilityPolicy.ComputeCoolingOffDeadlineUtc(deliveredAtUtc)
                : null,
            actions,
            order.RowVersion);
    }

    /// <summary>
    /// Alex PR #43 review A1: cancellation may not commit unless all active reservations and
    /// coupon seats are returned in the same SaveChanges transaction as the order transition.
    /// </summary>
    private async Task ReleaseCancellationResourcesAsync(
        Order order,
        string? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var reservations = await _dbContext.InventoryReservations
            .Where(candidate => candidate.OrderId == order.Id &&
                candidate.Status == InventoryReservationStatus.Active)
            .OrderBy(candidate => candidate.SkuId)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);
        var reservationGroups = reservations.GroupBy(candidate => candidate.SkuId).ToArray();
        var skuIds = reservationGroups.Select(group => group.Key).ToArray();
        var balances = await _dbContext.InventoryBalances
            .Where(candidate => skuIds.Contains(candidate.SkuId))
            .ToDictionaryAsync(candidate => candidate.SkuId, cancellationToken);

        foreach (var group in reservationGroups)
        {
            var quantityToRelease = group.Sum(candidate => candidate.Quantity);
            if (!balances.TryGetValue(group.Key, out var balance) ||
                balance.ReservedQuantity < quantityToRelease)
            {
                throw new OrderWriteException(
                    OrderWriteException.ErrorCodes.OrderStateConflict,
                    $"Inventory reservation state is inconsistent for order '{order.PublicId}'.");
            }

            var runningReservedQuantity = balance.ReservedQuantity;
            foreach (var reservation in group)
            {
                var afterReservedQuantity = runningReservedQuantity - reservation.Quantity;
                reservation.Release(InventoryReleaseReason, expired: false, releasedAtUtc: now);
                _dbContext.InventoryMovements.Add(new InventoryMovement(
                    Guid.CreateVersion7(),
                    reservation.SkuId,
                    reservation.Id,
                    "Release",
                    onHandDelta: 0,
                    reservedDelta: -reservation.Quantity,
                    beforeOnHand: balance.OnHandQuantity,
                    afterOnHand: balance.OnHandQuantity,
                    beforeReserved: runningReservedQuantity,
                    afterReserved: afterReservedQuantity,
                    reasonCode: InventoryReleaseReason,
                    referenceType: "Order",
                    referencePublicId: order.PublicId,
                    actorUserId,
                    occurredAtUtc: now));
                runningReservedQuantity = afterReservedQuantity;
            }

            balance.ApplyQuantities(balance.OnHandQuantity, runningReservedQuantity, now);
        }

        var redemptions = await _dbContext.CouponRedemptions
            .Where(candidate => candidate.OrderId == order.Id &&
                candidate.Status == CouponRedemptionStatus.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var redemption in redemptions)
        {
            redemption.Release(now);
        }

        var releasedRedemptionIds = redemptions.Select(candidate => candidate.Id).ToArray();
        var couponIds = redemptions.Select(candidate => candidate.CouponId).Distinct().ToArray();
        var exhaustedCoupons = await _dbContext.Coupons
            .Where(candidate => couponIds.Contains(candidate.Id) &&
                candidate.Status == CouponStatus.Exhausted)
            .ToListAsync(cancellationToken);
        foreach (var coupon in exhaustedCoupons)
        {
            var occupiedCount = await _dbContext.CouponRedemptions
                .AsNoTracking()
                .Where(candidate => candidate.CouponId == coupon.Id &&
                    !releasedRedemptionIds.Contains(candidate.Id))
                .Where(CouponRuleReader.OccupiesUsageSeatAt(now))
                .CountAsync(cancellationToken);
            var usage = new CouponUsageState(occupiedCount, MemberRedeemedCount: 0);
            if (coupon.IsWithinUsagePeriod(now) &&
                coupon.HasCompleteDiscountRule &&
                coupon.HasRemainingQuota(usage))
            {
                coupon.ReactivateAfterQuotaRelease(usage, now);
            }
        }

    }

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new OrderWriteException(
                OrderWriteException.ErrorCodes.ConcurrencyConflict,
                "The order was updated by someone else. Reload and try again.");
        }
    }

}
