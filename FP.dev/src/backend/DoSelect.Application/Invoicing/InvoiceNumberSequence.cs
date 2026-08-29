namespace DoSelect.Application.Invoicing;

/// <summary>
/// 模擬發票流水號的取號來源。
/// </summary>
/// <remarks>
/// <para>
/// alex 2026-08-29 對 Issue #65 的裁定：<b>發票流水號不應由 Orders 擁有</b>。
/// 原本 <c>IInvoiceIssuanceReader</c> 把「讀訂單快照」與「取號」放在同一個介面，
/// 前者是 Orders 的資料、後者是 Invoicing 自己的資料（<c>SimulatedInvoices</c>），
/// 兩種責任混在一起，於是這個介面沒有一個模組能正當實作它。
/// </para>
/// <para>
/// 取號必須<b>在開票的同一個交易內</b>進行，以配合
/// <c>UX_SimulatedInvoices_InvoiceNumber</c> 唯一索引：號碼配出去卻在別的交易，
/// 併發時兩張發票會拿到同一個號碼，最後由唯一索引擋下、整筆失敗。
/// 唯一索引是最後一道防線，不是取號策略。
/// </para>
/// </remarks>
public interface IInvoiceNumberSequence
{
    /// <summary>
    /// 取得 <paramref name="issuedAtUtc"/> 所屬月份的下一個流水號。
    /// </summary>
    /// <remarks>
    /// 號碼格式是 <c>DEMO-yyyyMM-NNNNNN</c>（<c>DemoInvoiceNumber.Format</c>），
    /// 所以序號是<b>逐月</b>重新起算的。
    /// </remarks>
    Task<int> NextAsync(DateTime issuedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// 一張訂單是否已經開過模擬發票。
/// </summary>
/// <remarks>
/// <para>
/// 這是 <b>Invoicing 自己的資料</b>（<c>SimulatedInvoices</c>，且有
/// <c>UX_SimulatedInvoices_OrderId</c> 唯一索引），所以由 Invoicing 的埠回答，
/// 不放進 Orders 的開票快照 —— 讓 Orders 去讀 SimulatedInvoices，只是把
/// Issue #65 要避免的越界反過來做一次。
/// </para>
/// <para>
/// 參數是內部 <c>OrderId</c>，用途正是比對 <c>SimulatedInvoices.OrderId</c> 這個既有外鍵，
/// 屬於 alex 裁定的窄內部 Key 例外（見 <c>OrderInvoicePortNotes.InternalKeyException</c>）。
/// </para>
/// </remarks>
public interface IInvoiceExistenceReader
{
    Task<bool> HasInvoiceAsync(long orderId, CancellationToken cancellationToken = default);
}
