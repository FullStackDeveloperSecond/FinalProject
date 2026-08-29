using DoSelect.Application.Refunds;
using DoSelect.Application.Returns;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 組出後端產生七類分攤所需的完整可信快照。
/// </summary>
/// <remarks>
/// <para>
/// 唯讀預覽（<see cref="RefundExecutionReader"/>）與實際執行（<see cref="RefundExecutor"/>）
/// **必須共用這一份**。兩處各寫一份會出現「預覽說可以執行、實際執行卻拒絕」的落差，
/// 而管理員只看得到後者。
/// </para>
/// <para>
/// 齊全的定義由 alex 於 DEC-BATCH-019 裁定：退貨原因必須能映射，且
/// <c>AssemblyFeeDisposition</c> 與 <c>ReturnShippingCost</c> **兩欄皆有值**。
/// 任一缺漏就回 <c>null</c>，呼叫端據此回 <c>refund_snapshot_unavailable</c>。
/// </para>
/// </remarks>
public sealed class RefundTrustedInputsReader
{
    private readonly DoSelectDbContext _context;

    public RefundTrustedInputsReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    /// 是否為完整退貨：每一列的「已退數量 + 本次退貨數量」都等於原始數量。
    /// </summary>
    /// <remarks>
    /// 這是 <see cref="RefundCalculator"/> 內 <c>isFullReturn</c> 的同一份判斷
    /// （`RefundCalculator.cs:102-104`）。兩處必須一致 —— 這裡只用它決定
    /// 「要不要求基本費快照」，若判斷比計算器寬鬆就會放行一筆算不出來的退款，
    /// 比計算器嚴格則會拒絕本來算得出來的退款。
    /// </remarks>
    private static bool IsFullReturn(
        IReadOnlyList<RefundOrderLine> orderLines,
        IReadOnlyList<RefundLineRequest> requestedLines)
    {
        var requestedByLine = requestedLines
            .GroupBy(line => line.OrderItemPublicId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        return orderLines.All(line =>
            line.AlreadyReturnedQuantity +
            requestedByLine.GetValueOrDefault(line.OrderItemPublicId) == line.Quantity);
    }

    public async Task<RefundTrustedInputs?> FindAsync(
        long orderId,
        long? returnRequestId,
        CancellationToken cancellationToken)
    {
        // 沒有關聯退貨就沒有原因、組裝費處置與退貨運費 —— 三項全缺。
        if (returnRequestId is not { } id)
        {
            return null;
        }

        var returnRequest = await _context.ReturnRequests
            .AsNoTracking()
            .Where(request => request.Id == id)
            .Select(request => new
            {
                request.ReasonCode,
                request.AssemblyFeeDisposition,
                request.ReturnShippingCost,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (returnRequest is null)
        {
            return null;
        }

        // Null 代表可信值從未被記錄，**不得**當成 NotApplicable 或 0。
        // 兩欄有資料庫 Check Constraint 保證同為 Null 或同為非 Null，
        // 這裡仍分別檢查：約束是資料庫的保證，不是這段程式碼可以省略判斷的理由。
        if (returnRequest.AssemblyFeeDisposition is not { } assemblyDisposition ||
            returnRequest.ReturnShippingCost is not { } returnShippingCost)
        {
            return null;
        }

        // LateNonDefectiveGoodwill 與 CustomerProcessDeviation 目前沒有 Returns 的
        // 輸入路徑，映射會失敗。不得自行猜測 —— 猜錯會直接改變退貨運費由誰負擔。
        if (!ReturnEligibilityPolicy.TryMapRefundReason(returnRequest.ReasonCode, out var reason))
        {
            return null;
        }

        var order = await _context.Orders
            .AsNoTracking()
            .Where(candidate => candidate.Id == orderId)
            .Select(candidate => new
            {
                candidate.ShippingFee,
                candidate.AssemblyFee,
                candidate.ShippingFreeThresholdSnapshot,
                candidate.ShippingMethodBaseFeeSnapshot,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var orderLines = await _context.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .Select(item => new RefundOrderLine(
                item.PublicId,
                item.Quantity,
                item.ReturnedQuantity,
                item.FinalUnitPrice,
                item.DiscountAllocation,
                item.IsCouponEligible))
            .ToArrayAsync(cancellationToken);

        if (orderLines.Length == 0)
        {
            return null;
        }

        // 優惠券是選用的：沒有套券的訂單三個欄位都是 0／null，那不是資料缺漏。
        var coupon = await _context.OrderCoupons
            .AsNoTracking()
            .Where(applied => applied.OrderId == orderId)
            .Select(applied => new
            {
                applied.AppliedAmount,
                applied.EligibleSubtotal,
                applied.MinimumSpendAmount,
            })
            .SingleOrDefaultAsync(cancellationToken);

        var requestedLines = await _context.ReturnItems
            .AsNoTracking()
            .Where(item => item.ReturnRequestId == id)
            .Join(
                _context.OrderItems.AsNoTracking(),
                item => item.OrderItemId,
                orderItem => orderItem.Id,
                (item, orderItem) => new RefundLineRequest(orderItem.PublicId, item.Quantity))
            .ToArrayAsync(cancellationToken);

        if (requestedLines.Length == 0)
        {
            return null;
        }

        // 免運追回：訂單當初免運、退貨後保留金額低於門檻時，要把原本的基準運費追回。
        //
        // `Orders.ShippingMethodBaseFeeSnapshot` 保存的是**下單當時**、免運規則套用前的
        // 配送方式基本費（alex 於 PR #54 落地）。舊訂單為 Null 且沒有回填 ——
        // 現行 `ShippingMethod.BaseFee` 是目前值，回查它就違反「不得依目前設定回推
        // 歷史交易」（DEC-P287）。
        //
        // 只在計算真的會讀到基本費時才要求快照。三個條件缺一不可：
        //
        // 1. 訂單當初免運（實付 0）—— 否則 ResolveShippingClawback 直接回 0。
        // 2. 有免運門檻快照 —— 否則同樣回 0。
        // 3. **不是完整退貨** —— 完整退貨走 OriginalShipping 退還原運費那條，
        //    根本不會執行免運追回。先前少了這一條，讓所有舊免運訂單連完整退貨
        //    都被拒絕，而那些退款其實完全算得出來。
        var wasFreeShipping = order.ShippingFee <= 0m;
        var isFullReturn = IsFullReturn(orderLines, requestedLines);
        var needsBaseFee =
            wasFreeShipping &&
            order.ShippingFreeThresholdSnapshot is not null &&
            !isFullReturn;

        if (needsBaseFee && order.ShippingMethodBaseFeeSnapshot is null)
        {
            return null;
        }

        return new RefundTrustedInputs(
            new RefundOrderSnapshot(
                orderLines,
                order.ShippingFee,
                order.ShippingMethodBaseFeeSnapshot ?? order.ShippingFee,
                order.ShippingFreeThresholdSnapshot,
                order.AssemblyFee,
                coupon?.AppliedAmount ?? 0m,
                coupon?.EligibleSubtotal ?? 0m,
                coupon?.MinimumSpendAmount),
            requestedLines,
            reason,
            assemblyDisposition,
            returnShippingCost);
    }
}
