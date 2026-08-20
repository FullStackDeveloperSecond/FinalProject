using DoSelect.Application.Refunds;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 以 <see cref="DoSelectDbContext"/> 讀出退款交易狀態與訂單可退款餘額。
/// 只讀取本模組擁有的 <c>Refunds</c> 與 <c>PaymentAttempts</c>，不觸碰其他模組的底層表。
/// </summary>
public sealed class RefundExecutionReader : IRefundExecutionReader
{
    private readonly DoSelectDbContext _context;

    public RefundExecutionReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<RefundExecutionSnapshot?> FindAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default)
    {
        if (refundPublicId == Guid.Empty)
        {
            return null;
        }

        var refund = await _context.Refunds
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PublicId == refundPublicId, cancellationToken);

        if (refund is null)
        {
            return null;
        }

        return new RefundExecutionSnapshot(
            refund.Id,
            refund.Status,
            refund.ApprovedAmount,
            refund.SucceededAmount,
            await CalculateRefundableBalanceAsync(refund.OrderId, cancellationToken));
    }

    /// <summary>
    /// 可退款餘額 = 該訂單已成功收款金額 - 已成功退款金額累計。
    /// 兩者都取自本模組的資料表，因此不需要跨模組查詢訂單金額。
    /// </summary>
    private async Task<decimal> CalculateRefundableBalanceAsync(
        long orderId,
        CancellationToken cancellationToken)
    {
        var paidTotal = await _context.PaymentAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.OrderId == orderId &&
                attempt.Status == PaymentAttemptStatus.Paid)
            .SumAsync(attempt => attempt.Amount, cancellationToken);

        var settledTotal = await _context.Refunds
            .AsNoTracking()
            .Where(candidate =>
                candidate.OrderId == orderId &&
                candidate.Status == RefundStatus.Succeeded &&
                candidate.SucceededAmount != null)
            .SumAsync(candidate => candidate.SucceededAmount!.Value, cancellationToken);

        var balance = paidTotal - settledTotal;
        return balance > 0m ? balance : 0m;
    }
}
