using DoSelect.Domain.Refunds;

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
/// 哪些退款分攤會變成折讓明細（DEC-BATCH-021／DEC-P298）。
/// </summary>
public static class InvoiceAllowancePolicy
{
    /// <summary>
    /// 只有原發票實際開立過的銷售金額才折讓。扣回方向與退貨寄回運費都不建立明細。
    /// </summary>
    /// <remarks>
    /// <c>OriginalShipping</c> 與 <c>AssemblyFee</c> 另有前提：原發票確實收取過該筆費用，
    /// 且本次退款確實退還。無法由同一張原發票的交易快照確認時必須拒絕建立折讓，
    /// 不得猜測或改讀目前設定 —— 這是 DEC-P298 明訂的停止條件。
    /// </remarks>
    public static bool CreatesAllowanceLine(RefundAllocationType allocationType) =>
        allocationType switch
        {
            // 商品退款、原始運費與組裝費都在原發票上收過，退還時要折讓。
            RefundAllocationType.ItemRefund => true,
            RefundAllocationType.OriginalShipping => true,
            RefundAllocationType.AssemblyFee => true,

            // 退貨寄回運費是另外發生的費用，不是原發票的銷售金額。
            RefundAllocationType.ReturnShipping => false,

            // 扣回方向只參與退款淨額計算（DEC-P278），
            // 不得寫成負值折讓明細去湊現金退款總額。
            RefundAllocationType.DiscountClawback => false,
            RefundAllocationType.ShippingClawback => false,

            // 第一版禁止寫入，出現即為資料錯誤。
            RefundAllocationType.OtherAdjustment => false,

            _ => throw new ArgumentOutOfRangeException(nameof(allocationType)),
        };

    /// <summary>
    /// 該類型的折讓明細是否需要對應到原發票的商品列。
    /// 運費與組裝費列在發票上沒有 <c>OrderItemId</c>，靠明細種類辨識。
    /// </summary>
    public static bool MapsToAnOrderItem(RefundAllocationType allocationType) =>
        allocationType == RefundAllocationType.ItemRefund;
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
        decimal roundingAdjustment,
        bool fullyAllowed,
        IReadOnlyList<InvoiceAllowanceLineBreakdown> lines)
    {
        ErrorCode = errorCode;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        Amount = amount;
        RoundingAdjustment = roundingAdjustment;
        FullyAllowed = fullyAllowed;
        Lines = lines;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>折讓表頭未稅，由折讓含稅總額回推，並精確等於各明細未稅合計。</summary>
    public decimal NetAmount { get; }

    public decimal TaxAmount { get; }

    /// <summary>折讓含稅總額，取整數元，精確等於未稅加稅額。</summary>
    public decimal Amount { get; }

    /// <summary>
    /// 表頭取整數元與各明細含稅合計之間的差額（<c>Amount - Sum(Line.GrossAmount)</c>）。
    /// 明細帶小數時必然不為零，這個差額只記錄在表頭，不塞進任何一列。
    /// </summary>
    public decimal RoundingAdjustment { get; }

    /// <summary>這筆折讓之後，原發票各明細是否已全數折讓完畢。</summary>
    public bool FullyAllowed { get; }

    /// <summary>折讓後原發票應該進入的狀態。</summary>
    public SimulatedInvoiceStatus ResultingInvoiceStatus => FullyAllowed
        ? SimulatedInvoiceStatus.FullyAllowed
        : SimulatedInvoiceStatus.PartiallyAllowed;

    public IReadOnlyList<InvoiceAllowanceLineBreakdown> Lines { get; }

    public static InvoiceAllowanceResult Failure(string errorCode) =>
        new(errorCode, 0m, 0m, 0m, 0m, false, []);

    public static InvoiceAllowanceResult Success(
        decimal netAmount,
        decimal taxAmount,
        decimal amount,
        decimal roundingAdjustment,
        bool fullyAllowed,
        IReadOnlyList<InvoiceAllowanceLineBreakdown> lines) =>
        new(null, netAmount, taxAmount, amount, roundingAdjustment, fullyAllowed, lines);
}
