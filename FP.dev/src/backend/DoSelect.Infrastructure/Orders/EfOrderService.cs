using System.Diagnostics;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// Member-owner scoped only for this slice (M-02's "訪客取消與退貨入口" 骨架) — guest access via
/// GuestOrderAccessToken depends on haru/feature/guest-ordertracking, which is not yet merged
/// into dev (see PR description). Every lookup here is filtered on the caller's own
/// MemberUserId; a guest-scoped overload can be added once that Cookie infrastructure lands,
/// without reshaping this service.
///
/// "退貨入口" is deliberately query-only in this slice: OrderDto.AvailableActions surfaces
/// `requestReturn` and OrderItemDto carries ReturnableQuantity/ReturnedQuantity so the customer
/// can see what qualifies, but no ReturnRequest is written here — Return's Application/API layer
/// (UC-RETURN-01) does not exist anywhere in the codebase yet and belongs to kafen's 客服退貨
/// module per the engineering package's cross-module contract.
/// </summary>
public sealed class EfOrderService : IOrderService
{
    private const string CancelAction = "cancel";
    private const string RequestReturnAction = "requestReturn";

    /// <summary>
    /// 退貨與退款政策.md「申請期限」：一般商品到貨翌日起 7 日內。只用來顯示猶豫期退貨的期限提示；
    /// 瑕疵／寄錯／運損／保固不受此期限限制，仍會顯示 requestReturn（見 BuildAvailableReturnDeadline
    /// 呼叫端）。
    /// </summary>
    private static readonly TimeSpan CoolingOffReturnWindow = TimeSpan.FromDays(7);

    private readonly DoSelectDbContext _dbContext;

    public EfOrderService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<OrderSummaryDto>> GetOrdersAsync(
        string memberUserId,
        OrderQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberUserId);
        ArgumentNullException.ThrowIfNull(query);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var ordersQuery = _dbContext.Orders.AsNoTracking()
            .Where(order => order.MemberUserId == memberUserId)
            .OrderByDescending(order => order.CreatedAtUtc);

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
        string memberUserId,
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberUserId);

        var order = await FindOwnedOrderAsync(memberUserId, orderPublicId, cancellationToken);
        var items = await _dbContext.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        return MapOrder(order, items);
    }

    public async Task<OrderDto> CancelOrderAsync(
        string memberUserId,
        Guid orderPublicId,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberUserId);
        ArgumentNullException.ThrowIfNull(request);

        if (!OrderCancellationReasonCodes.All.Contains(request.ReasonCode))
        {
            throw new OrderWriteException(
                OrderWriteException.ErrorCodes.ValidationFailed,
                $"'{request.ReasonCode}' is not a customer-selectable cancellation reason.");
        }

        var order = await FindOwnedOrderAsync(memberUserId, orderPublicId, cancellationToken);

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

        var now = DateTime.UtcNow;
        order.ChangeOrderStatus(OrderStatus.Cancelled, now);

        // Inventory release and coupon-usage restoration (state machine's documented side
        // effects for this transition) are out of scope: neither Terry's Reservation nor
        // yinyin's Coupon module has an Application-layer Use Case in dev yet to call into, and
        // this service must not write to another module's tables directly (engineering package
        // §6). Flagged in the PR/日誌 as a follow-up once those land.
        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.OrderStatus,
            fromStatus: OrderStatus.PendingPayment.ToString(),
            toStatus: OrderStatus.Cancelled.ToString(),
            reasonCode: request.ReasonCode,
            actorUserId: memberUserId,
            occurredAtUtc: now,
            traceId: GetTraceId()));

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        var items = await _dbContext.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        return MapOrder(order, items);
    }

    private async Task<Order> FindOwnedOrderAsync(
        string memberUserId,
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        // Not-found and not-owned collapse to the same resource_not_found response (API錯誤碼
        // 目錄.md: "不區分不存在與無權限") so a probing request can't learn whether the PublicId
        // belongs to someone else's order.
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(
                candidate => candidate.PublicId == orderPublicId && candidate.MemberUserId == memberUserId,
                cancellationToken);
        if (order is null)
        {
            throw new OrderWriteException(
                OrderWriteException.ErrorCodes.ResourceNotFound,
                $"Order '{orderPublicId}' was not found.");
        }

        return order;
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
            order.DeliveredAtUtc?.Add(CoolingOffReturnWindow),
            actions,
            order.RowVersion);
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

    private static string GetTraceId() =>
        Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
}
