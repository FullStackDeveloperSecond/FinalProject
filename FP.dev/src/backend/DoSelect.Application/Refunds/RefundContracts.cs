using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Refunds;

/// <summary>
/// 管理員摘要（API DTO與Schema契約第 113 行）。
/// </summary>
/// <remarks>
/// 只回管理員 <paramref name="PublicId"/>，**不回 Internal Identity ID**。
/// <paramref name="MaskedLabel"/> 優先使用遮蔽後的 DisplayName，缺少時才用遮蔽 Email；
/// 兩者都不得回完整值（DEC-P290）。
/// </remarks>
public sealed record MaskedAdminSummaryDto(Guid PublicId, string MaskedLabel);

/// <summary>
/// 一筆退款分攤（API DTO與Schema契約第 112 行）。
/// </summary>
/// <remarks>
/// <paramref name="Amount"/> **一律為正值**，增加退款或從退款扣回由
/// <paramref name="Type"/> 決定。<c>itemRefund</c> 必須同時有
/// <paramref name="OrderItemPublicId"/> 與正整數 <paramref name="Quantity"/>；
/// 其他六種類型兩欄皆為 <c>null</c>。V1 新寫入禁止 <c>otherAdjustment</c>，
/// 但既有資料仍可能出現，因此讀取端不得假設它不存在。
/// </remarks>
public sealed record RefundAllocationDto(
    Guid? OrderItemPublicId,
    int? Quantity,
    RefundAllocationType Type,
    decimal Amount);

/// <summary>
/// 退款的正式對外表示（API DTO與Schema契約第 114 行）。
/// </summary>
public sealed record RefundDto(
    Guid PublicId,
    string RefundNumber,
    Guid OrderPublicId,
    Guid? ReturnPublicId,
    RefundStatus Status,
    decimal RequestedAmount,
    decimal? ApprovedAmount,
    decimal? SucceededAmount,
    IReadOnlyList<RefundAllocationDto> Allocations,
    MaskedAdminSummaryDto? RequestedBy,
    MaskedAdminSummaryDto? ApprovedBy,
    MaskedAdminSummaryDto? ExecutedBy,
    DateTime CreatedAtUtc,
    DateTime? SucceededAtUtc,
    byte[] RowVersion);

/// <summary>
/// 讀出一筆退款的正式表示。實作屬 Infrastructure，本層不接觸 DbContext。
/// </summary>
public interface IRefundReader
{
    /// <summary>
    /// 依 PublicId 讀出退款。找不到時回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 三個管理員欄位在資料庫保存的是 Identity 內部 Id，實作必須在此換成
    /// <c>ApplicationUser.PublicId</c> 與遮蔽標籤 —— 這是唯一的轉換點，
    /// 內部 Id 不得離開 Infrastructure。
    /// </remarks>
    Task<RefundDto?> FindByPublicIdAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default);
}
