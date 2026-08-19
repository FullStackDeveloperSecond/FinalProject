namespace DoSelect.Domain.Invoicing;

/// <summary>
/// 模擬折讓單號。格式為 <c>DEMO-A-yyyyMM-NNNNNN</c>，與發票號碼一樣明確標示為展示資料。
/// </summary>
public static class DemoAllowanceNumber
{
    public const string Prefix = "DEMO-A";

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
/// 不能建立折讓的原因。
/// API錯誤碼目錄尚未登錄發票錯誤碼，因此本層回傳型別化原因，字串代碼的對應待目錄補齊。
/// </summary>
public enum InvoiceAllowanceRejection
{
    None,

    /// <summary>發票狀態不允許折讓。</summary>
    InvoiceNotAllowable,

    /// <summary>該筆退款已經有折讓；每筆 Refund 最多一筆折讓。</summary>
    RefundAlreadyAllowed,

    /// <summary>沒有任何可折讓的明細。</summary>
    NoAllowableLines,

    /// <summary>要折讓的明細不屬於這張發票。</summary>
    LineNotOnInvoice,

    /// <summary>累計折讓超過該明細的可折讓數量或金額。</summary>
    LineCapacityExceeded,
}

/// <summary>
/// 原發票明細目前的可折讓餘額。
/// </summary>
public sealed record InvoiceAllowanceCapacity(
    Guid SimulatedInvoiceItemPublicId,
    int Quantity,
    int AlreadyAllowedQuantity,
    decimal GrossAmount,
    decimal AlreadyAllowedGrossAmount)
{
    public int RemainingQuantity => Quantity - AlreadyAllowedQuantity;

    public decimal RemainingGrossAmount => GrossAmount - AlreadyAllowedGrossAmount;
}

/// <summary>
/// 要折讓的明細。<see cref="GrossAmount"/> 來自退款分攤，不由折讓端重算。
/// </summary>
public sealed record InvoiceAllowanceLineRequest(
    Guid SimulatedInvoiceItemPublicId,
    int Quantity,
    decimal GrossAmount);

public sealed record InvoiceAllowanceRequest(
    SimulatedInvoiceStatus InvoiceStatus,
    bool RefundAlreadyHasAllowance,
    IReadOnlyList<InvoiceAllowanceCapacity> Capacities,
    IReadOnlyList<InvoiceAllowanceLineRequest> Lines);

public sealed record InvoiceAllowanceLineBreakdown(
    Guid SimulatedInvoiceItemPublicId,
    int Quantity,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount);

/// <summary>
/// 折讓試算結果。失敗時只帶拒絕原因，不丟例外。
/// </summary>
public sealed class InvoiceAllowanceResult
{
    private InvoiceAllowanceResult(
        InvoiceAllowanceRejection rejection,
        decimal netAmount,
        decimal taxAmount,
        decimal amount,
        bool fullyAllowed,
        IReadOnlyList<InvoiceAllowanceLineBreakdown> lines)
    {
        Rejection = rejection;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        Amount = amount;
        FullyAllowed = fullyAllowed;
        Lines = lines;
    }

    public bool IsSuccess => Rejection == InvoiceAllowanceRejection.None;

    public InvoiceAllowanceRejection Rejection { get; }

    public decimal NetAmount { get; }

    public decimal TaxAmount { get; }

    /// <summary>折讓含稅總額，精確等於未稅加稅額。</summary>
    public decimal Amount { get; }

    /// <summary>這筆折讓之後，原發票各明細是否已全數折讓完畢。</summary>
    public bool FullyAllowed { get; }

    /// <summary>折讓後原發票應該進入的狀態。</summary>
    public SimulatedInvoiceStatus ResultingInvoiceStatus => FullyAllowed
        ? SimulatedInvoiceStatus.FullyAllowed
        : SimulatedInvoiceStatus.PartiallyAllowed;

    public IReadOnlyList<InvoiceAllowanceLineBreakdown> Lines { get; }

    public static InvoiceAllowanceResult Failure(InvoiceAllowanceRejection rejection) =>
        new(rejection, 0m, 0m, 0m, false, []);

    public static InvoiceAllowanceResult Success(
        decimal netAmount,
        decimal taxAmount,
        decimal amount,
        bool fullyAllowed,
        IReadOnlyList<InvoiceAllowanceLineBreakdown> lines) =>
        new(InvoiceAllowanceRejection.None, netAmount, taxAmount, amount, fullyAllowed, lines);
}
