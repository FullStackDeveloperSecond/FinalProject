namespace DoSelect.Domain.Invoicing;

/// <summary>
/// 模擬發票的錯誤碼。值必須與 API錯誤碼目錄 一致（DEC-BATCH-014）。
/// 共用錯誤沿用既有代碼，不新增同義別名。
/// </summary>
public static class InvoiceErrorCodes
{
    public const string InvoiceOrderUnpaid = "invoice_order_unpaid";
    public const string InvoiceOrderCancelled = "invoice_order_cancelled";
    public const string InvoiceAlreadyExists = "invoice_already_exists";
    public const string InvoiceStateConflict = "invoice_state_conflict";
    public const string InvoiceAllowanceRequired = "invoice_allowance_required";
    public const string ResourceNotFound = "resource_not_found";
    public const string IdempotencyPayloadConflict = "idempotency_payload_conflict";
}

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

/// <summary>
/// <paramref name="OrderPaidAmount"/> 是訂單實付金額，已於付款前四捨五入至整數新臺幣。
/// 明細加總四捨五入後必須等於它，否則視為訂單快照不一致。
/// </summary>
public sealed record InvoiceIssuanceRequest(
    InvoiceIssuanceTrigger Trigger,
    bool OrderAlreadyHasInvoice,
    decimal OrderPaidAmount,
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
/// 模擬發票試算結果。失敗時只帶錯誤碼，不丟例外。
/// </summary>
public sealed class InvoiceCalculationResult
{
    private InvoiceCalculationResult(
        string? errorCode,
        decimal netAmount,
        decimal taxAmount,
        decimal issuedAmount,
        decimal roundingAdjustment,
        IReadOnlyList<InvoiceLineBreakdown> lines)
    {
        ErrorCode = errorCode;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        IssuedAmount = issuedAmount;
        RoundingAdjustment = roundingAdjustment;
        Lines = lines;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>表頭未稅金額，整數元。<c>Round(IssuedAmount / 1.05, 0, AwayFromZero)</c>。</summary>
    public decimal NetAmount { get; }

    /// <summary>表頭稅額，整數元，精確等於各明細稅額合計。</summary>
    public decimal TaxAmount { get; }

    /// <summary>表頭含稅總額，整數元，等於訂單實付金額。</summary>
    public decimal IssuedAmount { get; }

    /// <summary>
    /// 表頭含稅總額減去明細含稅加總的尾差。明細允許兩位小數，因此這個值可能不為零。
    /// 由既有訂單總額與明細加總推導，不需要額外欄位。
    /// </summary>
    public decimal RoundingAdjustment { get; }

    /// <summary>明細金額允許兩位小數；每列滿足 <c>Gross = Net + Tax</c> 且不為負。</summary>
    public IReadOnlyList<InvoiceLineBreakdown> Lines { get; }

    public static InvoiceCalculationResult Failure(string errorCode) =>
        new(errorCode, 0m, 0m, 0m, 0m, []);

    public static InvoiceCalculationResult Success(
        decimal netAmount,
        decimal taxAmount,
        decimal issuedAmount,
        decimal roundingAdjustment,
        IReadOnlyList<InvoiceLineBreakdown> lines) =>
        new(null, netAmount, taxAmount, issuedAmount, roundingAdjustment, lines);
}
