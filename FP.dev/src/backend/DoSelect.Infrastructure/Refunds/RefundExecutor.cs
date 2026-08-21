using System.Data;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 在單一交易內完成退款執行：核對狀態與可退款餘額 → 條件更新退款 → 提交。
/// 隔離等級為 Serializable，因此餘額所依據的範圍查詢在交易期間不會被其他交易插入；
/// 退款列本身另有 rowversion 樂觀鎖，兩者共同保證成功退款累計不超過已收款金額。
/// </summary>
public sealed class RefundExecutor : IRefundExecutor
{
    /// <summary>SQL Server 的死結受害者錯誤碼。</summary>
    private const int DeadlockVictimErrorNumber = 1205;

    /// <summary>
    /// 交易邊界的重試次數。並行退款在 Serializable 下會互相死結，
    /// 重跑整個「重新讀取 → 重新判斷 → 寫入」才安全。
    /// </summary>
    private const int MaximumAttempts = 3;

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

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(request, cancellationToken);
            }
            catch (Exception exception) when (IsRetryableConflict(exception))
            {
                // 整個交易作廢，連同讀到的餘額一起丟掉。
                // 只重試 SaveChanges 會沿用死結前的舊餘額，因此必須重跑整段。
                _context.ChangeTracker.Clear();

                if (attempt == MaximumAttempts)
                {
                    return ExecuteRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
                }
            }
        }

        return ExecuteRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
    }

    private async Task<ExecuteRefundResult> ExecuteOnceAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken)
    {
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

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ExecuteRefundResult.Settled(plan.Amount, plan);
    }

    /// <summary>
    /// 值得重跑整段交易的並行衝突：SQL Server 死結受害者，或 rowversion 樂觀鎖失敗。
    /// 死結的 <see cref="SqlException"/> 會被層層包裝，因此往內層找。
    /// </summary>
    private static bool IsRetryableConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictimErrorNumber })
            {
                return true;
            }
        }

        return false;
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
