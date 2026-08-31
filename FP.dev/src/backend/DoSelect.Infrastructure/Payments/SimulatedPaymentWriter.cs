using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Outbox;
using DoSelect.Application.Payments;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Payments;

/// <summary>
/// 在共用冪等執行器持有的交易裡完成一筆模擬付款。
/// </summary>
/// <remarks>
/// <para>
/// <b>付款嘗試的狀態轉換與訂單的付款投影在同一個交易內</b>（Issue #65 C1）。
/// 兩者分開寫會產生一個中間狀態：付款嘗試已經是 <c>Paid</c>、訂單卻還沒付款，
/// 而那正是 <c>Order.PaidAmount</c> 目前為止從來沒有被寫過的原因。
/// </para>
/// <para>
/// 隔離等級用 <c>Serializable</c>。決策依據的快照（付款嘗試狀態、訂單狀態與金額）
/// 是在交易內重新讀的，但 <c>ReadCommitted</c> 之下另一個交易仍可能在讀完之後
/// 改掉它們。RowVersion 是最後一道防線，隔離等級是第一道。
/// </para>
/// </remarks>
public sealed class SimulatedPaymentWriter : ISimulatedPaymentWriter
{
    private const int OkStatusCode = 200;

    private readonly DoSelectDbContext _context;
    private readonly CompleteSimulatedPaymentService _planner;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly IAuditWriter _auditWriter;
    private readonly IOutboxWriter _outboxWriter;
    private readonly TimeProvider _timeProvider;

    public SimulatedPaymentWriter(
        DoSelectDbContext context,
        CompleteSimulatedPaymentService planner,
        IIdempotencyExecutor idempotencyExecutor,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(outboxWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _planner = planner;
        _idempotencyExecutor = idempotencyExecutor;
        _auditWriter = auditWriter;
        _outboxWriter = outboxWriter;
        _timeProvider = timeProvider;
    }

    public async Task<IdempotencyExecutionResult<PaymentAttemptDto>> CompleteAsync(
        CompleteSimulatedPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = await ResolveActorAsync(command.Actor, cancellationToken);

        // Request Hash 涵蓋付款嘗試與結果：同一把 simulationKey 換一個 outcome
        // 是 Payload 衝突，不是重播。
        var idempotencyCommand = IdempotencyCommand.Create(
            actor.IdempotencyScope,
            SimulatedPaymentWriteConstants.Operation,
            command.SimulationKey,
            new
            {
                command.PaymentAttemptPublicId,
                Outcome = command.Outcome.ToString(),
            });

        try
        {
            return await _idempotencyExecutor.ExecuteAsync(
                idempotencyCommand,
                handler: ct => CompleteOnceAsync(command, actor, ct),
                replayFactory: ReplayAsync,
                cancellationToken,
                IsolationLevel.Serializable);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 另一個交易在我們讀完之後改掉了付款嘗試或訂單。
            throw DomainProblemException.Conflict(
                PaymentErrorCodes.ConcurrencyConflict,
                "The payment changed while it was being completed. Reload it and try again.");
        }
    }

    private async Task<IdempotencyResponse<PaymentAttemptDto>> CompleteOnceAsync(
        CompleteSimulatedPaymentCommand command,
        ResolvedActor actor,
        CancellationToken cancellationToken)
    {
        // 追蹤查詢：這兩個實體等一下要被改寫。
        var attempt = await _context.PaymentAttempts.SingleOrDefaultAsync(
            candidate => candidate.PublicId == command.PaymentAttemptPublicId,
            cancellationToken)
            ?? throw DomainProblemException.NotFound("The payment attempt was not found.");

        var order = await _context.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == attempt.OrderId,
            cancellationToken)
            ?? throw DomainProblemException.NotFound("The order was not found.");

        // 只有訂單的擁有者能模擬它的付款。回 404 而不是 403 —— 區分「不存在」與
        // 「不是你的」等於告訴外人這個 id 存在。
        if (!OwnsOrder(order, command.Actor))
        {
            throw DomainProblemException.NotFound("The payment attempt was not found.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var decision = _planner.Decide(
            new SimulatedPaymentSnapshot(
                attempt.Id,
                attempt.Method,
                attempt.Status,
                attempt.Amount,
                attempt.InstructionExpiresAtUtc,
                order.Id,
                order.OrderStatus,
                order.PaymentStatus,
                order.PaidAmount,
                order.GrandTotal,
                order.PaymentDueAtUtc),
            command.Outcome,
            nowUtc);

        if (decision.Plan is not { } plan)
        {
            throw DomainProblemException.Conflict(
                decision.ErrorCode!,
                DescribeRejection(decision.ErrorCode!));
        }

        foreach (var next in plan.AttemptTransitions)
        {
            attempt.Transition(next, nowUtc, plan.FailureCode);
        }

        var previousOrderStatus = order.OrderStatus;
        var previousPaymentStatus = order.PaymentStatus;
        // 同一個交易裡的第二次寫入。付款嘗試進了終態、訂單卻沒跟上，
        // 就是這支端點要消滅的那個中間狀態。
        order.ApplyPaymentProjection(plan.OrderPaymentStatus, plan.OrderPaidAmount, nowUtc);
        if (plan.OrderStatusTransition is { } nextOrderStatus)
        {
            order.ChangeOrderStatus(nextOrderStatus, nowUtc);
        }

        AddPaymentEvent(attempt, command, nowUtc);
        AddHistories(
            order,
            previousOrderStatus,
            previousPaymentStatus,
            plan,
            actor.HistoryActorUserId,
            command.TraceId,
            nowUtc);
        AddOutboxMessages(
            attempt,
            order,
            command.Outcome,
            actor.MemberPublicId,
            command.CorrelationId,
            nowUtc);
        AddAudit(attempt, actor.AuditActor, plan, command);

        await _context.SaveChangesAsync(cancellationToken);

        var dto = ToDto(attempt);
        return new IdempotencyResponse<PaymentAttemptDto>(
            OkStatusCode,
            dto,
            JsonSerializer.Serialize(new SimulatedPaymentReceipt(attempt.PublicId)));
    }

    private async Task<PaymentAttemptDto> ReplayAsync(
        StoredIdempotencyResponse stored,
        CancellationToken cancellationToken)
    {
        var receipt = JsonSerializer.Deserialize<SimulatedPaymentReceipt>(stored.ResponseSummary)
            ?? throw new InvalidOperationException("The stored simulated payment receipt is invalid.");

        var attempt = await _context.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.PublicId == receipt.PaymentAttemptPublicId,
            cancellationToken)
            ?? throw new InvalidOperationException("The stored payment attempt no longer exists.");

        // 重播回的是付款嘗試「現在」的樣子，不是當初的快照 —— 讀回同一個資源，
        // 而不是把一份可能已經過期的內容當成事實。
        return ToDto(attempt);
    }

