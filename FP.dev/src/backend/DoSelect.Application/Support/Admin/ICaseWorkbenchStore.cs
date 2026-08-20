using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin;

/// <summary>
/// Read-only persistence port for the unified case workbench. Implementations query the keyless
/// vw_CaseWorkbench view directly and must apply the authorized-case-type predicate in SQL before
/// any other filter, the cursor seek predicate, ordering, and Take — never after materialization.
/// </summary>
public interface ICaseWorkbenchStore
{
    /// <summary>
    /// Returns up to <paramref name="pageSize"/> rows for the given (already-authorized)
    /// <paramref name="caseTypes"/>, ordered by LastActivityAtUtc DESC, CasePublicId DESC,
    /// starting strictly after <paramref name="after"/> when supplied.
    /// </summary>
    Task<CaseWorkbenchPage> QueryPageAsync(
        IReadOnlyCollection<CaseWorkbenchCaseType> caseTypes,
        IReadOnlyCollection<string>? statuses,
        IReadOnlyCollection<CasePriority>? priorities,
        Guid? assigneePublicId,
        bool? overdueOnly,
        string? keyword,
        int pageSize,
        CaseWorkbenchCursorPosition? after,
        CancellationToken cancellationToken);
}

public sealed record CaseWorkbenchPage(IReadOnlyList<CaseWorkbenchItemDto> Items, bool HasMore);
