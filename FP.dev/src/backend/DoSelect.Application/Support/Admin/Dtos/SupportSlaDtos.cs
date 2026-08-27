using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin.Dtos;

/// <summary>Request shape for the admin SLA queue (UC-SLA-01). No filters beyond pagination are defined yet.</summary>
public sealed record SupportSlaQueueQuery(int PageSize, string? Cursor);

/// <summary>
/// One row of the admin SLA queue. EffectiveDueAtUtc/UsageRatio/IsOverdue are computed against a
/// caller-supplied UTC instant (see ISupportSlaQueueService), never SQL server local time.
/// Assignee is the public-safe AdminProfile summary (PublicId/DisplayName only) and is null when
/// the ticket is unassigned or its assignee no longer has an active profile.
/// </summary>
public sealed record SupportSlaItemDto(
    Guid TicketPublicId,
    string TicketNumber,
    CasePriority Priority,
    AdminAssigneeSummaryDto? Assignee,
    SupportTicketStatus Status,
    DateTime FirstResponseDueAtUtc,
    DateTime ResolutionDueAtUtc,
    DateTime EffectiveDueAtUtc,
    double UsageRatio,
    bool IsOverdue,
    DateTime LastActivityAtUtc,
    byte[] RowVersion);

/// <summary>
/// The keyset position of the last item on a page: the same (IsOverdue, EffectiveDueAtUtc,
/// TicketPublicId) triple the queue orders by. Carried inside the opaque cursor, never exposed
/// directly to callers.
/// </summary>
public sealed record SupportSlaCursorPosition(bool IsOverdue, DateTime EffectiveDueAtUtc, Guid TicketPublicId);
