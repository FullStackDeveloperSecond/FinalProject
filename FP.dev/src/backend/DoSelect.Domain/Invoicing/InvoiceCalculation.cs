namespace DoSelect.Domain.Invoicing;

/// <summary>
/// 模擬發票號碼。格式為 <c>DEMO-yyyyMM-NNNNNN</c>，明確標示為展示資料，
/// 且刻意不採用真實統一發票的兩碼字軌加八碼數字格式。
/// </summary>
public static class DemoInvoiceNumber
{
    public const string Prefix = "DEMO";

    public static string Format(DateTime issuedAtUtc, int sequence)
    {
        if (issuedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(issuedAtUtc));
        }

        if (sequence is < 1 or > 999999)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return $"{Prefix}-{issuedAtUtc:yyyyMM}-{sequence:D6}";
    }
}

/// <summary>
/// 發票明細的來源。運費與組裝費沒有對應的 OrderItem，開立時 <c>OrderItemId</c> 留空。
/// </summary>
public enum InvoiceLineKind
{
    Merchandise,
    Shipping,
    AssemblyFee,
}

/// <summary>
/// 訂單在嘗試開立發票當下的付款情形。
/// </summary>
public enum InvoiceIssuanceTrigger
{
    /// <summary>線上付款成功。</summary>
    OnlinePaymentSucceeded,

    /// <summary>貨到付款完成收款。</summary>
    CashOnDeliveryCollected,

    /// <summary>尚未付款。</summary>
    NotPaid,

    /// <summary>訂單已取消。</summary>
    OrderCancelled,
}

/// <summary>
/// 不能開立模擬發票的原因。
/// API錯誤碼目錄尚未登錄發票錯誤碼，因此本層回傳型別化原因，字串代碼的對應待目錄補齊。
/// </summary>
public enum InvoiceIssuanceRejection
{
    None,

    /// <summary>未付款不開立。</summary>
    OrderNotPaid,

    /// <summary>取消訂單不開立。</summary>
    OrderCancelled,

    /// <summary>沒有任何可開立的明細。</summary>
    NoInvoiceableLines,

    /// <summary>已存在該訂單的發票；每張訂單最多一張。</summary>
    AlreadyIssued,
}

/// <summary>
/// 不能作廢模擬發票的原因。
/// </summary>
public enum InvoiceVoidRejection
{
    None,

    /// <summary>只有已開立的發票能作廢。</summary>
    NotIssued,

    /// <summary>訂單並未整筆取消。</summary>
    OrderNotFullyCancelled,

    /// <summary>已發生金流退款，必須建立折讓而不是作廢。</summary>
    RefundAlreadySettled,
}

/// <summary>
/// 開立發票所需的訂單交易快照。<see cref="GrossAmount"/> 為該列的含稅實付金額。
/// </summary>
public sealed record InvoiceOrderLine(
    Guid? OrderItemPublicId,
    InvoiceLineKind Kind,
    string ProductNameSnapshot,
    string SkuCodeSnapshot,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal GrossAmount);

public sealed record InvoiceIssuanceRequest(
    InvoiceIssuanceTrigger Trigger,
    bool OrderAlreadyHasInvoice,
    IReadOnlyList<InvoiceOrderLine> Lines);

public sealed record InvoiceLineBreakdown(
    Guid? OrderItemPublicId,
    InvoiceLineKind Kind,
    string ProductNameSnapshot,
    string SkuCodeSnapshot,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount);

/// <summary>
/// 模擬發票試算結果。失敗時只帶拒絕原因，不丟例外。
/// </summary>
public sealed class InvoiceCalculationResult
{
    private InvoiceCalculationResult(
        InvoiceIssuanceRejection rejection,
        decimal netAmount,
        decimal taxAmount,
        decimal issuedAmount,
        IReadOnlyList<InvoiceLineBreakdown> lines)
    {
        Rejection = rejection;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        IssuedAmount = issuedAmount;
        Lines = lines;
    }

    public bool IsSuccess => Rejection == InvoiceIssuanceRejection.None;

    public InvoiceIssuanceRejection Rejection { get; }

    /// <summary>未稅金額，等於各明細未稅金額合計。</summary>
    public decimal NetAmount { get; }

    /// <summary>稅額，等於各明細稅額合計。</summary>
    public decimal TaxAmount { get; }

    /// <summary>含稅總額，等於顧客實付金額，也精確等於未稅加稅額。</summary>
    public decimal IssuedAmount { get; }

    public IReadOnlyList<InvoiceLineBreakdown> Lines { get; }

    public static InvoiceCalculationResult Failure(InvoiceIssuanceRejection rejection) =>
        new(rejection, 0m, 0m, 0m, []);

    public static InvoiceCalculationResult Success(
        decimal netAmount,
        decimal taxAmount,
        decimal issuedAmount,
        IReadOnlyList<InvoiceLineBreakdown> lines) =>
        new(InvoiceIssuanceRejection.None, netAmount, taxAmount, issuedAmount, lines);
}
