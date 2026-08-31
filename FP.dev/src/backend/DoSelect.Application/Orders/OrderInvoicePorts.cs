using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Orders;

/// <summary>
/// 發票模組需要的 Orders 資料，一律由這裡的埠提供。
/// </summary>
/// <remarks>
/// <para>
/// alex 2026-08-29 對 Issue #65 的 A1 裁定：跨模組資料透過 Orders Application 提供的
/// Query／DTO／port 取得，<b>不開放 Invoicing 直接存取 Orders 的 DbSet 或 Repository</b>。
/// <c>DEC-B1</c>（退款的具名直查例外）只維持個案，不作為擴大直查的先例。
/// </para>
/// <para>
/// 實作放在 Orders 所擁有的 Infrastructure 位置，由 yinyin 以 haru 的備援身分交付，
/// Orders domain review 由 haru 負責。
/// </para>
/// </remarks>
public static class OrderInvoicePortNotes
{
    /// <summary>
    /// <b>窄內部 Key 例外</b>（alex 2026-08-29 Issue #65 裁定）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本檔案的埠允許回傳內部 <c>OrderId</c>／<c>OrderItemId</c>，但只限於寫入既有外鍵
    /// （<c>SimulatedInvoices.OrderId</c>、<c>SimulatedInvoiceItems.OrderItemId</c>）及將這些
    /// 已保存的外鍵批次換回 Orders-owned <c>PublicId</c>（DEC-P299）。
    /// </para>
    /// <para>
    /// 不得出現在 API、URL、log、Audit、event 或通用 DTO；也不代表授權 Invoicing
    /// 直接讀取 Orders 的 DbContext／Repository。一般跨模組不得暴露 bigint key 的規則仍然有效。
    /// </para>
    /// </remarks>
    public const string InternalKeyException =
        "僅供既有 FK 寫入與其 PublicId 投影的具名窄例外；不得對外輸出。";
}

/// <summary>
/// 一列發票明細的來源。
/// </summary>
/// <param name="OrderItemId">
/// 內部主鍵，僅供寫入 <c>SimulatedInvoiceItems.OrderItemId</c>（DEC-P299）。
/// 非商品列（運費、組裝費）沒有對應的訂單品項，為 <c>null</c>。
/// 適用 <see cref="OrderInvoicePortNotes.InternalKeyException"/>。
/// </param>
/// <param name="Line">交給 <c>InvoiceCalculator</c> 的對外安全內容。</param>
public sealed record InvoiceOrderLineSource(long? OrderItemId, InvoiceOrderLine Line);

/// <summary>
/// 一張訂單開立發票所需的交易快照。
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="OrderId"/> 是內部識別，適用
/// <see cref="OrderInvoicePortNotes.InternalKeyException"/>，不得對外回傳。
/// </para>
/// <para>
/// <b>為什麼回傳的是訂單狀態事實，而不是 <c>InvoiceIssuanceTrigger</c>。</b>
/// 要區分 <c>OnlinePaymentSucceeded</c> 與 <c>CashOnDeliveryCollected</c>，得看
/// <c>PaymentAttempt.Method</c> —— 那是 <b>Payments</b> 模組的資料，不在 A1 裁定的
/// Orders 範圍內。讓 Orders 去讀 PaymentAttempts 只是把同一個越界搬到上一層。
/// </para>
/// <para>
/// 所以埠只回它確實知道的兩件事（已取消、已付款），由 Invoicing 的 Application 層
/// 映射成 trigger。目前這個映射不影響任何結果：<c>InvoiceCalculator</c> 對兩個已付款
/// trigger 的處理完全相同，而 <c>InvoiceIssuanceTrigger</c> 在別處沒有用到。
/// 之後真的需要區分時，正確做法是補一個 Payments 側的埠，而不是讓 Orders 讀 PaymentAttempts。
/// </para>
/// <para>
/// 同理，「這張訂單開過發票了嗎」<b>不在這份快照裡</b>：那是 <c>SimulatedInvoices</c>，
/// 是 Invoicing 自己的資料。讓 Orders 去讀它只是把同一個越界反過來做一次。
/// 由 <c>IInvoiceExistenceReader</c> 回答。
/// </para>
/// </remarks>
public sealed record InvoiceOrderSnapshot(
    long OrderId,
    bool OrderIsCancelled,
    bool OrderIsPaid,
    decimal OrderPaidAmount,
    SimulatedInvoiceBuyerType BuyerType,
    string? BuyerEmail,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxId,
    string? CompanyName,
    IReadOnlyList<InvoiceOrderLineSource> Lines);

/// <summary>
/// 開立模擬發票所需的訂單快照。實作屬於 Orders 的 Infrastructure。
/// </summary>
public interface IOrderInvoiceIssuanceReader
{
    /// <summary>
    /// 取得指定訂單的開票快照；訂單不存在時回 <c>null</c>。
    /// </summary>
    Task<InvoiceOrderSnapshot?> FindIssuanceSnapshotAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 發票對外投影與擁有者驗證需要的訂單欄位。
/// </summary>
/// <param name="OrderId">
/// 內部識別，適用 <see cref="OrderInvoicePortNotes.InternalKeyException"/>；
/// 這裡的用途是把 <c>SimulatedInvoices.OrderId</c> 對回一張訂單，不得對外輸出。
/// </param>
/// <param name="OrderPublicId">對外識別，發票 DTO 的 <c>orderPublicId</c>。</param>
/// <param name="MemberUserId">會員訂單的擁有者；訪客訂單為 <c>null</c>。</param>
/// <param name="GuestEmailNormalized">訪客訂單的正規化 Email；會員訂單為 <c>null</c>。</param>
public sealed record OrderInvoiceReference(
    long OrderId,
    Guid OrderPublicId,
    string OrderNumber,
    string? MemberUserId,
    string? GuestEmailNormalized);

/// <summary>
/// 發票查詢用的訂單投影。實作屬於 Orders 的 Infrastructure。
/// </summary>
public interface IOrderInvoiceReferenceReader
{
    /// <summary>
    /// 以對外識別取得一張訂單，供前台 <c>GET /api/v1/orders/{orderId}/invoice</c> 使用。
    /// </summary>
    Task<OrderInvoiceReference?> FindAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次取回多張訂單。
    /// </summary>
    /// <remarks>
    /// 後台發票清單每一列都要 <c>orderPublicId</c> 與訂單摘要。逐列查會是 N+1，
    /// 所以這個埠<b>只提供批次形式</b>，不提供「給一個 id 查一筆」的內部主鍵版本 ——
    /// 有那個多載，呼叫端很自然就會在迴圈裡用它。
    /// </remarks>
    Task<IReadOnlyDictionary<long, OrderInvoiceReference>> FindManyAsync(
        IReadOnlyCollection<long> orderIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 將發票商品列保存的內部 <c>OrderItemId</c> 批次換回對外 <c>PublicId</c>。
    /// </summary>
    Task<IReadOnlyDictionary<long, Guid>> FindItemPublicIdsAsync(
        IReadOnlyCollection<long> orderItemIds,
        CancellationToken cancellationToken = default);
}
