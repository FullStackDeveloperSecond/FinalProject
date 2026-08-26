using System.Globalization;
using System.Text;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// 後台訂單管理（M-08 後半段）。只涵蓋 OrderStatus 維度的管理員操作（開始備貨／人工取消）；
/// 取消不釋放庫存、不建立退款交易——terry 的 InventoryReservation 與 yinyin 的 Refund 建立
/// Application 契約都還沒就緒，整合留待該兩個模組的正式契約到位後再做（見 PR 日誌）。
/// </summary>
public sealed class EfAdminOrderService : IAdminOrderService
{
    private readonly DoSelectDbContext _dbContext;

    public EfAdminOrderService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CursorPage<AdminOrderSummaryDto>> ListAsync(
        AdminOrderQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var summarySet = NormalizeAndValidate(query.SummaryStatus, AdminOrderSummaryStatuses.All, "summaryStatus");
        var badgeSet = NormalizeAndValidate(query.Badge, AdminOrderBadges.All, "badge");
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var ordersQuery = _dbContext.Orders.AsNoTracking().AsQueryable();

        ordersQuery = ApplySummaryStatusFilter(ordersQuery, summarySet);
        ordersQuery = ApplyBadgeFilter(ordersQuery, badgeSet);

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var afterCreatedAtUtc = DecodeCursor(query.Cursor);
            ordersQuery = ordersQuery.Where(order => order.CreatedAtUtc < afterCreatedAtUtc);
        }

        var orders = await ordersQuery
            .OrderByDescending(order => order.CreatedAtUtc)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = orders.Count > pageSize;
        var page = hasMore ? orders.Take(pageSize).ToList() : orders;
        var nextCursor = hasMore ? EncodeCursor(page[^1].CreatedAtUtc) : null;

