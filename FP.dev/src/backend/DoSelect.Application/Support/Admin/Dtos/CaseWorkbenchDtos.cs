using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin.Dtos;

/// <summary>The three vw_CaseWorkbench branches (UNION ALL of Support/Return/Report cases).</summary>
public enum CaseWorkbenchCaseType
{
    Support,
    Return,
    Report,
}

/// <summary>
/// Request shape for the unified case workbench (UC-WORKBENCH-01). CaseTypes is the caller's
/// *requested* subset; it is intersected with the caller's authorized scope by
/// ICaseWorkbenchService, never used to broaden it. Null/empty CaseTypes means "all authorized
/// types". Statuses/Priorities are bounded per the API DTO contract (statuses 0..10, priorities
/// 0..4); Keyword is matched only against CaseNumber/Title.
/// </summary>
public sealed record CaseWorkbenchQuery(
    IReadOnlyCollection<CaseWorkbenchCaseType>? CaseTypes,
    IReadOnlyCollection<string>? Statuses,
    IReadOnlyCollection<CasePriority>? Priorities,
    Guid? AssigneePublicId,
    bool? OverdueOnly,
    string? Keyword,
    string? Cursor,
    int PageSize);

/// <summary>
/// The fixed 12-field workbench projection (統一案件工作台設計). Must never grow to include
/// CustomerReplyState, a workbench-local RowVersion, or a second AssignmentState — acting on a
/// row still requires calling its source-domain detail endpoint for RowVersion/AvailableActions.
/// </summary>
public sealed record CaseWorkbenchItemDto(
    string CaseType,
    Guid CasePublicId,
    string CaseNumber,
    string Title,
    string Status,
    CasePriority Priority,
    string RequesterDisplay,
    Guid? AssigneePublicId,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime? SlaDueAtUtc,
    bool IsOverdue);

/// <summary>
/// The keyset position of the last item on a page: (LastActivityAtUtc, CasePublicId), matching
/// the fixed LastActivityAtUtc DESC, CasePublicId DESC ordering. Carried inside the opaque
/// cursor, never exposed directly to callers.
/// </summary>
public sealed record CaseWorkbenchCursorPosition(DateTime LastActivityAtUtc, Guid CasePublicId);
