using DoSelect.Application.Common;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Invoicing;

/// <summary>
/// 發票查詢的讀取契約。
/// </summary>
/// <remarks>
/// <para>
/// alex 2026-08-29 對 Issue #65 的裁定：<b>Invoicing Infrastructure 不得直接存取
/// <c>Orders</c>／<c>OrderItems</c></b>。所以這裡的 Reader 只讀 Invoicing 自己的表，
/// 回傳的列帶內部 <c>OrderId</c>；訂單那一半由
/// <c>IOrderInvoiceReferenceReader</c> <b>批次</b>補上，兩者在 Application 層合併。
/// </para>
/// <para>
/// 那個內部 <c>OrderId</c> 只在這一層存在，任何對外 DTO 都只有 <c>OrderPublicId</c>。
/// </para>
/// </remarks>
public sealed record SimulatedInvoiceItemDto(
    Guid PublicId,
    Guid? OrderItemPublicId,
    InvoiceLineKind Kind,
    string ProductName,
    string SkuCode,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount);

/// <summary>
/// 發票上一筆折讓的讀取摘要。
/// </summary>
/// <remarks>
/// <b>刻意不含 <c>RefundPublicId</c>。</b>建立折讓時的回應（
/// <c>SimulatedInvoiceAllowanceDto</c>）有那個欄位，因為寫入端手上就有退款；
/// 但查詢端要補它就得讀 <c>Refunds</c> —— 那是另一個模組，而發票查詢的契約
/// 並沒有要求這個欄位。為了一個沒人要的欄位開一條跨模組相依不划算。
/// </remarks>
public sealed record InvoiceAllowanceSummaryDto(
    Guid PublicId,
    string AllowanceNumber,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    IReadOnlyList<InvoiceAllowanceItemSummaryDto> Items,
    DateTime IssuedAtUtc);

public sealed record InvoiceAllowanceItemSummaryDto(
    Guid PublicId,
    Guid InvoiceItemPublicId,
    int Quantity,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount);

/// <summary>
/// 前台看得到的模擬發票。
/// </summary>
/// <remarks>
/// 買受人資料<b>只回遮蔽後的摘要</b>（`API Endpoint目錄` 第 74 行）。
/// 完整個資需要 <c>PersonalData.ViewFull</c>，不因為呼叫者是訂單擁有者就放行。
/// </remarks>
public sealed record SimulatedInvoiceDto(
    Guid PublicId,
    string InvoiceNumber,
    Guid OrderPublicId,
    SimulatedInvoiceStatus Status,
    SimulatedInvoiceBuyerType BuyerType,
    string? BuyerEmailMasked,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxIdMasked,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    string Currency,
    decimal TaxRate,
    IReadOnlyList<SimulatedInvoiceItemDto> Items,
    IReadOnlyList<InvoiceAllowanceSummaryDto> Allowances,
    DateTime? IssuedAtUtc,
    DateTime? VoidedAtUtc,
    string DemoMarker,
    byte[] RowVersion);

/// <summary>後台清單的一列；不含完整個資。</summary>
public sealed record AdminInvoiceSummaryDto(
    Guid PublicId,
    string InvoiceNumber,
    Guid OrderPublicId,
    string OrderNumber,
    SimulatedInvoiceStatus Status,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    DateTime? IssuedAtUtc,
    string DemoMarker,
    byte[] RowVersion);

/// <summary>
/// 後台發票明細。
/// </summary>
/// <param name="AvailableActions">
/// 這張發票目前可執行的動作。由狀態機決定，讓介面不必自己重寫一份規則 ——
/// 但這<b>不是安全邊界</b>，後端仍會擋。
/// </param>
public sealed record AdminInvoiceDto(
    SimulatedInvoiceDto Invoice,
    string OrderNumber,
    IReadOnlyList<string> AvailableActions);

/// <summary>
/// 後台清單查詢條件。
/// </summary>
public sealed record AdminInvoiceQuery(
    IReadOnlyList<SimulatedInvoiceStatus>? Statuses,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Q,
    int PageNumber,
    int PageSize);

/// <summary>
/// 一筆發票的原始讀取結果，<b>帶內部 <c>OrderId</c></b>。
/// </summary>
/// <remarks>
/// 只在 Application 與 Invoicing Infrastructure 之間流動。合併訂單資料之後就被丟棄，
/// 不會出現在任何對外 DTO。
/// </remarks>
public sealed record InvoiceRow(
    long OrderId,
    Guid PublicId,
    string InvoiceNumber,
    SimulatedInvoiceStatus Status,
    SimulatedInvoiceBuyerType BuyerType,
    string? BuyerEmail,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxId,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    string Currency,
    DateTime? IssuedAtUtc,
    DateTime? VoidedAtUtc,
    string DemoMarker,
    byte[] RowVersion,
    IReadOnlyList<SimulatedInvoiceItemDto> Items,
    IReadOnlyList<InvoiceAllowanceSummaryDto> Allowances);

/// <summary>
/// 發票讀取埠。實作只讀 Invoicing 自己的表。
/// </summary>
public interface IInvoiceQueryReader
{
    /// <summary>依訂單內部識別取一張發票；沒有就回 <c>null</c>。</summary>
    Task<InvoiceRow?> FindByOrderAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>依發票對外識別取一張發票。</summary>
    Task<InvoiceRow?> FindAsync(Guid invoicePublicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 後台清單。
    /// </summary>
    /// <remarks>
    /// 只在 Invoicing 自己的表上做條件與分頁；訂單摘要由呼叫端批次補上，
    /// 所以 <paramref name="query"/> 的 <c>Q</c> 只比對發票號碼。
    /// </remarks>
    Task<PageResult<InvoiceRow>> ListAsync(
        AdminInvoiceQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 誰在看這張發票。
/// </summary>
/// <remarks>
/// 訪客那一支不帶任何識別：Cookie 的 Scope 已經由既有的
/// <c>GuestOrderAccessScopeAuthorizer</c> 對「這一筆訂單」驗過，這裡再比一次沒有意義，
/// 也會變成第二份平行的驗證邏輯（alex 2026-08-29 Issue #65 明確禁止）。
/// </remarks>
public abstract record InvoiceViewer
{
    public sealed record Member(string MemberUserId) : InvoiceViewer;

    public sealed record Guest : InvoiceViewer;
}

/// <summary>發票可執行的動作名稱。</summary>
public static class InvoiceActions
{
    public const string Void = "void";
    public const string CreateAllowance = "createAllowance";

    /// <summary>
    /// 這個狀態下管理員可以做什麼。
    /// </summary>
    /// <remarks>
    /// 與 <c>SimulatedInvoice</c> 的狀態機逐項對應：<c>Void</c> 只接受 <c>Issued</c>，
    /// 折讓接受 <c>Issued</c> 與 <c>PartiallyAllowed</c>。
    /// <c>Voided</c> 與 <c>FullyAllowed</c> 是終態。
    /// </remarks>
    public static IReadOnlyList<string> For(SimulatedInvoiceStatus status) => status switch
    {
        SimulatedInvoiceStatus.Issued => [Void, CreateAllowance],
        SimulatedInvoiceStatus.PartiallyAllowed => [CreateAllowance],
        _ => [],
    };
}