        var items = page.Select(ToSummaryDto).ToList();
        return new CursorPage<AdminOrderSummaryDto>(items, nextCursor, hasMore);
    }

    public async Task<AdminOrderDto> GetAsync(Guid orderPublicId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == orderPublicId, cancellationToken);
        if (order is null)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ResourceNotFound,
                $"Order '{orderPublicId}' was not found.");
        }

        return await BuildDetailAsync(order, cancellationToken);
    }

    public async Task<OrderRecipientDto> GetRecipientAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == orderPublicId, cancellationToken);
        if (order is null)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ResourceNotFound,
                $"Order '{orderPublicId}' was not found.");
        }

        return new OrderRecipientDto(
            order.PublicId,
            order.RecipientName,
            order.RecipientPhone,
            order.RecipientEmail,
            order.PostalCode,
            order.RecipientCity,
            order.RecipientDistrict,
            order.AddressLine1,
            order.AddressLine2,
            order.ShippingMethodCode,
            order.StoreCode,
            order.StoreName,
            order.StoreAddress,
            "OrderFulfillment");
    }

    public async Task<AdminOrderDto> ExecuteActionAsync(
        Guid orderPublicId,
        string action,
        string actorUserId,
        string traceId,
        AdminOrderActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (action != AdminOrderActions.StartProcessing && action != AdminOrderActions.Cancel)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ValidationFailed,
                $"Action '{action}' is not supported.");
        }

        if (action == AdminOrderActions.Cancel && string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ValidationFailed,
                "A reason code is required to cancel an order.");
        }

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(candidate => candidate.PublicId == orderPublicId, cancellationToken);
        if (order is null)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ResourceNotFound,
                $"Order '{orderPublicId}' was not found.");
        }

        _dbContext.Entry(order).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var fromStatus = order.OrderStatus;
        var now = DateTime.UtcNow;
        var targetStatus = action == AdminOrderActions.StartProcessing
            ? OrderStatus.Processing
            : OrderStatus.Cancelled;

        try
        {
            order.ChangeOrderStatus(targetStatus, now);
        }
        catch (InvalidOperationException exception)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.OrderStateConflict,
                exception.Message);
        }

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.OrderStatus,
            fromStatus.ToString(),
            targetStatus.ToString(),
            request.ReasonCode,
            actorUserId,
            now,
            traceId));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ConcurrencyConflict,
                "The order was updated by someone else. Reload and try again.");
        }

        return await BuildDetailAsync(order, cancellationToken);
    }

    private async Task<AdminOrderDto> BuildDetailAsync(Order order, CancellationToken cancellationToken)
    {
        var items = await _dbContext.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        var history = await _dbContext.OrderStatusHistories.AsNoTracking()
            .Where(entry => entry.OrderId == order.Id)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        var itemDtos = items.Select(item => new AdminOrderItemDto(
            item.PublicId,
            item.SkuCodeSnapshot,
            item.ProductNameSnapshot,
            item.SkuNameSnapshot,
            item.Quantity,
            item.ListUnitPrice,
            item.SaleUnitPrice,
            item.FinalUnitPrice,
            item.UnitCostSnapshot,
            item.LineSubtotal,
            item.DiscountAllocation,
            item.LineTotal,
            item.ReturnableQuantity,
            item.ReturnedQuantity)).ToList();

        var historyDtos = history.Select(entry => new OrderStatusHistoryDto(
            entry.StateDimension.ToString(),
            entry.FromStatus,
            entry.ToStatus,
            entry.ReasonCode,
            entry.ActorUserId,
            entry.OccurredAtUtc)).ToList();

        return new AdminOrderDto(
            order.PublicId,
            order.OrderNumber,
            BuyerTypeOf(order),
            MaskEmail(order.RecipientEmail),
            order.OrderStatus.ToString(),
            order.PaymentStatus.ToString(),
            order.FulfillmentStatus.ToString(),
            order.AssemblyStatus.ToString(),
            order.OrderRefundStatus.ToString(),
            SummaryStatusOf(order),
            BadgesOf(order),
            itemDtos,
            new AdminOrderAmountsDto(
                order.MerchandiseSubtotal,
                order.ItemDiscountTotal,
                order.ShippingFee,
                order.AssemblyFee,
                order.GrandTotal,
                order.PaidAmount,
                order.RefundedAmount,
                order.Currency),
            order.ShippingMethodCode,
            order.StoreName,
            historyDtos,
            AvailableActionsOf(order),
            order.PaymentDueAtUtc,
            order.ConfirmedAtUtc,
            order.PaidAtUtc,
            order.ShippedAtUtc,
            order.DeliveredAtUtc,
            order.CompletedAtUtc,
            order.CancelledAtUtc,
            order.CreatedAtUtc,
            order.RowVersion);
    }

    private static AdminOrderSummaryDto ToSummaryDto(Order order) => new(
        order.PublicId,
        order.OrderNumber,
        BuyerTypeOf(order),
        MaskEmail(order.RecipientEmail),
        order.OrderStatus.ToString(),
        order.PaymentStatus.ToString(),
        order.FulfillmentStatus.ToString(),
        order.AssemblyStatus.ToString(),
        order.OrderRefundStatus.ToString(),
        SummaryStatusOf(order),
        BadgesOf(order),
        order.GrandTotal,
        order.Currency,
        order.ShippingMethodCode,
        order.CreatedAtUtc,
        order.PaidAtUtc,
        order.ShippedAtUtc,
        order.DeliveredAtUtc,
        order.CompletedAtUtc,
        order.RowVersion);

    private static string BuyerTypeOf(Order order) =>
        string.IsNullOrWhiteSpace(order.MemberUserId) ? "Guest" : "Member";

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex <= 0 ? "***" : $"{email[..1]}***{email[atIndex..]}";
    }

    /// <summary>
    /// 待出貨/已出貨/已完成/已取消完全由既有狀態衍生，不落地新欄位（UC-ADM-ORDER-01）。
    /// </summary>
    private static string SummaryStatusOf(Order order)
    {
        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            return AdminOrderSummaryStatuses.Cancelled;
        }

        if (order.OrderStatus == OrderStatus.Completed)
        {
            return AdminOrderSummaryStatuses.Completed;
        }

        return order.FulfillmentStatus switch
        {
            FulfillmentStatus.Shipped or FulfillmentStatus.InTransit or FulfillmentStatus.PickupReady or
                FulfillmentStatus.DeliveryFailed or FulfillmentStatus.Returned or FulfillmentStatus.Delivered or
                FulfillmentStatus.PickedUp => AdminOrderSummaryStatuses.Shipped,
            _ => AdminOrderSummaryStatuses.AwaitingShipment,
        };
    }

    private static IReadOnlyList<string> BadgesOf(Order order)
    {
        var badges = new List<string>();
        if (order.OrderRefundStatus == OrderRefundStatus.PartiallyRefunded)
        {
            badges.Add(AdminOrderBadges.PartiallyRefunded);
        }
        else if (order.OrderRefundStatus == OrderRefundStatus.Refunded)
        {
            badges.Add(AdminOrderBadges.Refunded);
        }

        if (order.OrderStatus == OrderStatus.PendingPayment &&
            order.PaymentDueAtUtc is { } dueAtUtc &&
            dueAtUtc < DateTime.UtcNow)
        {
            badges.Add(AdminOrderBadges.PaymentOverdue);
        }

        return badges;
    }

    /// <summary>
    /// 只開放 狀態機設計.md 確認由管理員直接觸發的 OrderStatus 轉移（見
    /// AdminOrderActions 上的說明）；Completed／已由付款觸發的 Confirmed 不開放成手動按鈕。
    /// </summary>
    private static IReadOnlyList<string> AvailableActionsOf(Order order) => order.OrderStatus switch
    {
        OrderStatus.PendingPayment => [AdminOrderActions.Cancel],
        OrderStatus.Confirmed => [AdminOrderActions.StartProcessing, AdminOrderActions.Cancel],
        OrderStatus.Processing => [AdminOrderActions.Cancel],
        _ => [],
    };

    private static HashSet<string> NormalizeAndValidate(
        IReadOnlyList<string>? values,
        IReadOnlyList<string> allowed,
        string parameterName)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var normalized = values.Select(value => value.Trim()).ToHashSet(StringComparer.Ordinal);
        var unknown = normalized.Except(allowed, StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ValidationFailed,
                $"Unknown {parameterName} value(s): {string.Join(", ", unknown)}.");
        }

        return normalized;
    }

    private static IQueryable<Order> ApplySummaryStatusFilter(IQueryable<Order> query, HashSet<string> summarySet)
    {
        if (summarySet.Count == 0)
        {
            return query;
        }

        var includeCancelled = summarySet.Contains(AdminOrderSummaryStatuses.Cancelled);
        var includeCompleted = summarySet.Contains(AdminOrderSummaryStatuses.Completed);
        var includeShipped = summarySet.Contains(AdminOrderSummaryStatuses.Shipped);
        var includeAwaitingShipment = summarySet.Contains(AdminOrderSummaryStatuses.AwaitingShipment);

        return query.Where(order =>
            (includeCancelled && order.OrderStatus == OrderStatus.Cancelled) ||
            (includeCompleted && order.OrderStatus == OrderStatus.Completed) ||
            (includeShipped && order.OrderStatus != OrderStatus.Cancelled &&
                order.OrderStatus != OrderStatus.Completed &&
                (order.FulfillmentStatus == FulfillmentStatus.Shipped ||
                 order.FulfillmentStatus == FulfillmentStatus.InTransit ||
                 order.FulfillmentStatus == FulfillmentStatus.PickupReady ||
                 order.FulfillmentStatus == FulfillmentStatus.DeliveryFailed ||
                 order.FulfillmentStatus == FulfillmentStatus.Returned ||
                 order.FulfillmentStatus == FulfillmentStatus.Delivered ||
                 order.FulfillmentStatus == FulfillmentStatus.PickedUp)) ||
            (includeAwaitingShipment && order.OrderStatus != OrderStatus.Cancelled &&
                order.OrderStatus != OrderStatus.Completed &&
                (order.FulfillmentStatus == FulfillmentStatus.Pending ||
                 order.FulfillmentStatus == FulfillmentStatus.Preparing)));
    }

    private static IQueryable<Order> ApplyBadgeFilter(IQueryable<Order> query, HashSet<string> badgeSet)
    {
        if (badgeSet.Count == 0)
        {
            return query;
        }

        var includePartiallyRefunded = badgeSet.Contains(AdminOrderBadges.PartiallyRefunded);
        var includeRefunded = badgeSet.Contains(AdminOrderBadges.Refunded);
        var includePaymentOverdue = badgeSet.Contains(AdminOrderBadges.PaymentOverdue);
        var now = DateTime.UtcNow;

        return query.Where(order =>
            (includePartiallyRefunded && order.OrderRefundStatus == OrderRefundStatus.PartiallyRefunded) ||
            (includeRefunded && order.OrderRefundStatus == OrderRefundStatus.Refunded) ||
            (includePaymentOverdue && order.OrderStatus == OrderStatus.PendingPayment &&
                order.PaymentDueAtUtc != null && order.PaymentDueAtUtc < now));
    }

    /// <summary>
    /// Cursor 只編碼 CreatedAtUtc（不含 bigint Id，避免違反「不得在 API 暴露 bigint Id」規則）。
    /// 同一毫秒建立多筆訂單時分頁邊界可能重覆/漏掉一筆——後台管理頁在本專案規模下可接受，
    /// 不為此加上額外的 tie-break 欄位。
    /// </summary>
    private static string EncodeCursor(DateTime createdAtUtc) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(createdAtUtc.ToString("O", CultureInfo.InvariantCulture)));

    private static DateTime DecodeCursor(string cursor)
    {
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ValidationFailed,
                "The cursor value is invalid.");
        }
    }
}
