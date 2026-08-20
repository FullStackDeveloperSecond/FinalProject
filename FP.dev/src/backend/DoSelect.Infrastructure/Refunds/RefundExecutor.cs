using System.Data;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 在單一交易內完成退款執行：核對狀態與可退款餘額 → 條件更新退款 → 提交。
/// 隔離等級為 Serializable，因此餘額所依據的範圍查詢在交易期間不會被其他交易插入；
/// 退款列本身另有 rowversion 樂觀鎖，兩者共同保證成功退款累計不超過已收款金額。
/// </summary>
public sealed class RefundExecutor : IRefundExecutor
{
    private readonly DoSelectDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RefundExecutor(DoSelectDbContext context, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<ExecuteRefundResult> ExecuteAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        RefundExecutionDecision.RequireWellFormed(request);

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        // 追蹤查詢：後續要在同一交易內更新這一列。
        var refund = await _context.Refunds
            .SingleOrDefaultAsync(
                candidate => candidate.PublicId == request.RefundPublicId,
                cancellationToken);

        if (refund is null)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.ResourceNotFound);
        }

        var snapshot = new RefundExecutionSnapshot(
            refund.Id,
            refund.Status,
            refund.ApprovedAmount,
            refund.SucceededAmount,
            await CalculateRefundableBalanceAsync(refund.OrderId, refund.Id, cancellationToken),
            refund.IdempotencyKey);

        var decision = RefundExecutionDecision.Evaluate(snapshot, request);
        if (decision.Plan is not { } plan)
        {
            // 拒絕或重播都不寫入，直接結束交易。
            return decision;
        }

        var occurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        refund.BeginProcessing(plan.ExecutedByAdminUserId, occurredAtUtc);
        refund.Complete(plan.Amount, occurredAtUtc);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 另一個交易已經動過這筆退款；本次不重試，交由呼叫端重新載入。
            return ExecuteRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
        }

        return ExecuteRefundResult.Settled(plan.Amount, plan);
    }

    /// <summary>
    /// 可退款餘額 = 該訂單已成功收款金額 - 其他退款已成功的金額累計。
    /// 排除本次退款自身，避免重試時把自己算成已用額度。
    /// </summary>
    private async Task<decimal> CalculateRefundableBalanceAsync(
        long orderId,
        long refundId,
        CancellationToken cancellationToken)
    {
        var paidTotal = await _context.PaymentAttempts
            .Where(attempt =>
                attempt.OrderId == orderId &&
                attempt.Status == PaymentAttemptStatus.Paid)
            .SumAsync(attempt => attempt.Amount, cancellationToken);

        var settledTotal = await _context.Refunds
            .Where(candidate =>
                candidate.OrderId == orderId &&
                candidate.Id != refundId &&
                candidate.Status == RefundStatus.Succeeded &&
                candidate.SucceededAmount != null)
            .SumAsync(candidate => candidate.SucceededAmount!.Value, cancellationToken);

        var balance = paidTotal - settledTotal;
        return balance > 0m ? balance : 0m;
    }
}
