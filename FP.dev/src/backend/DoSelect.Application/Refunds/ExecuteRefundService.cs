using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Refunds;

/// <summary>
/// 一筆退款交易在執行當下的狀態快照。
/// </summary>
public sealed record RefundExecutionSnapshot(
    long RefundId,
    RefundStatus Status,
    decimal? ApprovedAmount,
    decimal? SucceededAmount,
    decimal RefundableBalance);

/// <summary>
/// 退款執行所需的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface IRefundExecutionReader
{
    /// <summary>
    /// <paramref name="refundPublicId"/> 為對外識別。<see cref="RefundExecutionSnapshot.RefundableBalance"/>
    /// 必須是已扣除先前成功退款後的訂單可退款餘額，且與本次執行在同一交易內取得。
    /// </summary>
    Task<RefundExecutionSnapshot?> FindAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default);
}

public sealed record ExecuteRefundRequest(
    Guid RefundPublicId,
    string IdempotencyKey,
    string ExecutedByAdminUserId);

/// <summary>
/// 通過檢查後要執行的退款。實際狀態轉移與同交易副作用由退款端點負責。
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
}

/// <summary>
/// 決定要不要執行一筆已核准的退款。核准與執行是兩個 Use Case，本服務只負責執行前的檢查，
/// 不做核准、不寫資料庫。呼叫端必須已完成 TOTP 二次確認與授權；前端確認視窗不是安全邊界。
/// </summary>
public sealed class ExecuteRefundService
{
    private readonly IRefundExecutionReader _executionReader;

    public ExecuteRefundService(IRefundExecutionReader executionReader)
    {
        ArgumentNullException.ThrowIfNull(executionReader);

        _executionReader = executionReader;
    }

    public async Task<ExecuteRefundResult> ExecuteAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken = default)
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

        var snapshot = await _executionReader.FindAsync(request.RefundPublicId, cancellationToken);
        if (snapshot is null)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.ResourceNotFound);
        }

        // 已成功的退款重送時回同一結果，不產生第二次金流副作用。
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
            request.IdempotencyKey.Trim()));
    }
}
