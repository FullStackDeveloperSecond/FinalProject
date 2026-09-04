using System.Security.Cryptography;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;
using DoSelect.Application.Outbox;
using DoSelect.Application.Payments;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// M-11 物流狀態命令（組長 2026-09-04 裁定 A1～D1）。
///
/// 整筆命令交給共用的 <see cref="IIdempotencyExecutor"/>：它擁有那一個 SQL Server Transaction，
/// 同鍵同 payload 重播原結果、不同 payload 回 <c>idempotency_payload_conflict</c>（A1）。交易裡面依序：
/// Shipment 狀態轉移、ShipmentStatusHistory、Order 的 Fulfillment 投影與 OrderStatusHistory、
/// 交付完成時的 COD 收款（<see cref="CashOnDeliveryCompletionService"/> 的計畫）、Order Completed、
/// 通知／付款事件／模擬發票 Outbox、中央 Audit——全部同一次 SaveChanges，任何一步失敗整體回滾（B1）。
/// </summary>
public sealed class EfShipmentStatusService : IShipmentStatusService
{
    public const string Operation = "shipping.status";

    private readonly DoSelectDbContext _dbContext;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IAuditWriter _auditWriter;
    private readonly IAdminOrderService _adminOrderService;
    private readonly CashOnDeliveryCompletionService _cashOnDelivery;
    private readonly TimeProvider _timeProvider;

    public EfShipmentStatusService(
        DoSelectDbContext dbContext,
        IIdempotencyExecutor idempotencyExecutor,
        IOutboxWriter outboxWriter,
        IAuditWriter auditWriter,
        IAdminOrderService adminOrderService,
        CashOnDeliveryCompletionService cashOnDelivery,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _idempotencyExecutor = idempotencyExecutor;
        _outboxWriter = outboxWriter;
        _auditWriter = auditWriter;
        _adminOrderService = adminOrderService;
        _cashOnDelivery = cashOnDelivery;
        _timeProvider = timeProvider;
    }

    public async Task<ShipmentStatusResult> ExecuteAsync(
        ShipmentStatusCommand command,
        string adminUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(auditContext);

        var action = (command.Action ?? string.Empty).Trim();
        if (!ShipmentStatusActions.TryGetTarget(action, out var target))
        {
            throw DomainProblemException.Validation(
                $"'{command.Action}' is not a shipment status action. Valid values: {string.Join(", ", ShipmentStatusActions.All)}.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw DomainProblemException.Validation("Idempotency-Key is required.");
        }

        if (command.ShipmentRowVersion is not { Length: > 0 })
        {
            throw DomainProblemException.Validation("shipmentRowVersion is required.");
        }

        var reasonCode = string.IsNullOrWhiteSpace(command.ReasonCode) ? null : command.ReasonCode.Trim();
        if (reasonCode is null && ShipmentStatusActions.RequiresReason(action))
        {
            throw DomainProblemException.Validation($"A reasonCode is required for '{action}'.");
        }

        if (reasonCode is not null && !ShipmentStatusReasonCodes.All.Contains(reasonCode, StringComparer.Ordinal))
        {
            throw DomainProblemException.Validation(
                $"'{reasonCode}' is not a shipment status reason code. Valid values: {string.Join(", ", ShipmentStatusReasonCodes.All)}.");
        }

        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note is { Length: > 500 })
        {
            throw DomainProblemException.Validation("note cannot exceed 500 characters.");
        }

        // 冪等的 Actor Scope 要在交易外算（Executor 自己開交易）；角色在交易內重查（AuthorizeActorAsync）。
        var actor = await ResolveActorAsync(adminUserId, cancellationToken);

        var idempotencyCommand = IdempotencyCommand.Create(
            IdempotencyActorScope.ForAdmin(actor.PublicId!.Value),
            Operation,
            command.IdempotencyKey.Trim(),
            new
            {
                command.ShipmentPublicId,
                Action = action,
                ShipmentRowVersion = Convert.ToBase64String(command.ShipmentRowVersion),
                ReasonCode = reasonCode,
                Note = note,
            });

        var execution = await _idempotencyExecutor.ExecuteAsync(
            idempotencyCommand,
            handler: token => ExecuteOnceAsync(command.ShipmentPublicId, action, target, command.ShipmentRowVersion, reasonCode, note, adminUserId, actor, auditContext, token),
            replayFactory: (stored, token) => ReplayAsync(stored, token),
            cancellationToken);

        return new ShipmentStatusResult(execution.Body, execution.IsReplay);
    }