    private static PaymentAttemptDto ToDto(PaymentAttempt attempt) =>
        new(
            attempt.PublicId,
            attempt.Method,
            attempt.Status,
            attempt.Amount,
            OrderCurrency,
            ToInstruction(attempt),
            attempt.CreatedAtUtc,
            attempt.PaidAtUtc,
            attempt.RowVersion);

    private async Task<ResolvedActor> ResolveActorAsync(
        SimulatedPaymentActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor is SimulatedPaymentActor.Guest guest)
        {
            if (guest.TokenPublicId == Guid.Empty || guest.AuthorizedOrderPublicId == Guid.Empty)
            {
                throw DomainProblemException.NotFound("The payment attempt was not found.");
            }

            return new ResolvedActor(
                IdempotencyActorScope.ForGuestOrderAccess(guest.TokenPublicId),
                AuditActor.Create(AuditActorType.Guest, guest.TokenPublicId, roles: []),
                HistoryActorUserId: null,
                MemberPublicId: null);
        }

        var member = (SimulatedPaymentActor.Member)actor;
        var memberPublicId = await _context.Users.AsNoTracking()
            .Where(user => user.Id == member.UserId)
            .Select(user => user.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
        if (memberPublicId == Guid.Empty)
        {
            throw DomainProblemException.NotFound("The payment attempt was not found.");
        }

        return new ResolvedActor(
            IdempotencyActorScope.ForUser(memberPublicId),
            AuditActor.Create(AuditActorType.Member, memberPublicId, roles: []),
            member.UserId,
            memberPublicId);
    }

    private static bool OwnsOrder(Order order, SimulatedPaymentActor actor) => actor switch
    {
        SimulatedPaymentActor.Member member =>
            string.Equals(order.MemberUserId, member.UserId, StringComparison.Ordinal),
        SimulatedPaymentActor.Guest guest =>
            order.MemberUserId is null &&
            order.GuestEmailNormalized is not null &&
            order.PublicId == guest.AuthorizedOrderPublicId,
        _ => false,
    };

