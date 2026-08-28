using System.Globalization;
using System.Text;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// 後台訂單管理（M-08 後半段）。只涵蓋 OrderStatus 維度的管理員操作（開始備貨／人工取消）。
/// 取消只開放 PendingPayment（未付款）——Confirmed／Processing 一律已付款，退款流程尚未串接，
/// 在那之前不提供取消（Alex review，2026-08-28）；PendingPayment 取消會釋放庫存保留、歸還
/// 優惠券名額並寫中央 Audit，跟會員自助取消（EfOrderService）共用同一段
/// OrderCancellationResourceReleaser，避免兩條路徑各自維護一份、其中一份漏掉副作用。
/// </summary>
public sealed class EfAdminOrderService : IAdminOrderService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public EfAdminOrderService(
        DoSelectDbContext dbContext,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
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
            var (afterCreatedAtUtc, afterPublicId) = DecodeCursor(query.Cursor);
            ordersQuery = ordersQuery.Where(order =>
                order.CreatedAtUtc < afterCreatedAtUtc ||
                (order.CreatedAtUtc == afterCreatedAtUtc && order.PublicId.CompareTo(afterPublicId) < 0));
        }

        // API共通規範.md：後台訂單列表固定 `CreatedAtUtc DESC, OrderPublicId DESC`——單一
        // CreatedAtUtc 排序在同毫秒建立多筆訂單時會漏掉或重複邊界那幾筆（Alex review，
        // 2026-08-28），OrderPublicId 補上穩定同值鍵。
        var orders = await ordersQuery
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.PublicId)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = orders.Count > pageSize;
        var page = hasMore ? orders.Take(pageSize).ToList() : orders;
        var nextCursor = hasMore ? EncodeCursor(page[^1].CreatedAtUtc, page[^1].PublicId) : null;

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
        string actorUserId,
        OrderCancellationAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(auditContext);

        var order = await _dbContext.Orders.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == orderPublicId, cancellationToken);
        if (order is null)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ResourceNotFound,
                $"Order '{orderPublicId}' was not found.");
        }

        const string accessPurpose = "OrderFulfillment";
        var adminActor = await ResolveAdminAuditActorAsync(actorUserId, cancellationToken);
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            adminActor,
            AuditActions.OrderRecipientView,
            AuditResourceTypes.Order,
            order.PublicId,
            AuditResult.Success,
            errorCode: null,
            [AuditFieldChange.Code("accessPurpose", null, accessPurpose)],
            reason: accessPurpose,
            auditContext.CorrelationId,
            auditContext.TraceId,
            jobPublicId: null,
            remoteIpAddress: auditContext.RemoteIpAddress));
        await _dbContext.SaveChangesAsync(cancellationToken);

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
        OrderCancellationAuditContext auditContext,
        AdminOrderActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(auditContext);

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

        // Confirmed／Processing 一律已經付款，退款流程還沒串接，在那之前後台不得取消——只剩
        // PendingPayment（未付款）可以取消，直接釋放保留庫存與優惠券名額即可，不涉及退款
        // （Alex review，2026-08-28）。
        if (action == AdminOrderActions.Cancel && order.OrderStatus != OrderStatus.PendingPayment)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.OrderCancellationNotAllowed,
                $"Order '{orderPublicId}' cannot be cancelled from status '{order.OrderStatus}' " +
                "until the refund flow is wired up.");
        }

        _dbContext.Entry(order).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var fromStatus = order.OrderStatus;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var targetStatus = action == AdminOrderActions.StartProcessing
            ? OrderStatus.Processing
            : OrderStatus.Cancelled;

        if (action == AdminOrderActions.Cancel)
        {
            try
            {
                await OrderCancellationResourceReleaser.ReleaseAsync(
                    _dbContext, order, actorUserId, now, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                throw new AdminOrderWriteException(
                    AdminOrderWriteException.ErrorCodes.OrderStateConflict,
                    exception.Message);
            }
        }

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
            auditContext.TraceId));

        if (action == AdminOrderActions.Cancel)
        {
            var adminActor = await ResolveAdminAuditActorAsync(actorUserId, cancellationToken);
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                adminActor,
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
                reason: request.ReasonCode!,
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                remoteIpAddress: auditContext.RemoteIpAddress,
                note: request.Note));
        }

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

    /// <summary>Mirrors EfCompatibilityRuleAdminService.ResolveActorAsync — AuditActor.Create
    /// requires at least one role for an Admin actor, so the audit trail needs the acting
    /// admin's current roles, not just their PublicId.</summary>
    private async Task<AuditActor> ResolveAdminAuditActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var adminPublicId = await _dbContext.AdminProfiles.AsNoTracking()
            .Where(profile => profile.UserId == actorUserId)
            .Select(profile => (Guid?)profile.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
        if (adminPublicId is null)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.OrderStateConflict,
                "The admin profile required for the audit trail was not found.");
        }

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == actorUserId && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return AuditActor.Create(AuditActorType.Admin, adminPublicId.Value, roles);
    }

    /// <summary>OrderStatusHistories.ActorUserId 是內部 Identity 字串 Id，可能來自管理員
    /// （後台 Action）或會員（EfOrderService 的自助取消）。回傳 UserId→PublicId 的對照表，
    /// 查不到（例如訪客或系統事件的 Null）的一律省略，呼叫端遇到查無資料時輸出 Null，
    /// 不讓查詢頁因為單筆歷程資料異常而整體失敗。</summary>
    private async Task<IReadOnlyDictionary<string, Guid>> ResolveActorPublicIdsAsync(
        IEnumerable<string?> actorUserIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = actorUserIds.Where(id => id is not null).Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return new Dictionary<string, Guid>();
        }

        var result = await _dbContext.AdminProfiles.AsNoTracking()
            .Where(profile => distinctIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, profile => profile.PublicId, cancellationToken);

        var stillMissing = distinctIds.Where(id => !result.ContainsKey(id!)).ToArray();
        if (stillMissing.Length > 0)
        {
            var memberMatches = await _dbContext.MemberProfiles.AsNoTracking()
                .Where(profile => stillMissing.Contains(profile.UserId))
                .ToListAsync(cancellationToken);
            foreach (var profile in memberMatches)
            {
                result[profile.UserId] = profile.PublicId;
            }
        }

        return result;
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
            item.LineSubtotal,
            item.DiscountAllocation,
            item.LineTotal,
            item.ReturnableQuantity,
            item.ReturnedQuantity)).ToList();

        var actorPublicIds = await ResolveActorPublicIdsAsync(
            history.Select(entry => entry.ActorUserId), cancellationToken);
        var historyDtos = history.Select(entry => new OrderStatusHistoryDto(
            entry.StateDimension.ToString(),
            entry.FromStatus,
            entry.ToStatus,
            entry.ReasonCode,
            entry.ActorUserId is not null && actorPublicIds.TryGetValue(entry.ActorUserId, out var publicId)
                ? publicId
                : null,
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
    /// Cancel 只在 PendingPayment（未付款）開放——Confirmed／Processing 一律已付款，退款流程
    /// 尚未串接，在那之前不提供取消（Alex review，2026-08-28）。
    /// </summary>
    private static IReadOnlyList<string> AvailableActionsOf(Order order) => order.OrderStatus switch
    {
        OrderStatus.PendingPayment => [AdminOrderActions.Cancel],
        OrderStatus.Confirmed => [AdminOrderActions.StartProcessing],
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
    /// Cursor 編碼 (CreatedAtUtc, OrderPublicId) 這組穩定同值鍵（API共通規範.md：後台訂單列表
    /// 固定 `CreatedAtUtc DESC, OrderPublicId DESC`）。用 PublicId 而非 bigint Id，符合「不得在
    /// API 暴露 bigint Id」規則。
    /// </summary>
    private static string EncodeCursor(DateTime createdAtUtc, Guid publicId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{createdAtUtc.ToString("O", CultureInfo.InvariantCulture)}|{publicId:D}"));

    private static (DateTime CreatedAtUtc, Guid PublicId) DecodeCursor(string cursor)
    {
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = text.Split('|', 2);
            if (parts.Length != 2)
            {
                throw new FormatException("The cursor value is missing the tie-break segment.");
            }

            var createdAtUtc = DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var publicId = Guid.ParseExact(parts[1], "D");
            return (createdAtUtc, publicId);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new AdminOrderWriteException(
                AdminOrderWriteException.ErrorCodes.ValidationFailed,
                "The cursor value is invalid.");
        }
    }
}
