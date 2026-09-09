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
        CaseWorkbenchAssigneeFilter? assignee,
        DateOnly? createdFrom,
        DateOnly? createdTo,
        DateOnly? lastActivityFrom,
        DateOnly? lastActivityTo,
        CaseWorkbenchSortOrder sort,
        bool? overdueOnly,
        string? keyword,
        int pageSize,
        CaseWorkbenchCursorPosition? after,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken,
        int? pageNumber = null)
    {
        // CaseWorkbenchCaseType member names match vw_CaseWorkbench.CaseType exactly
        // ("Support"/"Return"/"Report"); computed in C# so the authorization predicate below is a
        // plain parameterized IN, not a per-row enum-to-string conversion.
        var caseTypeNames = caseTypes.Select(t => t.ToString()).ToArray();

        var query = _dbContext.CaseWorkbench
            .AsNoTracking()
            .Where(r => caseTypeNames.Contains(r.CaseType));

        if (!canSupervise)
        {
            query = query.Where(r => r.AssigneePublicId == null
                || _dbContext.AdminProfiles.Any(profile =>
                    profile.IsActive
                    && profile.UserId == adminUserId
                    && profile.PublicId == r.AssigneePublicId));
        }


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

        query = assignee switch
        {
            CaseWorkbenchAssigneeFilter.Mine => query.Where(r =>
                _dbContext.AdminProfiles.Any(profile =>
                    profile.IsActive
                    && profile.UserId == adminUserId
                    && profile.PublicId == r.AssigneePublicId)),
            CaseWorkbenchAssigneeFilter.Unassigned => query.Where(r => r.AssigneePublicId == null),
            CaseWorkbenchAssigneeFilter.Assigned => query.Where(r => r.AssigneePublicId != null),
            _ => query,
        };

        if (createdFrom is not null)
        {
            var lower = createdFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAtUtc >= lower);
        }

        if (createdTo is not null)
        {
            var upper = createdTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAtUtc <= upper);
        }

        if (lastActivityFrom is not null)
        {
            var lower = lastActivityFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.LastActivityAtUtc >= lower);
        }

        if (lastActivityTo is not null)
        {
            var upper = lastActivityTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(r => r.LastActivityAtUtc <= upper);
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

        var totalCount = await query.CountAsync(cancellationToken);

        if (after is not null && sort == CaseWorkbenchSortOrder.Latest)
        {
            query = query.Where(r =>
                r.LastActivityAtUtc < after.LastActivityAtUtc
                || (r.LastActivityAtUtc == after.LastActivityAtUtc && r.CasePublicId < after.CasePublicId));
        }
        else if (after is not null)
        {
            query = query.Where(r =>
                r.LastActivityAtUtc > after.LastActivityAtUtc
                || (r.LastActivityAtUtc == after.LastActivityAtUtc && r.CasePublicId > after.CasePublicId));
        }

        query = sort == CaseWorkbenchSortOrder.Latest
            ? query.OrderByDescending(r => r.LastActivityAtUtc).ThenByDescending(r => r.CasePublicId)
            : query.OrderBy(r => r.LastActivityAtUtc).ThenBy(r => r.CasePublicId);

        var rows = await query
            .Skip(((pageNumber ?? 1) - 1) * pageSize)
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
        return new CaseWorkbenchPage(items, hasMore, totalCount);
    }
}
