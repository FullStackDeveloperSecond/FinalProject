using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support.Admin;

/// <summary>
/// Queries the keyless read-only vw_CaseWorkbench view. The case-type predicate (the caller's
/// already-intersected authorized scope) is applied first, before every other filter, the cursor
/// seek predicate, ordering, and Take — so an unauthorized row is never fetched, let alone
/// materialized.
/// </summary>
public sealed class CaseWorkbenchStore : ICaseWorkbenchStore
{
    private readonly DoSelectDbContext _dbContext;

    public CaseWorkbenchStore(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CaseWorkbenchPage> QueryPageAsync(
        IReadOnlyCollection<CaseWorkbenchCaseType> caseTypes,
        IReadOnlyCollection<string>? statuses,
        IReadOnlyCollection<CasePriority>? priorities,
        Guid? assigneePublicId,
        bool? overdueOnly,
        string? keyword,
        int pageSize,
        CaseWorkbenchCursorPosition? after,
        CancellationToken cancellationToken)
    {
        // CaseWorkbenchCaseType member names match vw_CaseWorkbench.CaseType exactly
        // ("Support"/"Return"/"Report"); computed in C# so the authorization predicate below is a
        // plain parameterized IN, not a per-row enum-to-string conversion.
        var caseTypeNames = caseTypes.Select(t => t.ToString()).ToArray();

        var query = _dbContext.CaseWorkbench
            .AsNoTracking()
            .Where(r => caseTypeNames.Contains(r.CaseType));

        if (statuses is { Count: > 0 })
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        if (priorities is { Count: > 0 })
        {
            query = query.Where(r => priorities.Contains(r.Priority));
        }

        if (assigneePublicId is not null)
        {
            query = query.Where(r => r.AssigneePublicId == assigneePublicId);
        }

        if (overdueOnly == true)
        {
            query = query.Where(r => r.IsOverdue);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmed = keyword.Trim();
            query = query.Where(r => r.CaseNumber.Contains(trimmed) || r.Title.Contains(trimmed));
        }

        if (after is not null)
        {
            query = query.Where(r =>
                r.LastActivityAtUtc < after.LastActivityAtUtc
                || (r.LastActivityAtUtc == after.LastActivityAtUtc && r.CasePublicId < after.CasePublicId));
        }

        var rows = await query
            .OrderByDescending(r => r.LastActivityAtUtc)
            .ThenByDescending(r => r.CasePublicId)
            .Take(pageSize + 1)
            .Select(r => new CaseWorkbenchItemDto(
                r.CaseType,
                r.CasePublicId,
                r.CaseNumber,
                r.Title,
                r.Status,
                r.Priority,
                r.RequesterDisplay,
                r.AssigneePublicId,
                r.CreatedAtUtc,
                r.LastActivityAtUtc,
                r.SlaDueAtUtc,
                r.IsOverdue))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows.Take(pageSize).ToList() : rows;
        return new CaseWorkbenchPage(items, hasMore);
    }
}
