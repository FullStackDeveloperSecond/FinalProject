using System.Data;
using System.Text.Json;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Payments;
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
    private readonly TimeProvider _timeProvider;

    public SimulatedPaymentWriter(
        DoSelectDbContext context,
        CompleteSimulatedPaymentService planner,
        IIdempotencyExecutor idempotencyExecutor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _planner = planner;
        _idempotencyExecutor = idempotencyExecutor;
        _timeProvider = timeProvider;
    }

    public async Task<IdempotencyExecutionResult<PaymentAttemptDto>> CompleteAsync(
        CompleteSimulatedPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 冪等的 Actor Scope 用會員的 PublicId，不是 Identity 的字串 Id ——
        // 跟 EfBuildListService 一樣在這一層換，Controller 不必知道這件事。
        var memberPublicId = await _context.Users
            .Where(user => user.Id == command.MemberUserId)
            .Select(user => user.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
        if (memberPublicId == Guid.Empty)
        {
            throw DomainProblemException.NotFound("The payment attempt was not found.");
        }

        // Request Hash 涵蓋付款嘗試與結果：同一把 simulationKey 換一個 outcome
        // 是 Payload 衝突，不是重播。
        var idempotencyCommand = IdempotencyCommand.Create(
            IdempotencyActorScope.ForUser(memberPublicId),
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
                handler: ct => CompleteOnceAsync(command, ct),
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
        if (!string.Equals(order.MemberUserId, command.MemberUserId, StringComparison.Ordinal))
        {
            throw DomainProblemException.NotFound("The payment attempt was not found.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var decision = _planner.Decide(
            new SimulatedPaymentSnapshot(
                attempt.Id,
                attempt.Status,
                attempt.Amount,
                attempt.InstructionExpiresAtUtc,
                order.Id,
                order.OrderStatus,
                order.PaymentStatus,
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

        // 同一個交易裡的第二次寫入。付款嘗試進了終態、訂單卻沒跟上，
        // 就是這支端點要消滅的那個中間狀態。
        order.ApplyPaymentProjection(plan.OrderPaymentStatus, plan.OrderPaidAmount, nowUtc);

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
}
