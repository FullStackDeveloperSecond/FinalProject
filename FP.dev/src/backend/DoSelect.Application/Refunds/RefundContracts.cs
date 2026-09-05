using DoSelect.Application.Common;
using DoSelect.Application.Support.Dtos;
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

/// <summary>後台退款清單查詢（A-21）。</summary>
/// <remarks>
/// <paramref name="FromUtc"/>（含）與 <paramref name="ToUtc"/>（不含）以退款建立時間為準；
/// <paramref name="Q"/> 只比對退款編號，避免為清單搜尋擴張 Refund 模組的跨界讀取例外。
/// </remarks>
public sealed record AdminRefundQuery(
    IReadOnlyList<RefundStatus>? Statuses,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Q,
    int PageNumber,
    int PageSize);

public static class AdminRefundQueryValidator
{
    public const int MaximumPageSize = 100;

    public static void RequireValid(AdminRefundQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PageNumber < 1)
        {
            throw DomainProblemException.Validation("pageNumber must be 1 or greater.");
        }

        if (query.PageSize < 1 || query.PageSize > MaximumPageSize)
        {
            throw DomainProblemException.Validation(
                $"pageSize must be between 1 and {MaximumPageSize}.");
        }

        if (query.FromUtc is { } fromUtc && query.ToUtc is { } toUtc && fromUtc >= toUtc)
        {
            throw DomainProblemException.Validation("fromUtc must be earlier than toUtc.");
        }

        if (query.Statuses is not null && query.Statuses.Any(status => !Enum.IsDefined(status)))
        {
            throw DomainProblemException.Validation("statuses contains an unknown refund status.");
        }
    }
}

/// <summary>
/// 讀出一筆退款的正式表示。實作屬 Infrastructure，本層不接觸 DbContext。
/// </summary>
public interface IRefundReader
{
    Task<PageResult<RefundDto>> ListAsync(
        AdminRefundQuery query,
        CancellationToken cancellationToken = default);

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
