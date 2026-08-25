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

    public async Task<CursorPage<SupportSlaItemDto>> GetPageAsync(
        SupportSlaQueueQuery query,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken)
    {
        if (query.PageSize is < 1 or > 100)
        {
            throw DomainProblemException.Validation("pageSize must be between 1 and 100.");
        }

        var fingerprint = OpaqueCursorCodec.ComputeFingerprint(
            FingerprintTag,
            adminUserId,
            canSupervise.ToString());

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
            query.PageSize, after, nowUtc, adminUserId, canSupervise, cancellationToken);

        string? nextCursor = null;
        if (page.HasMore && page.Items.Count > 0)
        {
            var last = page.Items[^1];
            nextCursor = OpaqueCursorCodec.Encode(
                new SupportSlaCursorPosition(last.IsOverdue, last.EffectiveDueAtUtc, last.TicketPublicId),
                fingerprint);
        }

        return new CursorPage<SupportSlaItemDto>(page.Items, nextCursor, page.HasMore);
    }
}
