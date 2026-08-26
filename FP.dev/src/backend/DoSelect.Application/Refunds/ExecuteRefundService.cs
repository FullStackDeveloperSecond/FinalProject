using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Refunds;

/// <summary>
/// 一筆退款交易在執行當下的狀態快照。
/// <paramref name="StoredIdempotencyKey"/> 是建立退款時保存的金鑰，用於比對執行請求。
/// </summary>
public sealed record RefundExecutionSnapshot(
    long RefundId,
    RefundStatus Status,
    decimal? ApprovedAmount,
    decimal? SucceededAmount,
    decimal RefundableBalance,
    string StoredIdempotencyKey);

/// <summary>
/// 退款執行所需的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface IRefundExecutionReader
{
    /// <summary>
    /// 唯讀預覽用。<see cref="RefundExecutionSnapshot.RefundableBalance"/> 必須是已扣除先前
    /// 成功退款後的訂單可退款餘額。此路徑**不保證原子性**，實際執行請使用
    /// <see cref="IRefundExecutor"/>。
    /// </summary>
    Task<RefundExecutionSnapshot?> FindAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 實際執行退款。實作必須把「核對狀態與餘額 → 條件更新退款 → 副作用」放在同一個交易內，
/// 並以 rowversion 或等價的條件更新保證成功退款累計不超過已付金額。
/// </summary>
public interface IRefundExecutor
{
    Task<ExecuteRefundResult> ExecuteAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 執行退款的請求。
/// </summary>
/// <remarks>
/// 刻意沒有金額或分攤欄位（DEC-P287）：分攤一律由後端 <c>RefundCalculator</c> 依可信
/// 交易快照產生。<paramref name="ReasonCode"/> 與 <paramref name="Note"/> 只寫進中央
/// <c>AuditLog</c>，不在 <c>Refund</c> 重複保存（DEC-P289）。
/// <paramref name="ExecutedByAdminPublicId"/> 是管理員的 PublicId，不是內部 Identity Id。
/// </remarks>
public sealed record ExecuteRefundRequest(
    Guid RefundPublicId,
    string IdempotencyKey,
    string ExecutedByAdminUserId,
    string ReasonCode,
    string? Note,
    string CorrelationId,
    string TraceId);

/// <summary>
/// 通過檢查後要執行的退款。
/// </summary>
public sealed record RefundExecutionPlan(
    long RefundId,
    decimal Amount,
    string ExecutedByAdminUserId,
    string IdempotencyKey);

public sealed class ExecuteRefundResult
{
    private ExecuteRefundResult(string? errorCode, decimal? settledAmount, RefundExecutionPlan? plan)
    {
        ErrorCode = errorCode;
        SettledAmount = settledAmount;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>已經成功過的退款重送時，回傳既有金額而不再執行一次。</summary>
    public bool IsReplay => IsSuccess && Plan is null;

    public decimal? SettledAmount { get; }

    /// <summary>重播時為 <c>null</c>。</summary>
    public RefundExecutionPlan? Plan { get; }

    public static ExecuteRefundResult Failure(string errorCode) => new(errorCode, null, null);

    public static ExecuteRefundResult Replay(decimal settledAmount) =>
        new(null, settledAmount, null);

    public static ExecuteRefundResult Approved(RefundExecutionPlan plan) =>
        new(null, null, plan);

    public static ExecuteRefundResult Settled(decimal settledAmount, RefundExecutionPlan plan) =>
        new(null, settledAmount, plan);
}

/// <summary>
/// 退款執行的純決策，讀取預覽與實際執行共用同一份，避免兩處判斷漂移。
/// </summary>
public static class RefundExecutionDecision
{
    /// <summary>
    /// 依快照與請求判定結果。呼叫端必須已完成 TOTP 二次確認與授權；
    /// 前端確認視窗不是安全邊界。
    /// </summary>
    public static ExecuteRefundResult Evaluate(
        RefundExecutionSnapshot snapshot,
        ExecuteRefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);

        var presentedKey = request.IdempotencyKey.Trim();

        // 金鑰必須與建立退款時保存的一致。金鑰不符代表這不是同一個命令，
        // 不得對已完成的退款再產生副作用。
        if (!string.Equals(presentedKey, snapshot.StoredIdempotencyKey, StringComparison.Ordinal))
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.IdempotencyPayloadConflict);
        }

        // 相同金鑰、相同命令，且退款已成功：回同一結果，不再執行一次。
        if (snapshot.Status == RefundStatus.Succeeded)
        {
            return snapshot.SucceededAmount is { } settled
                ? ExecuteRefundResult.Replay(settled)
                : ExecuteRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
        }

        if (snapshot.Status != RefundStatus.Approved || snapshot.ApprovedAmount is not { } amount)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
        }

        if (amount > snapshot.RefundableBalance)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.RefundAmountExceeded);
        }

        return ExecuteRefundResult.Approved(new RefundExecutionPlan(
            snapshot.RefundId,
            amount,
            request.ExecutedByAdminUserId.Trim(),
            presentedKey));
    }

    /// <summary>請求本身的必填檢查。缺漏屬於呼叫端錯誤，由 API 層以驗證錯誤擋下。</summary>
    public static void RequireWellFormed(ExecuteRefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("The idempotency key is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ExecutedByAdminUserId))
        {
            throw new ArgumentException("The executing administrator is required.", nameof(request));
        }
    }
}

/// <summary>
/// 退款執行的**唯讀預覽**：後台在按下執行前顯示會發生什麼。
/// 這條路徑不保證原子性，兩個並行請求可能同時看到相同餘額，
/// 因此不得用它決定是否寫入；實際執行請用 <see cref="IRefundExecutor"/>。
/// 核准與執行是兩個 Use Case，本服務不做核准。
/// </summary>
public sealed class ExecuteRefundService
{
    private readonly IRefundExecutionReader _executionReader;

    public ExecuteRefundService(IRefundExecutionReader executionReader)
    {
        ArgumentNullException.ThrowIfNull(executionReader);

        _executionReader = executionReader;
    }

    public async Task<ExecuteRefundResult> PreviewAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        RefundExecutionDecision.RequireWellFormed(request);

        var snapshot = await _executionReader.FindAsync(request.RefundPublicId, cancellationToken);

        return snapshot is null
            ? ExecuteRefundResult.Failure(RefundErrorCodes.ResourceNotFound)
            : RefundExecutionDecision.Evaluate(snapshot, request);
    }
}
