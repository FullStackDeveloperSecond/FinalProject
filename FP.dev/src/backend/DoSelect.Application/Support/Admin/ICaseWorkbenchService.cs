using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin.Dtos;

namespace DoSelect.Application.Support.Admin;

/// <summary>
/// Admin-facing read use case for the unified case workbench (UC-WORKBENCH-01). The caller is
/// responsible for resolving the acting admin's authorized case-type scope (per role/Policy)
/// before invoking this service; <paramref name="authorizedCaseTypes"/> in
/// <see cref="GetPageAsync"/> is a trusted Application-layer input, not re-derived here.
/// </summary>
public interface ICaseWorkbenchService
{
    /// <summary>
    /// Throws DomainProblemException (validation_failed, 400) when PageSize is outside 1..100,
    /// a filter collection exceeds its documented bound, or Cursor is present but
    /// malformed/mismatched for this query shape (including a stale authorized scope). Returns
    /// an empty page — never an error — when <paramref name="authorizedCaseTypes"/> is empty or
    /// does not intersect the requested case types.
    /// </summary>
    Task<CursorPage<CaseWorkbenchItemDto>> GetPageAsync(
        CaseWorkbenchQuery query,
        IReadOnlyCollection<CaseWorkbenchCaseType> authorizedCaseTypes,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken);
}
