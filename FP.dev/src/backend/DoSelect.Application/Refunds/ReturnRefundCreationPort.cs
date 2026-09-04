using DoSelect.Application.Common;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Refunds;

/// <summary>
/// 退貨核准／檢查完成後，判斷並暫存那筆退款的窄介面
/// （alex 2026-09-03 #98 A2、2026-09-04 #99 A1 裁定）。
/// </summary>
/// <remarks>
/// <para>
/// <b>介面歸 Refund（M-13）所有</b>，由 Returns（M-12）呼叫。金額、付款嘗試、退款編號與
/// 冪等金鑰全部由實作決定；退貨端只宣告「這張退貨到了 <c>AwaitingRefund</c>」，
/// 不建構 <see cref="DoSelect.Domain.Refunds.Refund"/>，也不傳任何金額。
/// </para>
/// <para>
/// 這是本模組刻意與 <c>IReturnInventoryPort</c> 不同的地方：那個埠定義在
/// <c>Application/Returns</c>（退貨端定義、庫存端實作），而這個埠定義在
/// <c>Application/Refunds</c>。退款金額的可信來源是 Refund 模組，介面若由退貨端定義，
/// 遲早會被加上金額參數。
/// </para>
/// <para>
/// <b>實作不得呼叫 SaveChanges。</b>與 <c>IReturnInventoryPort.StageReturnToStockAsync</c>
/// 同一個約定：只在目前這個 scoped Unit of Work 上暫存，由
/// <c>IReturnStore.SaveTransitionAsync</c> 把退貨狀態、歷程與這筆退款一起提交。
/// 退貨狀態進了資料庫、退款卻沒有（或反過來）就是一筆對不了帳的財務紀錄。
/// </para>
/// </remarks>
public interface IReturnRefundCreationPort
{
    /// <summary>
    /// 為 <paramref name="returnPublicId"/> 暫存唯一一筆 <c>PendingReview</c> 退款。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 唯一性由 <c>UX_Refunds_IdempotencyKey</c> 保證：實作寫入的是由退貨對外識別推導的
    /// <b>決定性</b>金鑰，因此並行的兩次核准最多只有一次能提交，另一次整筆交易回滾。
    /// 這不需要新的索引或 Migration —— <c>Refund.IdempotencyKey</c> 本來就是
    /// 「建立退款」這個操作的金鑰（見 <c>RefundExecutionSnapshot</c> 的說明），
    /// 執行階段用的是共用 <c>IIdempotencyExecutor</c>，兩者互不相干。
    /// </para>
    /// <para>
    /// 可信快照不齊（退貨原因無法映射、組裝費處置或退貨運費未記錄）時丟
    /// <see cref="DomainProblemException"/> 帶 <c>refund_snapshot_unavailable</c>，
    /// 與執行端同一個錯誤碼。此時整筆核准都不成立 —— 建立不出退款的核准，
    /// 只會留下一張永遠等不到退款的退貨。
    /// </para>
    /// <para>
    /// <b>回傳具名結果，不用例外表達「無款可退」（#99 A1）。</b>後端依可信快照算出
    /// 淨額 &lt;= 0 是一個合法的業務結果 —— 折扣／運費扣回蓋過了品項退款，不是錯誤。
    /// 呼叫端不得把它跟真正的失敗（快照缺漏、計算輸入不合法）混在同一個例外類型裡，
    /// 用 <c>catch</c> 判斷業務分支。只有 <see cref="ReturnRefundCreationOutcome.NoRefundDue"/>
    /// 是無款可退；其餘情形（找不到已付款嘗試、可信快照不齊、計算本身失敗）仍然丟例外。
    /// </para>
    /// </remarks>
    Task<ReturnRefundCreationOutcome> StagePendingRefundAsync(
        ReturnRefundCreationCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IReturnRefundCreationPort.StagePendingRefundAsync"/> 的結果（#99 A1 裁定）。
/// </summary>
public abstract record ReturnRefundCreationOutcome
{
    /// <summary>已暫存唯一一筆 <c>PendingReview</c> 退款。</summary>
    public sealed record PendingRefundStaged : ReturnRefundCreationOutcome;

    /// <summary>
    /// 可信快照算出的淨額 &lt;= 0，沒有建立任何 Refund。呼叫端據此讓退貨直接結案
    /// （<c>Approved</c>／<c>Inspecting</c> → <c>Completed</c>），不得經過 <c>AwaitingRefund</c>。
    /// </summary>
    public sealed record NoRefundDue : ReturnRefundCreationOutcome;
}

/// <summary>
/// 退貨端在推進到 <c>AwaitingRefund</c> 的<b>同一刻</b>交給退款模組的可信退貨快照。
/// </summary>
/// <remarks>
/// 這三項（原因、組裝費處置、退貨運費）必須由呼叫端傳進來，不能由實作回頭讀資料庫：
/// <c>CaptureRefundTrustedInputs</c> 此時只改了記憶體中的退貨實體，SaveChanges 還沒發生，
/// 讀資料庫只會讀到舊值。這也正是裁定裡「M-12 提供可信退貨快照」的那一份。
/// <para>
/// 金額不在這裡 —— 由退款模組依這份快照與訂單自己算，管理端與退貨端都不傳金額。
/// </para>
/// </remarks>
public sealed record ReturnRefundCreationCommand(
    Guid ReturnPublicId,
    string AdminUserId,
    string ReasonCode,
    AssemblyFeeDisposition AssemblyFeeDisposition,
    decimal ReturnShippingCost,
    DateTime OccurredAtUtc);
