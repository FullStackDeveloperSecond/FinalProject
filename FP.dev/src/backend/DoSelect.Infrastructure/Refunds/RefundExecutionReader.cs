using DoSelect.Application.Refunds;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 退款執行的唯讀預覽來源。只讀取本模組擁有的 <c>Refunds</c> 與 <c>PaymentAttempts</c>，
/// 不觸碰其他模組的底層表。
/// </summary>
/// <remarks>
/// 這裡分次查詢且不加鎖，**不保證原子性**：兩個並行請求可能讀到相同餘額。
/// 因此本型別只供後台預覽顯示，實際寫入必須走 <see cref="IRefundExecutor"/>，
/// 由它在同一交易內重新核對後條件更新。
/// </remarks>
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
            await CalculateRefundableBalanceAsync(refund.OrderId, refund.Id, cancellationToken),
            refund.RowVersion,
            await FindTrustedInputsAsync(refund, cancellationToken));
    }

    /// <summary>
    /// 讀出後端產生七類分攤所需的完整可信快照，齊全時才回傳。
    /// </summary>
    /// <remarks>
    /// 判斷邏輯與實際執行共用 <see cref="RefundTrustedInputsReader"/>，
    /// 避免出現「預覽說可以執行、實際執行卻拒絕」的落差。
    /// </remarks>
    private Task<RefundTrustedInputs?> FindTrustedInputsAsync(
        Refund refund,
        CancellationToken cancellationToken) =>
        new RefundTrustedInputsReader(_context)
            .FindAsync(refund.OrderId, refund.ReturnRequestId, cancellationToken);

    /// <summary>
    /// 可退款餘額 = 該訂單已成功收款金額 - 其他退款已成功的金額累計。
    /// 排除本次退款自身，避免重試時把自己算成已用額度。
    /// 兩者都取自本模組的資料表，因此不需要跨模組查詢訂單金額。
    /// </summary>
    private async Task<decimal> CalculateRefundableBalanceAsync(
        long orderId,
        long refundId,
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
                candidate.Id != refundId &&
                candidate.Status == RefundStatus.Succeeded &&
                candidate.SucceededAmount != null)
            .SumAsync(candidate => candidate.SucceededAmount!.Value, cancellationToken);

        var balance = paidTotal - settledTotal;
        return balance > 0m ? balance : 0m;
    }
}
