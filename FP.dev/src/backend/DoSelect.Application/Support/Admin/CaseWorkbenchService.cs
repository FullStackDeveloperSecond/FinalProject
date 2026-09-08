using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin;

public sealed class CaseWorkbenchService : ICaseWorkbenchService
{
    private const string FingerprintTag = "case-workbench-v2";

    // caseTypes?:support/report/return[1..3] and statuses?:string[0..10] and
    // priorities?:string[0..4] per API DTO與Schema契約. Keyword has no documented bound in that
    // contract; 100 is a defensive cap (matches vw_CaseWorkbench.CaseNumber/Title being short
    // controlled-length columns) pending a confirmed value.
    private const int MaxCaseTypesCount = 3;
    private const int MaxStatusesCount = 10;
    private const int MaxPrioritiesCount = 4;
    private const int MaxKeywordLength = 100;
    private const int MaxStatusLength = 32;

    private readonly ICaseWorkbenchStore _store;

    public CaseWorkbenchService(ICaseWorkbenchStore store)
    {
        _store = store;
    }

    public async Task<CaseWorkbenchSearchResultDto> GetPageAsync(
        CaseWorkbenchQuery query,
        IReadOnlyCollection<CaseWorkbenchCaseType> authorizedCaseTypes,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken)
    {
        if (query.PageSize is < 1 or > 100)
        {
            throw DomainProblemException.Validation("pageSize must be between 1 and 100.");
        }

        if (query.CaseTypes is { Count: > 0 } requestedCaseTypes
            && (requestedCaseTypes.Count > MaxCaseTypesCount
                || requestedCaseTypes.Distinct().Count() != requestedCaseTypes.Count))
        {
            throw DomainProblemException.Validation("caseTypes must contain 1 to 3 distinct values.");
        }

        if (query.Statuses is { Count: > 0 } statuses
            && (statuses.Count > MaxStatusesCount
                || statuses.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > MaxStatusLength)))
        {
            throw DomainProblemException.Validation("statuses must contain 0 to 10 non-empty values.");
        }

        if (query.Priorities is { Count: > 0 } priorities
            && (priorities.Count > MaxPrioritiesCount || priorities.Any(p => !Enum.IsDefined(p))))
        {
            throw DomainProblemException.Validation("priorities must contain 0 to 4 valid values.");
        }

        if (query.Keyword is { Length: > MaxKeywordLength })
        {
            throw DomainProblemException.Validation($"keyword must not exceed {MaxKeywordLength} characters.");
        }

        if (query.Assignee is { } assignee && !Enum.IsDefined(assignee))
        {
            throw DomainProblemException.Validation("assignee must be a valid value.");
        }

        if (query.AssigneePublicId is not null
            && query.Assignee is not null and not CaseWorkbenchAssigneeFilter.Any)
        {
            throw DomainProblemException.Validation("assignee and assigneePublicId cannot both be specified.");
        }

        if (query.Sort is { } sort && !Enum.IsDefined(sort))
        {
            throw DomainProblemException.Validation("sort must be a valid value.");
        }

        ValidateDateRange(query.CreatedFrom, query.CreatedTo, "created");
        ValidateDateRange(query.LastActivityFrom, query.LastActivityTo, "lastActivity");

        // The requested case types only ever narrow the authorized scope — a request for a type
        // outside the scope is dropped, never used to broaden it.
        var authorizedScope = authorizedCaseTypes.Distinct().OrderBy(t => t).ToArray();
        var effectiveCaseTypes = query.CaseTypes is { Count: > 0 }
            ? query.CaseTypes.Distinct().Where(authorizedScope.Contains).OrderBy(t => t).ToArray()
            : authorizedScope;

        var fingerprint = ComputeFingerprint(query, authorizedScope, adminUserId, canSupervise);

        CaseWorkbenchCursorPosition? after = null;
        if (query.Cursor is not null)
        {
            if (!OpaqueCursorCodec.TryDecode<CaseWorkbenchCursorPosition>(query.Cursor, fingerprint, out var decoded))
            {
                throw DomainProblemException.Validation("The cursor is invalid or no longer applicable.");
            }

            after = decoded;
        }

        if (effectiveCaseTypes.Length == 0)
        {
            // Empty authorized scope, or every requested case type falls outside it: return an
            // empty page without querying the store so an unauthorized filter cannot be
            // distinguished from a genuinely empty result (no count/existence leak).
            return new CaseWorkbenchSearchResultDto([], null, false, 0);
        }

        var effectiveSort = query.Sort ?? CaseWorkbenchSortOrder.Latest;

        var page = await _store.QueryPageAsync(
            effectiveCaseTypes,
            query.Statuses,
            query.Priorities,
            query.AssigneePublicId,
            query.Assignee,
            query.CreatedFrom,
            query.CreatedTo,
            query.LastActivityFrom,
            query.LastActivityTo,
            effectiveSort,
            query.OverdueOnly,
            query.Keyword,
            query.PageSize,
            after,
            adminUserId,
            canSupervise,
            cancellationToken);

        string? nextCursor = null;
        if (page.HasMore && page.Items.Count > 0)
        {
            var last = page.Items[^1];
            nextCursor = OpaqueCursorCodec.Encode(
                new CaseWorkbenchCursorPosition(last.LastActivityAtUtc, last.CasePublicId),
                fingerprint);
        }

        return new CaseWorkbenchSearchResultDto(
            page.Items,
            nextCursor,
            page.HasMore,
            page.TotalCount);
    }

    private static void ValidateDateRange(DateOnly? from, DateOnly? to, string name)
    {
        if (from is not null && to is not null && from > to)
        {
            throw DomainProblemException.Validation($"{name}From must not be after {name}To.");
        }
    }

    /// <summary>
    /// Binds the cursor to every filter plus the caller's authorized scope so a decoded cursor
    /// from a different query shape, or from a caller whose authorized scope has since changed,
    /// is rejected as a mismatch rather than replayed. Collections are canonicalized (deduped,
    /// sorted) so equivalent filters expressed in a different order still fingerprint equal.
    /// </summary>
    private static string ComputeFingerprint(
        CaseWorkbenchQuery query,
        IReadOnlyCollection<CaseWorkbenchCaseType> authorizedScope,
        string adminUserId,
        bool canSupervise)
    {
        var scope = string.Join(",", authorizedScope.OrderBy(t => t));
        var requestedCaseTypes = query.CaseTypes is { Count: > 0 }
            ? string.Join(",", query.CaseTypes.Distinct().OrderBy(t => t))
            : string.Empty;
        var statuses = query.Statuses is { Count: > 0 }
            ? string.Join(",", query.Statuses.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal))
            : string.Empty;
        var priorities = query.Priorities is { Count: > 0 }
            ? string.Join(",", query.Priorities.Distinct().OrderBy(p => p))
            : string.Empty;

        return OpaqueCursorCodec.ComputeFingerprint(
            FingerprintTag,
            scope,
            requestedCaseTypes,
            statuses,
            priorities,
            query.AssigneePublicId?.ToString("D"),
            query.Assignee?.ToString(),
            query.CreatedFrom?.ToString("O"),
            query.CreatedTo?.ToString("O"),
            query.LastActivityFrom?.ToString("O"),
            query.LastActivityTo?.ToString("O"),
            (query.Sort ?? CaseWorkbenchSortOrder.Latest).ToString(),
            query.OverdueOnly?.ToString(),
            query.Keyword?.Trim(),
            adminUserId,
            canSupervise.ToString());
    }
}
