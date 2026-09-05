namespace DoSelect.Application.Refunds;

/// <summary>
/// 把對應退貨從 <c>AwaitingRefund</c> 推到 <c>Completed</c> 的窄介面。原本只服務
/// 「退款執行成功」那一刻（alex 2026-09-04 #98 追蹤，接續 #99 A1 裁定）；#103 裁定
/// 把「核准時重算後已無款可退，退款終止為 <c>Cancelled</c>」也納入同一個埠——兩者
/// 都是退貨結案的合法終局，差別只在<b>為什麼</b>結案，因此由呼叫端提供
/// <see cref="RefundReturnCompletionCommand.ReasonCode"/> 而不是寫死在實作裡。
/// </summary>
/// <remarks>
/// <para>
/// <b>介面歸 Refund（M-13）所有</b>——是 Refund 知道退款何時真的執行成功，Returns 不該
/// 反過來輪詢或猜測。與 <c>IReturnRefundCreationPort</c>（Returns 呼叫、Refund 實作）方向
/// 相反：這個埠由 Refund 呼叫，實作歸 Returns，因為退貨的狀態機、轉移規則與歷程格式
/// 是 Returns 的權威。與 <c>IReturnInventoryPort</c>（Returns 呼叫、Inventory 實作）同一個
/// 慣例——由需要那個行為的模組定義介面，由擁有那份資料的模組實作。
/// </para>
/// <para>
/// <b>實作不得呼叫 SaveChanges，也不得自行開交易。</b>只在目前這個 scoped Unit of Work
/// 上暫存，由 <c>RefundExecutor</c> 既有的單一 <c>SaveChangesAsync</c>（交易由共用
/// <c>IIdempotencyExecutor</c> 擁有）與退款狀態、分攤、稽核一起提交。退款狀態進了
/// 資料庫、退貨卻沒有跟著結案（或反過來）就是一筆對不了帳的財務紀錄。
/// </para>
/// <para>
/// 只有 <c>Refund.ReturnRequestId</c> 非 <c>null</c> 才呼叫這個埠——沒有關聯退貨的退款
/// 沒有東西可結案。呼叫時退貨必須恰好是 <c>AwaitingRefund</c>：#98 A2／#99 A1 已保證
/// 一張退貨只會有唯一一筆 Refund，且那筆 Refund 建立的同一筆交易就把退貨推進
/// <c>AwaitingRefund</c>，該狀態的唯一出口只有 <c>Completed</c>。若呼叫時不是這個狀態，
/// 代表不變量被破壞，實作應該丟例外讓整筆退款執行回滾，而不是靜默略過或硬寫。
/// </para>
/// </remarks>
public interface IRefundReturnCompletionPort
{
    Task CompleteReturnAsync(
        RefundReturnCompletionCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// 退款結案那一刻，Refund 模組交給 Returns 完成結案所需的最小資訊。
/// </summary>
/// <remarks>
/// <para>
/// 不帶金額——退貨結案不需要知道退了多少錢，那是 Refund 自己的紀錄。
/// </para>
/// <para>
/// <paramref name="ReasonCode"/> 寫進 <c>ReturnStatusHistory.ReasonCode</c>，讓稽核能
/// 區分退貨是因為退款真的執行成功而結案，還是核准時重算後已無款可退——兩者都是
/// 合法終局，但對帳意義完全不同。呼叫端須用既有的 <c>AuditFieldChange.RequireSafeCode</c>
/// 規則（ASCII、64 字元內），不得夾帶自由文字。
/// </para>
/// </remarks>
public sealed record RefundReturnCompletionCommand(
    long ReturnRequestId,
    string AdminUserId,
    DateTime OccurredAtUtc,
    string ReasonCode);
