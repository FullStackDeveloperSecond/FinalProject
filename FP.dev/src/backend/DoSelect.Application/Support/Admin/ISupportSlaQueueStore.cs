using DoSelect.Application.Support.Admin.Dtos;

namespace DoSelect.Application.Support.Admin;

/// <summary>
/// Read-only persistence port for the admin support SLA queue. Implementations query
/// SupportTickets directly (not the vw_CaseWorkbench view) so due/usage/overdue can be computed
/// from a caller-supplied UTC instant instead of SQL server local time.
/// </summary>
public interface ISupportSlaQueueStore
{
    /// <summary>
    /// Returns up to <paramref name="pageSize"/> active (non-terminal) tickets ordered by
    /// IsOverdue DESC, EffectiveDueAtUtc ASC, TicketPublicId ASC, starting strictly after
    /// <paramref name="after"/> when supplied. <paramref name="nowUtc"/> drives every due/usage/
    /// overdue calculation.
    /// </summary>
    Task<SupportSlaQueuePage> QueryPageAsync(
        int pageSize,
        SupportSlaCursorPosition? after,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

public sealed record SupportSlaQueuePage(IReadOnlyList<SupportSlaItemDto> Items, bool HasMore);
