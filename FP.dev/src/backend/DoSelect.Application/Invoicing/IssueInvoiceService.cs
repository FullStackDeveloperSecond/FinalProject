using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Invoicing;

/// <summary>
/// 一張訂單開立發票所需的交易快照。<paramref name="OrderId"/> 為內部識別，不得對外回傳。
/// </summary>
public sealed record InvoiceOrderSnapshot(
    long OrderId,
    InvoiceIssuanceTrigger Trigger,
    bool OrderAlreadyHasInvoice,
    SimulatedInvoiceBuyerType BuyerType,
    string? BuyerEmail,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxId,
    string? CompanyName,
    IReadOnlyList<InvoiceOrderLine> Lines);

/// <summary>
/// 開立模擬發票所需的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface IInvoiceIssuanceReader
{
    Task<InvoiceOrderSnapshot?> FindOrderSnapshotAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得下一個模擬發票流水號。必須在同一交易內原子取號，
    /// 以配合 <c>SimulatedInvoices.InvoiceNumber</c> 的唯一索引。
    /// </summary>
    Task<int> NextInvoiceSequenceAsync(
        DateTime issuedAtUtc,
        CancellationToken cancellationToken = default);
}

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
    IReadOnlyList<InvoiceLineBreakdown> Lines);

public sealed class IssueInvoiceResult
{
    private IssueInvoiceResult(
        bool orderFound,
        InvoiceIssuanceRejection rejection,
        InvoiceIssuancePlan? plan)
    {
        OrderFound = orderFound;
        Rejection = rejection;
        Plan = plan;
    }

    public bool IsSuccess => Plan is not null;

    /// <summary>訂單不存在或依安全策略不可揭露時為 <c>false</c>。</summary>
    public bool OrderFound { get; }

    public InvoiceIssuanceRejection Rejection { get; }

    public InvoiceIssuancePlan? Plan { get; }

    public static IssueInvoiceResult NotFound() =>
        new(false, InvoiceIssuanceRejection.None, null);

    public static IssueInvoiceResult Rejected(InvoiceIssuanceRejection rejection) =>
        new(true, rejection, null);

    public static IssueInvoiceResult Approved(InvoiceIssuancePlan plan) =>
        new(true, InvoiceIssuanceRejection.None, plan);
}

/// <summary>
/// 決定要不要為一張訂單開立模擬發票。本服務只做決策與取號，不寫資料庫。
/// 模擬發票不串接財政部平台，也不產生可兌獎或具法律效力的電子發票。
/// </summary>
public sealed class IssueInvoiceService
{
    private readonly IInvoiceIssuanceReader _issuanceReader;
    private readonly TimeProvider _timeProvider;

    public IssueInvoiceService(IInvoiceIssuanceReader issuanceReader, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(issuanceReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _issuanceReader = issuanceReader;
        _timeProvider = timeProvider;
    }

    public async Task<IssueInvoiceResult> IssueAsync(
        IssueInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = await _issuanceReader.FindOrderSnapshotAsync(
            request.OrderPublicId,
            cancellationToken);

        if (snapshot is null)
        {
            return IssueInvoiceResult.NotFound();
        }

        var calculation = InvoiceCalculator.Calculate(new InvoiceIssuanceRequest(
            snapshot.Trigger,
            snapshot.OrderAlreadyHasInvoice,
            snapshot.Lines));

        if (!calculation.IsSuccess)
        {
            return IssueInvoiceResult.Rejected(calculation.Rejection);
        }

        var issuedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var sequence = await _issuanceReader.NextInvoiceSequenceAsync(issuedAtUtc, cancellationToken);

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
            calculation.Lines));
    }
}
