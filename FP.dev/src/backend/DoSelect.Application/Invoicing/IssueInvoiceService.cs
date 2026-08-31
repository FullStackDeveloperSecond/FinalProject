using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Invoicing;

public sealed record IssueInvoiceRequest(Guid OrderPublicId);

/// <summary>
/// 通過檢查後要建立的模擬發票。實際寫入與狀態歷程由發票端點或付款成功流程負責。
/// </summary>
public sealed record InvoiceIssuancePlan(
    long OrderId,
    string InvoiceNumber,
    SimulatedInvoiceBuyerType BuyerType,
    string? BuyerEmail,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxId,
    string? CompanyName,
    decimal NetAmount,
    decimal TaxAmount,
    decimal IssuedAmount,
    decimal RoundingAdjustment,
    IReadOnlyList<InvoiceIssuanceLinePlan> Lines);

public sealed record InvoiceIssuanceLinePlan(
    long? OrderItemId,
    InvoiceLineBreakdown Breakdown);

public sealed class IssueInvoiceResult
{
    private IssueInvoiceResult(string? errorCode, InvoiceIssuancePlan? plan)
    {
        ErrorCode = errorCode;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    public InvoiceIssuancePlan? Plan { get; }

    public static IssueInvoiceResult Failure(string errorCode) => new(errorCode, null);

    public static IssueInvoiceResult Approved(InvoiceIssuancePlan plan) => new(null, plan);
}

/// <summary>
/// 決定要不要為一張訂單開立模擬發票。本服務只做決策與取號，不寫資料庫。
/// 模擬發票不串接財政部平台，也不產生可兌獎或具法律效力的電子發票。
/// </summary>
public sealed class IssueInvoiceService
{
    private readonly IOrderInvoiceIssuanceReader _orderReader;
    private readonly IInvoiceExistenceReader _existenceReader;
    private readonly IInvoiceNumberSequence _numberSequence;
    private readonly TimeProvider _timeProvider;

    public IssueInvoiceService(
        IOrderInvoiceIssuanceReader orderReader,
        IInvoiceExistenceReader existenceReader,
        IInvoiceNumberSequence numberSequence,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(orderReader);
        ArgumentNullException.ThrowIfNull(existenceReader);
        ArgumentNullException.ThrowIfNull(numberSequence);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _orderReader = orderReader;
        _existenceReader = existenceReader;
        _numberSequence = numberSequence;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// 把 Orders 回報的訂單狀態映射成開票 trigger。
    /// </summary>
    /// <remarks>
    /// 已付款一律映射為 <see cref="InvoiceIssuanceTrigger.OnlinePaymentSucceeded"/>：
    /// 要分辨貨到付款得看 <c>PaymentAttempt.Method</c>，那是 Payments 模組的資料，
    /// 不在 Issue #65 A1 裁定的 Orders 範圍內。目前這個映射不影響任何結果 ——
    /// <c>InvoiceCalculator</c> 對兩個已付款 trigger 的處理完全相同。
    /// 需要區分時要補 Payments 側的埠，不是讓 Orders 去讀 PaymentAttempts。
    /// </remarks>
    private static InvoiceIssuanceTrigger TriggerFor(InvoiceOrderSnapshot snapshot)
    {
        if (snapshot.OrderIsCancelled)
        {
            return InvoiceIssuanceTrigger.OrderCancelled;
        }

        return snapshot.OrderIsPaid
            ? InvoiceIssuanceTrigger.OnlinePaymentSucceeded
            : InvoiceIssuanceTrigger.NotPaid;
    }

    public async Task<IssueInvoiceResult> IssueAsync(
        IssueInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = await _orderReader.FindIssuanceSnapshotAsync(
            request.OrderPublicId,
            cancellationToken);

        if (snapshot is null)
        {
            return IssueInvoiceResult.Failure(InvoiceErrorCodes.ResourceNotFound);
        }

        // 公司發票缺統編或抬頭時，SimulatedInvoice 建構子會拒絕。
        // 先在這裡擋下，否則會回傳一份無法持久化的成功計畫，並白白耗掉一個流水號。
        if (!InvoicePolicy.HasCompleteBuyerDetails(
            snapshot.BuyerType, snapshot.CompanyTaxId, snapshot.CompanyName))
        {
            throw new InvalidOperationException(
                "A company invoice requires both the company tax id and the company name.");
        }
        // 「已經開過票了嗎」問 Invoicing 自己的表，不放進 Orders 的快照。
        var alreadyIssued = await _existenceReader.HasInvoiceAsync(
            snapshot.OrderId,
            cancellationToken);

        var invoiceableSources = snapshot.Lines
            .Where(source => source.Line.GrossAmount > 0m)
            .ToArray();
        var calculation = InvoiceCalculator.Calculate(new InvoiceIssuanceRequest(
            TriggerFor(snapshot),
            alreadyIssued,
            snapshot.OrderPaidAmount,
            [.. invoiceableSources.Select(source => source.Line)]));

        if (!calculation.IsSuccess)
        {
            return IssueInvoiceResult.Failure(calculation.ErrorCode!);
        }

        var issuedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var sequence = await _numberSequence.NextAsync(issuedAtUtc, cancellationToken);

        return IssueInvoiceResult.Approved(new InvoiceIssuancePlan(
            snapshot.OrderId,
            DemoInvoiceNumber.Format(issuedAtUtc, sequence),
            snapshot.BuyerType,
            snapshot.BuyerEmail,
            snapshot.CarrierType,
            snapshot.CarrierValueMasked,
            snapshot.CompanyTaxId,
            snapshot.CompanyName,
            calculation.NetAmount,
            calculation.TaxAmount,
            calculation.IssuedAmount,
            calculation.RoundingAdjustment,
            calculation.Lines
                .Select((line, index) => new InvoiceIssuanceLinePlan(
                    invoiceableSources[index].OrderItemId,
                    line))
                .ToArray()));
    }
}