    private async Task<IdempotencyResponse<AdminOrderDto>> ExecuteOnceAsync(
        Guid shipmentPublicId,
        string action,
        FulfillmentStatus target,
        byte[] expectedRowVersion,
        string? reasonCode,
        string? note,
        string adminUserId,
        AuditActor actor,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Shipments
            .SingleOrDefaultAsync(candidate => candidate.PublicId == shipmentPublicId, cancellationToken)
            ?? throw DomainProblemException.NotFound("The shipment was not found.");

        // 先比對再交給資料庫：後面一連串寫入都以這個 RowVersion 為前提，過期就直接 409。
        if (!shipment.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The shipment was updated by someone else. Reload and try again.");
        }

        _dbContext.Entry(shipment).Property(candidate => candidate.RowVersion).OriginalValue = expectedRowVersion;

        var order = await _dbContext.Orders
            .SingleAsync(candidate => candidate.Id == shipment.OrderId, cancellationToken);
        var shippingMethodKind = await _dbContext.ShippingMethods.AsNoTracking()
            .Where(method => method.Id == shipment.ShippingMethodId)
            .Select(method => method.Kind)
            .SingleAsync(cancellationToken);

        // B1：宅配才允許 Delivered；超取才允許 PickupReady／PickedUp。狀態機的邊由實體守，配送方式
        // 由這裡守，兩者都是 shipping_status_transition_invalid——對管理員來說都是「這一步不合法」。
        if (!ShipmentStatusPolicy.IsAllowedForMethod(target, shippingMethodKind))
        {
            throw DomainProblemException.Conflict(
                ShippingErrorCodes.ShippingStatusTransitionInvalid,
                $"'{action}' is not allowed for shipping method kind '{shippingMethodKind}'.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var previousShipmentStatus = shipment.Status;
        var previousFulfillmentStatus = order.FulfillmentStatus;
        try
        {
            shipment.ChangeStatus(target, now);
        }
        catch (InvalidOperationException exception)
        {
            throw DomainProblemException.Conflict(ShippingErrorCodes.ShippingStatusTransitionInvalid, exception.Message);
        }

        _dbContext.ShipmentStatusHistories.Add(new ShipmentStatusHistory(
            Guid.CreateVersion7(),
            shipment.Id,
            previousShipmentStatus,
            target,
            externalEventId: null,
            now,
            adminUserId));

        order.ApplyFulfillmentProjection(target, now);
        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.FulfillmentStatus,
            previousFulfillmentStatus.ToString(),
            target.ToString(),
            reasonCode,
            adminUserId,
            now,
            auditContext.TraceId));

        var auditChanges = new List<AuditFieldChange>
        {
            AuditFieldChange.Code("fulfillmentStatus", previousFulfillmentStatus.ToString(), target.ToString()),
        };
        if (reasonCode is not null)
        {
            auditChanges.Add(AuditFieldChange.Code("reasonCode", null, reasonCode));
        }

        if (ShipmentStatusPolicy.IsDeliveryCompletion(target))
        {
            var memberPublicId = await ResolveMemberPublicIdAsync(order, cancellationToken);
            var previousPaymentStatus = order.PaymentStatus;
            var previousOrderStatus = order.OrderStatus;

            // B1：進入 Delivered／PickedUp 時，COD 在同一交易套用 CashOnDeliveryCompletionService 的計畫。
            var codAttempt = await FindCashOnDeliveryAttemptAsync(order.Id, cancellationToken);
            if (codAttempt is not null)
            {
                ApplyCashOnDeliveryCompletion(codAttempt, order, shipment, target, memberPublicId, adminUserId, auditContext, now);
            }

            // 付款條件完成後同步把 Order 推進 Completed（Confirmed 先經 Processing，狀態機只認單步邊）。
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                AdvanceOrderToCompleted(order, adminUserId, auditContext.TraceId, now);
            }

            if (order.PaymentStatus != previousPaymentStatus)
            {
                // 中央稽核的安全代碼規則不收含 "payment" 的值（AwaitingPayment 會被拒），所以 paymentStatus
                // 只記「改了」；前後值在同一交易的 OrderStatusHistory（PaymentStatus 維度）與 PaymentEvent 裡。
                auditChanges.Add(AuditFieldChange.Changed("paymentStatus"));
            }

            if (order.OrderStatus != previousOrderStatus)
            {
                auditChanges.Add(AuditFieldChange.Code("orderStatus", previousOrderStatus.ToString(), order.OrderStatus.ToString()));
            }

            AddShipmentNotifications(shipment, memberPublicId, auditContext.CorrelationId, now);
        }
        else
        {
            var memberPublicId = await ResolveMemberPublicIdAsync(order, cancellationToken);
            AddShipmentNotifications(shipment, memberPublicId, auditContext.CorrelationId, now);
        }

