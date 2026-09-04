using System.Net;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Refunds;

/// <summary>
/// 一筆退款交易在核准當下的狀態快照。
/// </summary>
/// <remarks>
/// 與 <see cref="RefundExecutionSnapshot"/> 共用同一份 <see cref="RefundTrustedInputs"/>——
/// 核准與執行都依<b>同一條路徑</b>算金額，避免「核准時算得出來、執行時算不出來」的落差。
/// <para>
/// 刻意不另外提供唯讀預覽用的 Reader／Service 包裝：<c>ExecuteRefundService.PreviewAsync</c>
/// 就是這個形狀，但目前沒有任何 Controller 真正呼叫它——保留一份沒人用的預覽路徑只會
/// 增加要維護、卻永遠不會被走到的程式碼。<see cref="IRefundApprover"/> 的實作自己在交易內
/// 組出這份快照，不透過額外一層非原子的 Reader 介面。
/// </para>
/// </remarks>
public sealed record RefundApprovalSnapshot(
    long RefundId,
    RefundStatus Status,
    decimal RequestedAmount,
    byte[] RowVersion,
    RefundTrustedInputs? TrustedInputs);

/// <summary>
/// 實際核准退款。實作必須把「核對狀態 → 重新計算可信金額 → 條件更新退款」放在同一個
/// 交易內，並以 rowversion 或等價的條件更新防止並行核准衝突。
/// </summary>
public interface IRefundApprover
{
    Task<ApproveRefundResult> ApproveAsync(
        ApproveRefundRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 核准退款的請求。
/// </summary>
/// <remarks>
/// 刻意沒有金額欄位（alex 2026-09-04 #98 WP2 裁定）：<c>ApprovedAmount</c> 一律由後端
/// 依可信交易快照重新計算，管理員只確認、填理由與 RowVersion。<paramref name="ReasonCode"/>
/// 與 <paramref name="Note"/> 只寫進中央 <c>AuditLog</c>，不在 <c>Refund</c> 重複保存
/// （DEC-P289，與 <see cref="ExecuteRefundRequest"/> 同一個約定）。
/// </remarks>
public sealed record ApproveRefundRequest(
    Guid RefundPublicId,
    byte[] RefundRowVersion,
    string IdempotencyKey,
    string ApprovedByAdminUserId,
    string ReasonCode,
    string? Note,
    string CorrelationId,
    string TraceId,
    IPAddress? RemoteIpAddress);

/// <summary>
/// 通過檢查後要核准的退款。
/// </summary>
public sealed record RefundApprovalPlan(
    long RefundId,
    decimal ApprovedAmount,
    string ApprovedByAdminUserId,
    string IdempotencyKey,
    byte[] ExpectedRefundRowVersion);

public sealed class ApproveRefundResult
{
    private ApproveRefundResult(string? errorCode, decimal? approvedAmount, RefundApprovalPlan? plan)
    {
        ErrorCode = errorCode;
        ApprovedAmount = approvedAmount;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    public decimal? ApprovedAmount { get; }

    /// <summary>拒絕時為 <c>null</c>。</summary>
    public RefundApprovalPlan? Plan { get; }

    public static ApproveRefundResult Failure(string errorCode) => new(errorCode, null, null);

    /// <summary>共用 <c>IIdempotencyExecutor</c> 判定為重播時，回放先前保存的金額。</summary>
    public static ApproveRefundResult Replayed(decimal approvedAmount) =>
        new(null, approvedAmount, null);

    /// <summary>
    /// 決策通過、尚未寫入——呼叫端據此執行 <see cref="RefundApprovalPlan"/>。
    /// 與 <see cref="Settled"/> 的差別和 <c>ExecuteRefundResult.Approved</c>／
    /// <c>Settled</c> 同一個約定：這裡只是「可以核准」，真正寫進資料庫後才是
    /// <see cref="Settled"/>。
    /// </summary>
    public static ApproveRefundResult Approved(RefundApprovalPlan plan) =>
        new(null, plan.ApprovedAmount, plan);

    /// <summary>首次核准成功寫入後的結果——首次執行與重播都回同一份形狀。</summary>
    public static ApproveRefundResult Settled(decimal approvedAmount, RefundApprovalPlan plan) =>
        new(null, approvedAmount, plan);
}

/// <summary>
/// 退款核准的純決策，讀取預覽與實際核准共用同一份，避免兩處判斷漂移。
/// </summary>
public static class RefundApprovalDecision
{
    /// <summary>
    /// 依快照與請求判定結果。呼叫端必須已完成 TOTP 二次確認與授權；
    /// 前端確認視窗不是安全邊界。
    /// </summary>
    public static ApproveRefundResult Evaluate(
        RefundApprovalSnapshot snapshot,
        ApproveRefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);

        if (!RowVersionMatches(request.RefundRowVersion, snapshot.RowVersion))
        {
            return ApproveRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
        }

        // 只有 PendingReview 可以核准（Refund.AllowedTransitions）。已核准／已拒絕／
        // 已取消／執行中／已結清都不是「等待第一次核准」，一律狀態衝突。
        if (snapshot.Status != RefundStatus.PendingReview)
        {
            return ApproveRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
        }

        // E1 的同一個約定：上游可信快照未齊全時必須拒絕，不得以估算值補齊。
        if (snapshot.TrustedInputs is not { } trustedInputs)
        {
            return ApproveRefundResult.Failure(RefundErrorCodes.RefundSnapshotUnavailable);
        }

        var calculation = RefundCalculator.Calculate(new RefundCalculationRequest(
            trustedInputs.Order,
            trustedInputs.Lines,
            trustedInputs.Reason,
            trustedInputs.AssemblyDisposition,
            trustedInputs.ReturnShippingCost));

        // 計算器本身失敗（含淨額 <= 0）時原樣回傳它的錯誤碼——包含
        // RefundAmountExceeded。建立這筆退款之後，若同一張訂單受理了其他退貨，
        // 可信快照重算出的淨額可能已經降到 0 或負數；退款維持 PendingReview，
        // 需要管理員介入決定後續（見 PR 說明的已知邊界）。
        if (!calculation.IsSuccess)
        {
            return ApproveRefundResult.Failure(calculation.ErrorCode!);
        }

        // 依 DEC-P287／#98 精神，核准金額不得高於建立當下記錄的申請金額——
        // Refund.Approve 本身也會擋這個方向（approvedAmount > RequestedAmount 丟例外），
        // 這裡先擋，讓呼叫端拿到正確的錯誤碼而不是未處理例外變成的 500。結構上這個
        // 方向理論上不會發生（AlreadyReturnedQuantity 只增不減，淨額只會持平或降低），
        // 仍保留防禦。
        if (calculation.NetRefundAmount > snapshot.RequestedAmount)
        {
            return ApproveRefundResult.Failure(RefundErrorCodes.RefundCalculationMismatch);
        }

        return ApproveRefundResult.Approved(new RefundApprovalPlan(
            snapshot.RefundId,
            calculation.NetRefundAmount,
            request.ApprovedByAdminUserId.Trim(),
            request.IdempotencyKey.Trim(),
            snapshot.RowVersion));
    }

    private static bool RowVersionMatches(byte[]? presented, byte[]? current) =>
        presented is not null &&
        current is not null &&
        presented.AsSpan().SequenceEqual(current);

    /// <summary>
    /// 請求本身的必填與格式檢查。與 <see cref="RefundExecutionDecision.RequireWellFormed"/>
    /// 同一套規則——reasonCode／note 最終都進中央 Audit，共用同一份判斷不複製規則。
    /// </summary>
    public static void RequireWellFormed(ApproveRefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw DomainProblemException.Validation("The idempotency key is required.");
        }

        if (request.IdempotencyKey.Trim().Length > 128)
        {
            throw DomainProblemException.Validation(
                "The idempotency key cannot exceed 128 characters.");
        }

        if (request.RefundRowVersion is not { Length: 8 })
        {
            throw DomainProblemException.Validation(
                "The refund row version must be an 8-byte value.");
        }

        if (string.IsNullOrWhiteSpace(request.ApprovedByAdminUserId))
        {
            throw DomainProblemException.Validation("The approving administrator is required.");
        }

        try
        {
            AuditFieldChange.RequireSafeCode(request.ReasonCode, nameof(request.ReasonCode), 64);
            AuditWriteRequest.RequireSafeNote(request.Note, allowsNote: true);
        }
        catch (ArgumentException exception)
        {
            throw DomainProblemException.Validation(exception.Message);
        }
    }
}
