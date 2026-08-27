using System.Net;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Refunds;

/// <summary>
/// 後端產生七類分攤所需的完整可信交易快照。
/// </summary>
/// <remarks>
/// 依 DEC-P287，分攤只能由後端依**下單與退貨核准當下**的快照計算，不得回查目前的
/// 商品或優惠券設定，也不得由管理端傳入。
/// <para>
/// <see cref="Reason"/>、<see cref="AssemblyDisposition"/> 與
/// <see cref="ReturnShippingCost"/> 三項目前在資料庫沒有任何持久化來源：
/// <c>ReturnRequest.ReasonCode</c> 是自由文字而非 <see cref="ReturnReason"/> 列舉，
/// 另外兩項則完全不存在（kafen 的 M-12 已合併但未涵蓋）。因此讀取端目前一律
/// 回 <c>null</c>，退款執行被拒絕 —— 這是 E1 裁定要求的行為，不是尚未實作。
/// </para>
/// </remarks>
public sealed record RefundTrustedInputs(
    RefundOrderSnapshot Order,
    IReadOnlyList<RefundLineRequest> Lines,
    ReturnReason Reason,
    AssemblyFeeDisposition AssemblyDisposition,
    decimal ReturnShippingCost);

/// <summary>
/// 一筆退款交易在執行當下的狀態快照。
/// </summary>
/// <remarks>
/// <paramref name="RowVersion"/> 是讀取當下的退款版本，用來比對呼叫端持有的
/// <c>refundRowVersion</c>。
/// <para>
/// 這裡**不再**保存建立退款時的冪等金鑰。那個值是「建立退款」這個操作的金鑰，
/// 不是 <c>refund.execute</c> 這一次操作的；拿它去比對執行請求，會讓正常使用新金鑰
/// 的呼叫端直接被判 <c>idempotency_payload_conflict</c>。重播與 Payload 衝突改由
/// 共用 <c>IIdempotencyExecutor</c> 負責（Operation <c>refund.execute</c>）。
/// </para>
/// </remarks>
public sealed record RefundExecutionSnapshot(
    long RefundId,
    RefundStatus Status,
    decimal? ApprovedAmount,
    decimal? SucceededAmount,
    decimal RefundableBalance,
    byte[] RowVersion,
    RefundTrustedInputs? TrustedInputs);

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
    byte[] RefundRowVersion,
    string IdempotencyKey,
    string ExecutedByAdminUserId,
    string ReasonCode,
    string? Note,
    string CorrelationId,
    string TraceId,
    IPAddress? RemoteIpAddress);

/// <summary>
/// 通過檢查後要執行的退款。
/// </summary>
/// <remarks>
/// <paramref name="ExpectedRefundRowVersion"/> 是本次決策依據的退款版本，Writer 必須在
/// 同一交易內以條件更新再次比對。<paramref name="Allocations"/> 是後端算出的完整七類
/// 分攤，必須與退款狀態同交易寫入 —— 沒有分攤的退款會讓對帳、發票折讓與稽核的
/// <c>allocationCount</c> 全部不完整。
/// </remarks>
public sealed record RefundExecutionPlan(
    long RefundId,
    decimal Amount,
    string ExecutedByAdminUserId,
    string IdempotencyKey,
    byte[] ExpectedRefundRowVersion,
    IReadOnlyList<RefundAllocationDraft> Allocations);

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

    public decimal? SettledAmount { get; }

    /// <summary>拒絕時為 <c>null</c>。</summary>
    public RefundExecutionPlan? Plan { get; }

    public static ExecuteRefundResult Failure(string errorCode) => new(errorCode, null, null);

    /// <summary>
    /// 共用 <c>IIdempotencyExecutor</c> 判定為重播時，回放先前保存的金額。
    /// </summary>
    /// <remarks>
    /// 這個工廠只給 Executor 的 replayFactory 用。決策層看不到重播 ——
    /// 走到 <see cref="RefundExecutionDecision.Evaluate"/> 的一定是首次執行。
    /// </remarks>
    public static ExecuteRefundResult Replayed(decimal settledAmount) =>
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

        // 呼叫端持有的版本與資料庫目前版本不符，代表它看到的金額或狀態已經過期。
        // rowversion 只能擋「伺服器讀取之後」的競爭，擋不掉「送進來時就已過時」——
        // 那正是管理員拿著舊畫面按下執行的情況。
        if (!RowVersionMatches(request.RefundRowVersion, snapshot.RowVersion))
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
        }

        // 重播與 Payload 衝突由外層的共用 IIdempotencyExecutor 在此之前判斷完畢。
        // 走到這裡的一定是首次執行，因此已成功的退款只可能是「換了一把金鑰再送」，
        // 那是狀態衝突而不是重播。
        if (snapshot.Status == RefundStatus.Succeeded)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
        }

        // 失敗的退款可以重試：Refund.AllowedTransitions 本來就允許 Failed → Processing，
        // ApprovedAmount 也保留著。只認 Approved 會讓一次暫時性失敗變成永久卡死。
        if (snapshot.Status is not (RefundStatus.Approved or RefundStatus.Failed) ||
            snapshot.ApprovedAmount is not { } amount)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
        }

        if (amount > snapshot.RefundableBalance)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.RefundAmountExceeded);
        }

        // E1：上游可信快照未齊全時必須拒絕，不得以估算值或管理端傳入的分攤補齊。
        // 一筆沒有分攤的成功退款，會讓對帳、發票折讓與稽核的 allocationCount 全部失真，
        // 而且寫進去之後不可變 —— 拒絕比事後修正便宜得多。
        //
        // 這裡刻意**不用** refund_state_conflict：那個碼的意思是「退款目前狀態不允許
        // 操作」，管理員收到會去查退款狀態，但實際原因與退款狀態無關，而是退貨核准端
        // 的可信資料還沒齊。alex 於 PR #16 裁定專屬碼 refund_snapshot_unavailable。
        if (snapshot.TrustedInputs is not { } trustedInputs)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.RefundSnapshotUnavailable);
        }

        var calculation = RefundCalculator.Calculate(new RefundCalculationRequest(
            trustedInputs.Order,
            trustedInputs.Lines,
            trustedInputs.Reason,
            trustedInputs.AssemblyDisposition,
            trustedInputs.ReturnShippingCost));

        var allocations = RefundAllocationDrafts.From(calculation);

        return ExecuteRefundResult.Approved(new RefundExecutionPlan(
            snapshot.RefundId,
            amount,
            request.ExecutedByAdminUserId.Trim(),
            presentedKey,
            snapshot.RowVersion,
            allocations));
    }

    private static bool RowVersionMatches(byte[]? presented, byte[]? current) =>
        presented is not null &&
        current is not null &&
        presented.AsSpan().SequenceEqual(current);

    /// <summary>請求本身的必填檢查。缺漏屬於呼叫端錯誤，由 API 層以驗證錯誤擋下。</summary>
    public static void RequireWellFormed(ExecuteRefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("The idempotency key is required.", nameof(request));
        }

        // SQL Server 的 rowversion 固定 8 bytes。長度不對的值不可能是任何一列的版本，
        // 在這裡擋下可得到 400，而不是讓它一路走到比對失敗後變成語意不對的 409。
        if (request.RefundRowVersion is not { Length: 8 })
        {
            throw new ArgumentException(
                "The refund row version must be an 8-byte value.", nameof(request));
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