    private void AddPaymentEvent(
        PaymentAttempt attempt,
        CompleteSimulatedPaymentCommand command,
        DateTime nowUtc)
    {
        var canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            attempt.PublicId,
            Outcome = command.Outcome.ToString(),
            command.SimulationKey,
        });
        var payloadHash = SHA256.HashData(canonicalPayload);
        var externalEventId = $"simulation:{Convert.ToHexString(payloadHash).ToLowerInvariant()}";
        var paymentEvent = new PaymentEvent(
            Guid.CreateVersion7(),
            attempt.Id,
            externalEventId,
            NotificationKey(command.Outcome),
            new DateTimeOffset(nowUtc),
            nowUtc,
            payloadHash,
            JsonSerializer.Serialize(new
            {
                outcome = command.Outcome.ToString(),
                source = "demo",
            }),
            nowUtc);
        paymentEvent.MarkProcessed();
        _context.PaymentEvents.Add(paymentEvent);
    }

    private void AddHistories(
        Order order,
        OrderStatus previousOrderStatus,
        PaymentStatus previousPaymentStatus,
        SimulatedPaymentPlan plan,
        string? actorUserId,
        string traceId,
        DateTime nowUtc)
    {
        var reason = HistoryReason(plan.OrderPaymentStatus);
        _context.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.PaymentStatus,
            previousPaymentStatus.ToString(),
            order.PaymentStatus.ToString(),
            reason,
            actorUserId,
            nowUtc,
            traceId));

        if (plan.OrderStatusTransition is not null)
        {
            _context.OrderStatusHistories.Add(new OrderStatusHistory(
                Guid.CreateVersion7(),
                order.Id,
                OrderStateDimension.OrderStatus,
                previousOrderStatus.ToString(),
                order.OrderStatus.ToString(),
                reason,
                actorUserId,
                nowUtc,
                traceId));
        }
    }

    private void AddOutboxMessages(
        PaymentAttempt attempt,
        Order order,
        SimulatedPaymentOutcome outcome,
        Guid? memberPublicId,
        string correlationId,
        DateTime nowUtc)
    {
        var notificationId = Guid.CreateVersion7();
        var key = attempt.Status switch
        {
            PaymentAttemptStatus.Paid => "payment.succeeded",
            PaymentAttemptStatus.Failed => "payment.failed",
            PaymentAttemptStatus.Expired => "payment.expired",
            PaymentAttemptStatus.Cancelled => "payment.cancelled",
            _ => throw new InvalidOperationException("The simulated payment is not terminal."),
        };
        _outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditResourceTypes.PaymentAttempt,
            attempt.PublicId,
            new EmailNotificationRequestedV1(
                notificationId,
                key,
                "payment.customer",
                AuditResourceTypes.PaymentAttempt,
                attempt.PublicId,
                "zh-TW",
                1),
            nowUtc,
            nowUtc,
            correlationId));

        if (memberPublicId is { } memberId)
        {
            _outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.PaymentAttempt,
                attempt.PublicId,
                new InAppNotificationRequestedV1(
                    Guid.CreateVersion7(),
                    memberId,
                    key,
                    AuditResourceTypes.PaymentAttempt,
                    attempt.PublicId,
                    "zh-TW",
                    1),
                nowUtc,
                nowUtc,
                correlationId));
        }

        if (outcome == SimulatedPaymentOutcome.Succeeded)
        {
            _outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.Order,
                order.PublicId,
                new SimulatedInvoiceRequestedV1(order.PublicId),
                nowUtc,
                nowUtc,
                correlationId));
        }
    }

    private void AddAudit(
        PaymentAttempt attempt,
        AuditActor actor,
        SimulatedPaymentPlan plan,
        CompleteSimulatedPaymentCommand command)
    {
        var changes = new List<AuditFieldChange>
        {
            AuditFieldChange.Changed("attemptStatus"),
            AuditFieldChange.Changed("paymentStatus"),
            AuditFieldChange.Changed("paymentEvent"),
            AuditFieldChange.Changed("notification"),
        };
        if (plan.OrderStatusTransition is not null)
        {
            changes.Add(AuditFieldChange.Changed("orderStatus"));
        }

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.PaymentSimulateComplete,
            AuditResourceTypes.PaymentAttempt,
            attempt.PublicId,
            AuditResult.Success,
            errorCode: null,
            changes,
            "simulated_completion",
            command.CorrelationId,
            command.TraceId,
            jobPublicId: null,
            command.ClientIpAddress));
    }

    private static string NotificationKey(SimulatedPaymentOutcome outcome) => outcome switch
    {
        SimulatedPaymentOutcome.Succeeded => "payment.succeeded",
        SimulatedPaymentOutcome.Failed => "payment.failed",
        SimulatedPaymentOutcome.Expired => "payment.expired",
        SimulatedPaymentOutcome.Cancelled => "payment.cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static string HistoryReason(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "payment_simulation_succeeded",
        PaymentStatus.Failed => "payment_simulation_failed",
        PaymentStatus.Expired => "payment_simulation_expired",
        PaymentStatus.Cancelled => "payment_simulation_cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <remarks>
    /// 即時付款沒有要顯示給使用者照著做的指示，所以回 <c>null</c> 而不是一個空殼。
    /// ATM 與超商代碼才有代碼要呈現。
    /// </remarks>
    private static PaymentInstructionDto? ToInstruction(PaymentAttempt attempt)
    {
        var kind = PaymentMethodPolicy.KindOf(attempt.Method);
        if (kind != PaymentSettlementKind.Deferred)
        {
            return null;
        }

        return new PaymentInstructionDto(
            attempt.Method.ToString(),
            MaskedAccount: null,
            attempt.ExternalReference,
            attempt.InstructionExpiresAtUtc);
    }

    private static string DescribeRejection(string errorCode) => errorCode switch
    {
        PaymentErrorCodes.PaymentAttemptExpired =>
            "The payment instruction expired.",
        PaymentErrorCodes.OrderPaymentDeadlineExpired =>
            "The order payment deadline passed.",
        _ => "The payment is not in a state that can be completed.",
    };

    private const string OrderCurrency = "TWD";

    private sealed record SimulatedPaymentReceipt(Guid PaymentAttemptPublicId);

    private sealed record ResolvedActor(
        IdempotencyActorScope IdempotencyScope,
        AuditActor AuditActor,
        string? HistoryActorUserId,
        Guid? MemberPublicId);
}
