using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.Support.Admin.Dtos;

namespace DoSelect.Application.Support.Admin;

public sealed class SupportSlaQueueService : ISupportSlaQueueService
{
    // The SLA queue has no filters beyond pagination today; the tag alone still stops a
    // workbench cursor (or any other feature's cursor) from being replayed here by accident.
    private const string FingerprintTag = "support-sla-queue-v1";

    private readonly ISupportSlaQueueStore _store;
    private readonly TimeProvider _timeProvider;

    public SupportSlaQueueService(ISupportSlaQueueStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<SupportSlaQueueResponse> GetPageAsync(
        SupportSlaQueueQuery query,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken)
    {
        if (query.PageSize is < 1 or > 100)
        {
            throw DomainProblemException.Validation("pageSize must be between 1 and 100.");
        }

        if (query.PageNumber is < 1 or > 1000000 || query.Search?.Length > 100 ||
            (query.Status is { } status && !Enum.IsDefined(status)) ||
            (query.Priority is { } priority && !Enum.IsDefined(priority)) ||
            query.Assignee is not ("all" or "mine" or "unassigned") || query.Sort is not ("deadline" or "recent") ||
            (query.PageNumber is not null && query.Cursor is not null) || (query.PageNumber is null && query.Sort != "deadline"))
            throw DomainProblemException.Validation("篩選或分頁條件不正確。");

        var fingerprint = OpaqueCursorCodec.ComputeFingerprint(
            FingerprintTag,
            adminUserId,
            canSupervise.ToString(), query.Search?.Trim() ?? "", query.Status?.ToString() ?? "",
            query.Priority?.ToString() ?? "", query.OnlyOverdue.ToString(), query.Assignee, query.Sort);

        SupportSlaCursorPosition? after = null;
        if (query.Cursor is not null)
        {
            if (!OpaqueCursorCodec.TryDecode<SupportSlaCursorPosition>(query.Cursor, fingerprint, out var decoded))
            {
                throw DomainProblemException.Validation("The cursor is invalid or no longer applicable.");
            }

            after = decoded;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var page = await _store.QueryPageAsync(
            query.PageSize, after, nowUtc, adminUserId, canSupervise, cancellationToken, query);

        string? nextCursor = null;
        if (query.PageNumber is null && page.HasMore && page.Items.Count > 0)
        {
            var last = page.Items[^1];
            nextCursor = OpaqueCursorCodec.Encode(
                new SupportSlaCursorPosition(last.IsOverdue, last.EffectiveDueAtUtc, last.TicketPublicId),
                fingerprint);
        }

        return new SupportSlaQueueResponse(page.Items, nextCursor, page.HasMore, page.TotalCount, query.PageNumber);
    }
}
