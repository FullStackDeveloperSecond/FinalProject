using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Invoicing;

/// <summary>
/// 一筆退款要開立折讓時，原發票的狀態與各明細可折讓餘額。
/// <paramref name="SimulatedInvoiceId"/> 與 <paramref name="RefundId"/> 為內部識別，不得對外回傳。
/// </summary>
public sealed record InvoiceAllowanceSnapshot(
    long SimulatedInvoiceId,
    long RefundId,
    SimulatedInvoiceStatus InvoiceStatus,
    bool RefundAlreadyHasAllowance,
    IReadOnlyList<InvoiceAllowanceCapacity> Capacities);

/// <summary>
/// 開立折讓所需的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface IInvoiceAllowanceReader
{
    /// <summary>
    /// 以退款的對外識別找出原發票與可折讓餘額。餘額必須與本次開立在同一交易內取得。
    /// </summary>
    Task<InvoiceAllowanceSnapshot?> FindByRefundAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得下一個模擬折讓流水號。必須在同一交易內原子取號，
    /// 以配合 <c>SimulatedInvoiceAllowances.AllowanceNumber</c> 的唯一索引。
    /// </summary>
    Task<int> NextAllowanceSequenceAsync(
        DateTime issuedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed record IssueInvoiceAllowanceRequest(
    Guid RefundPublicId,
    IReadOnlyList<InvoiceAllowanceLineRequest> Lines);

/// <summary>
/// 通過檢查後要建立的折讓。實際寫入、原發票狀態轉移與狀態歷程由折讓端點負責。
/// </summary>
public sealed record InvoiceAllowancePlan(
    long SimulatedInvoiceId,
    long RefundId,
    string AllowanceNumber,
    decimal NetAmount,
    decimal TaxAmount,
    decimal Amount,
    DateTime IssuedAtUtc,
    SimulatedInvoiceStatus ResultingInvoiceStatus,
    IReadOnlyList<InvoiceAllowanceLineBreakdown> Lines);

public sealed class IssueInvoiceAllowanceResult
{
    private IssueInvoiceAllowanceResult(
        bool refundFound,
        InvoiceAllowanceRejection rejection,
        InvoiceAllowancePlan? plan)
    {
        RefundFound = refundFound;
        Rejection = rejection;
        Plan = plan;
    }

    public bool IsSuccess => Plan is not null;

    /// <summary>退款不存在或沒有對應發票時為 <c>false</c>。</summary>
    public bool RefundFound { get; }

    public InvoiceAllowanceRejection Rejection { get; }

    public InvoiceAllowancePlan? Plan { get; }

    public static IssueInvoiceAllowanceResult NotFound() =>
        new(false, InvoiceAllowanceRejection.None, null);

    public static IssueInvoiceAllowanceResult Rejected(InvoiceAllowanceRejection rejection) =>
        new(true, rejection, null);

    public static IssueInvoiceAllowanceResult Approved(InvoiceAllowancePlan plan) =>
        new(true, InvoiceAllowanceRejection.None, plan);
}

/// <summary>
/// 決定要不要為一筆已完成的退款建立折讓。本服務只做決策與取號，不寫資料庫。
/// 折讓金額只依訂單交易快照與退款分攤計算，不回寫也不刪除原發票。
/// </summary>
public sealed class IssueInvoiceAllowanceService
{
    private readonly IInvoiceAllowanceReader _allowanceReader;
    private readonly TimeProvider _timeProvider;

    public IssueInvoiceAllowanceService(
        IInvoiceAllowanceReader allowanceReader,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(allowanceReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _allowanceReader = allowanceReader;
        _timeProvider = timeProvider;
    }

    public async Task<IssueInvoiceAllowanceResult> IssueAsync(
        IssueInvoiceAllowanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);

        var snapshot = await _allowanceReader.FindByRefundAsync(
            request.RefundPublicId,
            cancellationToken);

        if (snapshot is null)
        {
            return IssueInvoiceAllowanceResult.NotFound();
        }

        var calculation = InvoiceAllowanceCalculator.Calculate(new InvoiceAllowanceRequest(
            snapshot.InvoiceStatus,
            snapshot.RefundAlreadyHasAllowance,
            snapshot.Capacities,
            request.Lines));

        if (!calculation.IsSuccess)
        {
            return IssueInvoiceAllowanceResult.Rejected(calculation.Rejection);
        }

        var issuedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var sequence = await _allowanceReader.NextAllowanceSequenceAsync(
            issuedAtUtc,
            cancellationToken);

        return IssueInvoiceAllowanceResult.Approved(new InvoiceAllowancePlan(
            snapshot.SimulatedInvoiceId,
            snapshot.RefundId,
            DemoAllowanceNumber.Format(issuedAtUtc, sequence),
            calculation.NetAmount,
            calculation.TaxAmount,
            calculation.Amount,
            issuedAtUtc,
            calculation.ResultingInvoiceStatus,
            calculation.Lines));
    }
}
