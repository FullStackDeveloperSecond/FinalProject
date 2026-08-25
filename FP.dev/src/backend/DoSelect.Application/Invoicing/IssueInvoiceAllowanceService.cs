using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Invoicing;

/// <summary>
/// 一筆退款要開立折讓時，原發票的狀態、各明細可折讓餘額，以及由成功 Refund 推導出的折讓明細。
/// <paramref name="SimulatedInvoiceId"/> 與 <paramref name="RefundId"/> 為內部識別，不得對外回傳。
/// </summary>
/// <remarks>
/// 退款存在但尚未成功時，發票相關欄位為 <c>null</c>：那些資料還不該被讀取，
/// 填入預設值只會把「退款狀態不對」偽裝成別的失敗。
/// </remarks>
public sealed record InvoiceAllowanceSnapshot(
    RefundStatus RefundStatus,
    long RefundId,
    long? SimulatedInvoiceId,
    SimulatedInvoiceStatus? InvoiceStatus,
    bool RefundAlreadyHasAllowance,
    IReadOnlyList<InvoiceAllowanceCapacity> Capacities,
    IReadOnlyList<RefundedInvoiceLine> RefundedLines);

/// <summary>
/// 開立折讓所需的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface IInvoiceAllowanceReader
{
    /// <summary>
    /// 以退款的對外識別找出原發票、可折讓餘額，以及由成功 Refund 分攤推導出的折讓明細。
    /// 全部必須與本次開立在同一交易內取得。
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

/// <summary>
/// 折讓請求只帶退款識別與冪等金鑰。金額一律由後端依成功 Refund 與原發票明細推導，
/// 不接受前端指定（DEC-BATCH-014 第 9 項）。
/// </summary>
public sealed record IssueInvoiceAllowanceRequest(
    Guid RefundPublicId,
    string IdempotencyKey,
    long? ExpectedSimulatedInvoiceId = null);

/// <summary>
/// 通過檢查後要建立的折讓。實際寫入、原發票狀態轉移、Audit 與同交易提交由 Writer 負責。
/// API 端點只傳遞請求與 actor context，不協調交易。
/// </summary>
public sealed record InvoiceAllowancePlan(
    long SimulatedInvoiceId,
    long RefundId,
    string AllowanceNumber,
    string IdempotencyKey,
    decimal NetAmount,
    decimal TaxAmount,
    decimal Amount,
    DateTime IssuedAtUtc,
    SimulatedInvoiceStatus ResultingInvoiceStatus,
    IReadOnlyList<InvoiceAllowanceLineBreakdown> Lines);

public sealed class IssueInvoiceAllowanceResult
{
    private IssueInvoiceAllowanceResult(string? errorCode, InvoiceAllowancePlan? plan)
    {
        ErrorCode = errorCode;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    public InvoiceAllowancePlan? Plan { get; }

    public static IssueInvoiceAllowanceResult Failure(string errorCode) => new(errorCode, null);

    public static IssueInvoiceAllowanceResult Approved(InvoiceAllowancePlan plan) =>
        new(null, plan);
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

        // Idempotency-Key 為必填 Header，缺少時由 API 層以驗證錯誤擋下。
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("The idempotency key is required.", nameof(request));
        }

        var snapshot = await _allowanceReader.FindByRefundAsync(
            request.RefundPublicId,
            cancellationToken);

        // 退款不存在才是 resource_not_found。
        if (snapshot is null)
        {
            return IssueInvoiceAllowanceResult.Failure(InvoiceErrorCodes.ResourceNotFound);
        }

        // 退款存在但還沒成功，是狀態問題而不是找不到資源。
        if (snapshot.RefundStatus != RefundStatus.Succeeded)
        {
            return IssueInvoiceAllowanceResult.Failure(RefundErrorCodes.RefundStateConflict);
        }

        // 成功的退款卻找不到原發票，代表沒有可折讓的對象。
        if (snapshot.SimulatedInvoiceId is not { } simulatedInvoiceId ||
            snapshot.InvoiceStatus is not { } invoiceStatus)
        {
            return IssueInvoiceAllowanceResult.Failure(InvoiceErrorCodes.ResourceNotFound);
        }

        if (request.ExpectedSimulatedInvoiceId is { } expectedInvoiceId &&
            simulatedInvoiceId != expectedInvoiceId)
        {
            return IssueInvoiceAllowanceResult.Failure(InvoiceErrorCodes.ResourceNotFound);
        }

        var calculation = InvoiceAllowanceCalculator.Calculate(new InvoiceAllowanceRequest(
            invoiceStatus,
            snapshot.RefundAlreadyHasAllowance,
            snapshot.Capacities,
            snapshot.RefundedLines));

        if (!calculation.IsSuccess)
        {
            return IssueInvoiceAllowanceResult.Failure(calculation.ErrorCode!);
        }

        var issuedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var sequence = await _allowanceReader.NextAllowanceSequenceAsync(
            issuedAtUtc,
            cancellationToken);

        return IssueInvoiceAllowanceResult.Approved(new InvoiceAllowancePlan(
            simulatedInvoiceId,
            snapshot.RefundId,
            DemoAllowanceNumber.Format(issuedAtUtc, sequence),
            request.IdempotencyKey.Trim(),
            calculation.NetAmount,
            calculation.TaxAmount,
            calculation.Amount,
            issuedAtUtc,
            calculation.ResultingInvoiceStatus,
            calculation.Lines));
    }
}
