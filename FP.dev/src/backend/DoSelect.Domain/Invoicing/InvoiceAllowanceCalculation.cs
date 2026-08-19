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
/// 由成功 Refund 的分攤推導出的折讓明細。金額必須由後端依成功退款與原發票明細推導，
/// 不接受前端指定（DEC-BATCH-014 第 9 項）。
/// </summary>
public sealed record RefundedInvoiceLine(
    Guid SimulatedInvoiceItemPublicId,
    int Quantity,
    decimal GrossAmount);

public sealed record InvoiceAllowanceRequest(
    SimulatedInvoiceStatus InvoiceStatus,
    bool RefundAlreadyHasAllowance,
    IReadOnlyList<InvoiceAllowanceCapacity> Capacities,
    IReadOnlyList<RefundedInvoiceLine> RefundedLines);

public sealed record InvoiceAllowanceLineBreakdown(
    Guid SimulatedInvoiceItemPublicId,
    int Quantity,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount);

/// <summary>
/// 折讓試算結果。失敗時只帶錯誤碼，不丟例外。
/// </summary>
public sealed class InvoiceAllowanceResult
{
    private InvoiceAllowanceResult(
        string? errorCode,
        decimal netAmount,
        decimal taxAmount,
        decimal amount,
        bool fullyAllowed,
        IReadOnlyList<InvoiceAllowanceLineBreakdown> lines)
    {
        ErrorCode = errorCode;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        Amount = amount;
        FullyAllowed = fullyAllowed;
        Lines = lines;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>折讓表頭未稅，由折讓含稅總額回推，並精確等於各明細未稅合計。</summary>
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

    public static InvoiceAllowanceResult Failure(string errorCode) =>
        new(errorCode, 0m, 0m, 0m, false, []);

    public static InvoiceAllowanceResult Success(
        decimal netAmount,
        decimal taxAmount,
        decimal amount,
        bool fullyAllowed,
        IReadOnlyList<InvoiceAllowanceLineBreakdown> lines) =>
        new(null, netAmount, taxAmount, amount, fullyAllowed, lines);
}
