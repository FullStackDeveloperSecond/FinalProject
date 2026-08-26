using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Invoicing;

/// <summary>
/// 後台發票查詢條件。一般頁碼分頁。
/// </summary>
public sealed record AdminInvoiceQuery(
    IReadOnlyList<SimulatedInvoiceStatus>? Statuses,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Keyword,
    int PageNumber,
    int PageSize)
{
    public const int MaximumPageSize = 100;

    public const int DefaultPageSize = 20;

    /// <summary>
    /// 正規化分頁與時間範圍。頁碼與筆數由後端夾住，呼叫端不能要求任意大小的頁面。
    /// </summary>
    public AdminInvoiceQuery Normalize()
    {
        if (FromUtc is { } from && from.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(FromUtc));
        }

        if (ToUtc is { } to && to.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(ToUtc));
        }

        return this with
        {
            PageNumber = PageNumber < 1 ? 1 : PageNumber,
            PageSize = PageSize switch
            {
                < 1 => DefaultPageSize,
                > MaximumPageSize => MaximumPageSize,
                _ => PageSize,
            },
            Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
            Statuses = Statuses is { Count: > 0 } ? Statuses : null,
        };
    }
}

/// <summary>
/// 後台發票清單的一列。
/// </summary>
/// <remarks>
/// **不含完整個資**。買受人只有型別與遮蔽後的識別，清單頁不需要也不應該看到完整 Email 或統編。
/// </remarks>
public sealed record AdminInvoiceSummaryDto(
    Guid PublicId,
    string InvoiceNumber,
    Guid OrderPublicId,
    string OrderNumber,
    SimulatedInvoiceStatus Status,
    decimal NetAmount,
    decimal TaxAmount,
    decimal IssuedAmount,
    DateTime? IssuedAtUtc,
    string DemoMarker,
    byte[] RowVersion);

/// <summary>
/// 發票明細的一列。<paramref name="Kind"/> 為對外的穩定值，不外洩內部保留碼（DEC-P299）。
/// </summary>
public sealed record SimulatedInvoiceItemDto(
    Guid PublicId,
    Guid? OrderItemPublicId,
    string Kind,
    string ProductNameSnapshot,
    string SkuCodeSnapshot,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount);

/// <summary>
/// 發票上的買受人摘要。
/// </summary>
/// <remarks>
/// 預設一律遮蔽。只有同時具備 <c>PersonalData.ViewFull</c> 的呼叫端才會拿到完整值；
/// <c>Invoice.Manage</c> 本身**不足以**看到完整個資。
/// </remarks>
public sealed record InvoiceBuyerSummaryDto(
    SimulatedInvoiceBuyerType BuyerType,
    string? BuyerEmailMasked,
    string? BuyerEmail,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxIdMasked,
    string? CompanyTaxId,
    string? CompanyName);

/// <summary>
/// 後台發票明細頁。
/// </summary>
public sealed record AdminInvoiceDto(
    Guid PublicId,
    string InvoiceNumber,
    Guid OrderPublicId,
    SimulatedInvoiceStatus Status,
    InvoiceBuyerSummaryDto Buyer,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    string Currency,
    decimal TaxRate,
    IReadOnlyList<SimulatedInvoiceItemDto> Items,
    IReadOnlyList<SimulatedInvoiceAllowanceSummaryDto> Allowances,
    DateTime? IssuedAtUtc,
    DateTime? VoidedAtUtc,
    string DemoMarker,
    IReadOnlyList<string> AvailableActions,
    byte[] RowVersion);

/// <summary>
/// 明細頁上的折讓摘要。
/// </summary>
public sealed record SimulatedInvoiceAllowanceSummaryDto(
    Guid PublicId,
    string AllowanceNumber,
    decimal NetAmount,
    decimal TaxAmount,
    decimal Amount,
    DateTime IssuedAtUtc,
    string DemoMarker);

/// <summary>
/// 顧客看自己訂單的發票。刻意比後台少很多欄位，且沒有 RowVersion。
/// </summary>
public sealed record CustomerInvoiceDto(
    Guid PublicId,
    string InvoiceNumber,
    SimulatedInvoiceStatus Status,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    string Currency,
    decimal TaxRate,
    IReadOnlyList<SimulatedInvoiceItemDto> Items,
    DateTime? IssuedAtUtc,
    string DemoMarker);

/// <summary>
/// 一頁查詢結果。
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// 後台與顧客發票查詢的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface IAdminInvoiceReader
{
    Task<PagedResult<AdminInvoiceSummaryDto>> SearchAsync(
        AdminInvoiceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 後台發票明細。<paramref name="includeFullPersonalData"/> 只有在呼叫端具備
    /// <c>PersonalData.ViewFull</c> 時才可為 <c>true</c>；否則買受人一律只回遮蔽值。
    /// </summary>
    Task<AdminInvoiceDto?> FindAsync(
        Guid invoicePublicId,
        bool includeFullPersonalData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 顧客查看自己訂單的發票。
    /// </summary>
    /// <param name="orderId">
    /// **已完成擁有者驗證**的內部訂單鍵。擁有者比對由本模組的 Application／授權層
    /// 以 <c>IOrderIdentityReader</c> 取得的 <c>MemberUserId</c> 與登入者 claim 完成，
    /// 不在這個 reader 內判斷 —— 授權是本模組 endpoint 的職責，Orders 的 reader
    /// 只負責提供資料。
    /// </param>
    /// <remarks>
    /// 呼叫順序必須是「先驗證擁有者、再取發票」：
    /// <c>IOrderIdentityReader.FindByPublicIdAsync</c> 回 <c>null</c> 或
    /// <c>MemberUserId</c> 與登入者不符時就直接擋掉，不得先把 DTO 生出來再篩
    /// （工程包「不得先取出他人資料再由 DTO 或 Vue 隱藏」）。
    /// </remarks>
    Task<CustomerInvoiceDto?> FindByOrderIdAsync(
        long orderId,
        CancellationToken cancellationToken = default);
}
