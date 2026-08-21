using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin.Dtos;

public sealed record ClaimSupportTicketRequest
{
    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

/// <summary>
/// Public-safe reference to the assigned admin: PublicId and DisplayName from AdminProfile, never
/// the internal Identity string Id or the admin's Email.
/// </summary>
public sealed record AdminAssigneeSummaryDto(Guid PublicId, string DisplayName);

public sealed record AdminSupportTicketDto(
    Guid PublicId,
    string TicketNumber,
    SupportTicketCategory Category,
    string Subject,
    SupportTicketStatus Status,
    CasePriority Priority,
    Guid? OrderPublicId,
    AdminAssigneeSummaryDto Assignee,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime FirstResponseDueAtUtc,
    DateTime ResolutionDueAtUtc,
    DateTime? FirstHumanResponseAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    int ReopenCount,
    byte[] RowVersion);

/// <summary>
/// A single message or internal note as returned to an authorized admin (SupportTicket.Handle).
/// IsInternal is always present so callers can distinguish internal notes from public replies;
/// never includes SenderUserId, the internal bigint Id, or a storage key.
/// </summary>
public sealed record AdminSupportMessageDto(
    Guid PublicId,
    SupportSenderType SenderType,
    bool AiGenerated,
    bool IsInternal,
    string Body,
    string Language,
    DateTime SentAtUtc);

/// <summary>
/// Full admin-facing ticket detail, including internal notes. Kept separate from
/// AdminSupportTicketDto (whose Assignee is non-null, matching the claim-result invariant)
/// because a read can encounter tickets with no assignee at all.
/// AvailableActions is a public-safe, usability-only hint of which actions the caller may
/// currently attempt: exactly ["claim"] when the ticket is Open and unassigned, otherwise
/// empty. The server's SupportTicketHandle policy and AdminSupportTicketService.ClaimAsync's
/// store-level conditional check remain the sole authorization source; this list never grants
/// an action the server would otherwise reject.
/// </summary>
public sealed record AdminSupportTicketDetailDto(
    Guid PublicId,
    string TicketNumber,
    SupportTicketCategory Category,
    string Subject,
    SupportTicketStatus Status,
    CasePriority Priority,
    Guid? OrderPublicId,
    AdminAssigneeSummaryDto? Assignee,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime FirstResponseDueAtUtc,
    DateTime ResolutionDueAtUtc,
    bool IsOverdue,
    DateTime? FirstHumanResponseAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    int ReopenCount,
    IReadOnlyList<string> AvailableActions,
    byte[] RowVersion,
    IReadOnlyList<AdminSupportMessageDto> Messages);
