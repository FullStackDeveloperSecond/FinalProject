using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin;

public sealed class CaseWorkbenchService : ICaseWorkbenchService
{
    private const string FingerprintTag = "case-workbench-v1";

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

    public async Task<CursorPage<CaseWorkbenchItemDto>> GetPageAsync(
        CaseWorkbenchQuery query,
        IReadOnlyCollection<CaseWorkbenchCaseType> authorizedCaseTypes,
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

        // The requested case types only ever narrow the authorized scope — a request for a type
        // outside the scope is dropped, never used to broaden it.
        var authorizedScope = authorizedCaseTypes.Distinct().OrderBy(t => t).ToArray();
        var effectiveCaseTypes = query.CaseTypes is { Count: > 0 }
            ? query.CaseTypes.Distinct().Where(authorizedScope.Contains).OrderBy(t => t).ToArray()
            : authorizedScope;

        var fingerprint = ComputeFingerprint(query, authorizedScope);

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
            return new CursorPage<CaseWorkbenchItemDto>([], null, false);
        }

        var page = await _store.QueryPageAsync(
            effectiveCaseTypes,
            query.Statuses,
            query.Priorities,
            query.AssigneePublicId,
            query.OverdueOnly,
            query.Keyword,
            query.PageSize,
            after,
            cancellationToken);

        string? nextCursor = null;
        if (page.HasMore && page.Items.Count > 0)
        {
            var last = page.Items[^1];
            nextCursor = OpaqueCursorCodec.Encode(
                new CaseWorkbenchCursorPosition(last.LastActivityAtUtc, last.CasePublicId),
                fingerprint);
        }

        return new CursorPage<CaseWorkbenchItemDto>(page.Items, nextCursor, page.HasMore);
    }

    /// <summary>
    /// Binds the cursor to every filter plus the caller's authorized scope so a decoded cursor
    /// from a different query shape, or from a caller whose authorized scope has since changed,
    /// is rejected as a mismatch rather than replayed. Collections are canonicalized (deduped,
    /// sorted) so equivalent filters expressed in a different order still fingerprint equal.
    /// </summary>
    private static string ComputeFingerprint(
        CaseWorkbenchQuery query,
        IReadOnlyCollection<CaseWorkbenchCaseType> authorizedScope)
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
            query.OverdueOnly?.ToString(),
            query.Keyword?.Trim());
    }
}
