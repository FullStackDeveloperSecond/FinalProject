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