        // D1：Resource 維持 Order，比照 shipment.create_label／mark_shipped；Note 只留在管理端 Audit。
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActionOf(target),
            AuditResourceTypes.Order,
            order.PublicId,
            AuditResult.Success,
            errorCode: null,
            auditChanges,
            reason: reasonCode ?? "admin_shipment_status",
            auditContext.CorrelationId,
            auditContext.TraceId,
            jobPublicId: null,
            auditContext.RemoteIpAddress,
            note: note));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The shipment or its order was updated by someone else. Reload and try again.");
        }

        // 同一個 DbContext、同一筆交易裡讀回更新後的 AdminOrderDto（C1：命令成功回傳更新的 AdminOrderDto）。
        var dto = await _adminOrderService.GetAsync(order.PublicId, cancellationToken);
        return new IdempotencyResponse<AdminOrderDto>(
            StatusCodes.Ok,
            dto,
            JsonSerializer.Serialize(new ShipmentStatusReceipt(order.PublicId, shipment.PublicId, target.ToString())));
    }

    private async Task<AdminOrderDto> ReplayAsync(StoredIdempotencyResponse stored, CancellationToken cancellationToken)
    {
        var receipt = JsonSerializer.Deserialize<ShipmentStatusReceipt>(stored.ResponseSummary)
            ?? throw new InvalidOperationException("The stored shipment status receipt is invalid.");
        return await _adminOrderService.GetAsync(receipt.OrderPublicId, cancellationToken);
    }

    /// <summary>
    /// 只認訂單上仍在等收款的 COD 嘗試；非 COD（預付）訂單沒有這種嘗試，回 null 就跳過收款
    /// （E1：非 COD 不得誤改付款）。
    /// </summary>
    private async Task<PaymentAttempt?> FindCashOnDeliveryAttemptAsync(long orderId, CancellationToken cancellationToken) =>
        await _dbContext.PaymentAttempts
            .Where(attempt => attempt.OrderId == orderId && attempt.Method == PaymentMethod.CashOnDelivery)
            .OrderByDescending(attempt => attempt.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private void ApplyCashOnDeliveryCompletion(
        PaymentAttempt attempt,
        Order order,
        Shipment shipment,
        FulfillmentStatus target,
        Guid? memberPublicId,
        string adminUserId,
        AuditRequestContext auditContext,
        DateTime now)
    {
        if (attempt.Status == PaymentAttemptStatus.Paid && order.PaymentStatus == PaymentStatus.Paid)
        {
            // 已收過款（例如再次配送後再次送達）：不重複收款、不重複事件與發票。
            return;
        }

        var decision = _cashOnDelivery.Decide(new CashOnDeliveryCompletionSnapshot(
            attempt.Id,
            attempt.Method,
            attempt.Status,
            attempt.Amount,
            order.Id,
            order.OrderStatus,
            order.PaymentStatus,
            order.PaidAmount,
            order.GrandTotal,
            target));
        if (decision.Plan is not { } plan)
        {
            // B1：任何一步失敗整體回滾——收不了款，交付就不算完成。
            throw DomainProblemException.Conflict(
                decision.ErrorCode!,
                "The cash-on-delivery payment cannot be completed for this order.");
        }

        foreach (var next in plan.AttemptTransitions)
        {
            attempt.Transition(next, now);
        }

        var previousPaymentStatus = order.PaymentStatus;
        order.ApplyPaymentProjection(plan.OrderPaymentStatus, plan.OrderPaidAmount, now);
        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.PaymentStatus,
            previousPaymentStatus.ToString(),
            order.PaymentStatus.ToString(),
            "cod_collected_on_delivery",
            adminUserId,
            now,
            auditContext.TraceId));

        // 付款事件：以物流單＋目標狀態決定 externalEventId，同一次交付永遠是同一個事件識別。
        var canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            attempt.PublicId,
            ShipmentPublicId = shipment.PublicId,
            Target = target.ToString(),
        });
        var payloadHash = SHA256.HashData(canonicalPayload);
        var paymentEvent = new PaymentEvent(
            Guid.CreateVersion7(),
            attempt.Id,
            $"cod-delivery:{shipment.PublicId:N}:{target}",
            "payment.succeeded",
            new DateTimeOffset(now),
            now,
            payloadHash,
            JsonSerializer.Serialize(new { outcome = "Succeeded", source = "shipment", target = target.ToString() }),
            now);
        paymentEvent.MarkProcessed();
        _dbContext.PaymentEvents.Add(paymentEvent);

        // 付款通知與模擬發票 Outbox：形狀比照 SimulatedPaymentWriter，發票只在真的收款這一次排。
        _outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditResourceTypes.PaymentAttempt,
            attempt.PublicId,
            new EmailNotificationRequestedV1(
                Guid.CreateVersion7(),
                "payment.succeeded",
                "payment.customer",
                AuditResourceTypes.PaymentAttempt,
                attempt.PublicId,
                "zh-TW",
                1),
            now,
            now,
            auditContext.CorrelationId));
        if (memberPublicId is { } memberId)
        {
            _outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.PaymentAttempt,
                attempt.PublicId,
                new InAppNotificationRequestedV1(
                    Guid.CreateVersion7(),
                    memberId,
                    "payment.succeeded",
                    AuditResourceTypes.PaymentAttempt,
                    attempt.PublicId,
                    "zh-TW",
                    1),
                now,
                now,
                auditContext.CorrelationId));
        }

        if (plan.RequestSimulatedInvoice)
        {
            _outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.Order,
                order.PublicId,
                new SimulatedInvoiceRequestedV1(order.PublicId),
                now,
                now,
                auditContext.CorrelationId));
        }
    }

    private void AdvanceOrderToCompleted(Order order, string adminUserId, string traceId, DateTime now)
    {
        if (order.OrderStatus is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            return;
        }

        var steps = order.OrderStatus == OrderStatus.Confirmed
            ? new[] { OrderStatus.Processing, OrderStatus.Completed }
            : new[] { OrderStatus.Completed };
        foreach (var step in steps)
        {
            var from = order.OrderStatus;
            order.ChangeOrderStatus(step, now);
            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
                Guid.CreateVersion7(),
                order.Id,
                OrderStateDimension.OrderStatus,
                from.ToString(),
                step.ToString(),
                "delivery_completed",
                adminUserId,
                now,
                traceId));
        }
    }

    private void AddShipmentNotifications(Shipment shipment, Guid? memberPublicId, string correlationId, DateTime now)
    {
        // 通知沿用既有 shipment.updated 模板與 shipment.customer 用途（Resource 是 Shipment，
        // 通知消費端由物流單找回訂單與收件人）。
        _outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            "Shipment",
            shipment.PublicId,
            new EmailNotificationRequestedV1(
                Guid.CreateVersion7(),
                "shipment.updated",
                "shipment.customer",
                "Shipment",
                shipment.PublicId,
                "zh-TW",
                1),
            now,
            now,
            correlationId));
        if (memberPublicId is { } memberId)
        {
            _outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                "Shipment",
                shipment.PublicId,
                new InAppNotificationRequestedV1(
                    Guid.CreateVersion7(),
                    memberId,
                    "shipment.updated",
                    "Shipment",
                    shipment.PublicId,
                    "zh-TW",
                    1),
                now,
                now,
                correlationId));
        }
    }

    private async Task<Guid?> ResolveMemberPublicIdAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.MemberUserId is null)
        {
            return null;
        }

        var publicId = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == order.MemberUserId)
            .Select(user => user.PublicId)
            .FirstOrDefaultAsync(cancellationToken);
        return publicId == Guid.Empty ? null : publicId;
    }

    /// <summary>與 EfBatchShipmentService.ResolveActorAsync 同形：稽核與冪等 Actor Scope 都靠這個身分。</summary>
    private async Task<AuditActor> ResolveActorAsync(string adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.Forbidden("The administrator identity is invalid.");

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.OrderManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw DomainProblemException.Forbidden("The administrator is not allowed to update shipments.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    private static string AuditActionOf(FulfillmentStatus target) => target switch
    {
        FulfillmentStatus.InTransit => AuditActions.ShipmentMarkInTransit,
        FulfillmentStatus.Delivered => AuditActions.ShipmentMarkDelivered,
        FulfillmentStatus.PickupReady => AuditActions.ShipmentMarkPickupReady,
        FulfillmentStatus.PickedUp => AuditActions.ShipmentMarkPickedUp,
        FulfillmentStatus.DeliveryFailed => AuditActions.ShipmentMarkDeliveryFailed,
        FulfillmentStatus.Returned => AuditActions.ShipmentMarkReturned,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static class StatusCodes
    {
        public const int Ok = 200;
    }

    private sealed record ShipmentStatusReceipt(Guid OrderPublicId, Guid ShipmentPublicId, string Status);
}
