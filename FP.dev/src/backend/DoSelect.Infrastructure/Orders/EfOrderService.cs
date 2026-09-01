using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Application.Returns;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
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

        return OrderDtoMapper.Map(order, items);
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

        bool assemblyChanged;
        try
        {
            assemblyChanged = await OrderCancellationResourceReleaser.ReleaseAsync(
                _dbContext,
                order,
                actorUserId,
                now,
                auditContext.TraceId,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new OrderWriteException(OrderWriteException.ErrorCodes.OrderStateConflict, exception.Message);
        }

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

        List<AuditFieldChange> cancelFieldChanges =
        [
            AuditFieldChange.Changed("orderStatus"),
            AuditFieldChange.Changed("inventoryReservations"),
            AuditFieldChange.Changed("couponRedemptions"),
        ];
        if (assemblyChanged)
        {
            cancelFieldChanges.Add(AuditFieldChange.Changed("assemblyStatus"));
        }

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            auditActor,
            AuditActions.OrderCancel,
            AuditResourceTypes.Order,
            order.PublicId,
            AuditResult.Success,
            errorCode: null,
            cancelFieldChanges,
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
        return OrderDtoMapper.Map(order, items);
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
