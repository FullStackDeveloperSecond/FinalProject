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
/// 非商品發票明細的識別方式（DEC-BATCH-022／DEC-P299）。
/// </summary>
/// <remarks>
/// 發票明細不另外持久化種類欄位，改以 <c>SkuCodeSnapshot</c> 的保留值識別。
/// 保留值集中定義在這裡，Writer、Reader 與 DTO 映射一律引用，不得各自散落字串。
/// 這些是內部識別值，**不是公開契約**；對外一律以 <see cref="InvoiceLineKind"/> 表達。
/// </remarks>
public static class InvoiceLineSkuCodes
{
    /// <summary>運費列的保留 SkuCodeSnapshot。</summary>
    public const string Shipping = "__INVOICE_SHIPPING__";

    /// <summary>組裝費列的保留 SkuCodeSnapshot。</summary>
    public const string AssemblyFee = "__INVOICE_ASSEMBLY_FEE__";

    /// <summary>該值是否為保留給非商品列的識別值。</summary>
    public static bool IsReserved(string? skuCodeSnapshot) =>
        skuCodeSnapshot is Shipping or AssemblyFee;

    /// <summary>
    /// 由 <c>OrderItemId</c> 與 <c>SkuCodeSnapshot</c> 判斷明細種類。
    /// </summary>
    /// <remarks>
    /// 商品列必須有 <c>OrderItemId</c> 且不得使用保留值；
    /// 非商品列必須沒有 <c>OrderItemId</c> 且只接受保留值。
    /// 未知的非商品識別值必須拒絕，不得靜默略過，也不得歸類成其他種類。
    /// </remarks>
    public static InvoiceLineKind ResolveKind(long? orderItemId, string? skuCodeSnapshot)
    {
        if (orderItemId is not null)
        {
            if (IsReserved(skuCodeSnapshot))
            {
                throw new ArgumentException(
                    "A merchandise invoice line cannot use a reserved SKU code.",
                    nameof(skuCodeSnapshot));
            }

            return InvoiceLineKind.Merchandise;
        }

        return skuCodeSnapshot switch
        {
            Shipping => InvoiceLineKind.Shipping,
            AssemblyFee => InvoiceLineKind.AssemblyFee,
            _ => throw new ArgumentException(
                "A non-merchandise invoice line must use a reserved SKU code.",
                nameof(skuCodeSnapshot)),
        };
    }

    /// <summary>對外的穩定值。保留值本身不得出現在 API 契約上。</summary>
    public static string ToPublicKind(InvoiceLineKind kind) => kind switch
    {
        InvoiceLineKind.Merchandise => "merchandise",
        InvoiceLineKind.Shipping => "shipping",
        InvoiceLineKind.AssemblyFee => "assemblyFee",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
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
