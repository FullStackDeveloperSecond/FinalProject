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
            FindTrustedInputs(refund));
    }

    /// <summary>
    /// 讀出後端產生七類分攤所需的完整可信快照，三項輸入齊全時才回傳。
    /// </summary>
    /// <remarks>
    /// **目前一律回 <c>null</c>。這是 E1 裁定要求的行為，不是尚未實作。**
    /// <see cref="RefundCalculator"/> 需要三項輸入，而三項在資料庫都沒有來源：
    /// <list type="bullet">
    /// <item><c>ReturnReason</c>：<c>ReturnRequest.ReasonCode</c> 是自由文字，
    /// 與列舉沒有對應關係，也沒有任何登錄過的代碼集合。</item>
    /// <item><c>AssemblyFeeDisposition</c>：沒有任何實體保存。</item>
    /// <item><c>ReturnShippingCost</c>：沒有任何實體保存。</item>
    /// </list>
    /// 三項都屬退貨核准範圍（kafen）。M-12（PR #42）已合併但未涵蓋。
    /// <para>
    /// 這裡刻意不以退款金額比例、固定值或目前的商品／優惠券設定推測。分攤一旦寫入
    /// 即不可變，估算值會讓對帳與發票折讓永久失真。上游落地後在此組出
    /// <see cref="RefundTrustedInputs"/> 即可，決策層不需要改動。
    /// </para>
    /// </remarks>
    private static RefundTrustedInputs? FindTrustedInputs(Refund refund) => null;

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
