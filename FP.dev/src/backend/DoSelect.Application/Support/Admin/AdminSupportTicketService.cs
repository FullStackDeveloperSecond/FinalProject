using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin;

public sealed class AdminSupportTicketService : IAdminSupportTicketService
{
    private readonly IAdminSupportTicketStore _store;
    private readonly TimeProvider _timeProvider;

    public AdminSupportTicketService(IAdminSupportTicketStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<AdminSupportTicketDto> ClaimAsync(
        string adminUserId,
        Guid ticketPublicId,
        ClaimSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.ClaimAsync(
            ticketPublicId,
            adminUserId,
            request.RowVersion,
            nowUtc,
            cancellationToken);

        return result.Outcome switch
        {
            SupportTicketClaimOutcome.Claimed => ToDto(result.Ticket!),
            SupportTicketClaimOutcome.NotFound => throw DomainProblemException.NotFound(
                "The support ticket was not found."),
            // Covers both "someone else already claimed it" and "it is no longer Open"
            // (Closed/Cancelled/etc.) — the API contract defines a single conflict code for
            // every reason a ticket can no longer be claimed as-is.
            SupportTicketClaimOutcome.AssignmentConflict => throw DomainProblemException.Conflict(
                DomainErrorCodes.SupportTicketAssignmentConflict,
                "The ticket is no longer open and unassigned; another admin may have already claimed it."),
            SupportTicketClaimOutcome.ConcurrencyConflict => throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The ticket was modified by another request. Reload and try again."),
            SupportTicketClaimOutcome.AdminNotEligible => throw DomainProblemException.Forbidden(
                "The acting admin does not have an active support profile."),
            _ => throw new InvalidOperationException($"Unhandled claim outcome '{result.Outcome}'."),
        };
    }

    public async Task<AdminSupportTicketDetailDto> GetDetailAsync(
        string adminUserId,
        bool canSupervise,
        Guid ticketPublicId,
        CancellationToken cancellationToken)
    {
        var detail = await _store.GetDetailAsync(
            ticketPublicId,
            adminUserId,
            canSupervise,
            cancellationToken);
        if (detail is null)
        {
            throw DomainProblemException.NotFound("The support ticket was not found.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return ToDetailDto(detail, nowUtc);
    }

    /// <summary>
    /// Mirrors ISupportTicketService's overdue rule: overdue tracks whichever SLA target is
    /// still active (FirstResponse until a human has replied, then Resolution) and is always
    /// false once the ticket has left active work, regardless of how the due dates compare to
    /// now. Kept in lockstep with SupportTicketService.ComputeIsOverdue by design intent.
    /// </summary>
    private static bool ComputeIsOverdue(AdminSupportTicketDetail detail, DateTime nowUtc)
    {
        if (detail.Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
        {
            return false;
        }

        var activeDueAtUtc = detail.FirstHumanResponseAtUtc is null
            ? detail.FirstResponseDueAtUtc
            : detail.ResolutionDueAtUtc;
        return nowUtc > activeDueAtUtc;
    }

    /// <summary>
    /// Mirrors ClaimAsync's eligibility precondition exactly: claim is only ever accepted by the
    /// store when the ticket is Open and unassigned (every other state maps to
    /// AssignmentConflict). Kept in lockstep by design intent, the same way ComputeIsOverdue is.
    /// </summary>
    private static IReadOnlyList<string> ComputeAvailableActions(AdminSupportTicketDetail detail) =>
        detail.Status == SupportTicketStatus.Open && detail.AssigneeAdminPublicId is null
            ? ["claim"]
            : [];

    private static AdminSupportTicketDetailDto ToDetailDto(AdminSupportTicketDetail detail, DateTime nowUtc) => new(
        detail.PublicId,
        detail.TicketNumber,
        detail.Category,
        detail.Subject,
        detail.Status,
        detail.Priority,
        detail.OrderPublicId,
        detail.AssigneeAdminPublicId is null
            ? null
            : new AdminAssigneeSummaryDto(detail.AssigneeAdminPublicId.Value, detail.AssigneeAdminDisplayName!),
        detail.CreatedAtUtc,
        detail.LastActivityAtUtc,
        detail.FirstResponseDueAtUtc,
        detail.ResolutionDueAtUtc,
        ComputeIsOverdue(detail, nowUtc),
        detail.FirstHumanResponseAtUtc,
        detail.ResolvedAtUtc,
        detail.ClosedAtUtc,
        detail.ReopenCount,
        ComputeAvailableActions(detail),
        detail.RowVersion,
        [.. detail.Messages.Select(m => new AdminSupportMessageDto(
            m.PublicId,
            m.SenderType,
            m.AiGenerated,
            m.IsInternal,
            m.Body,
            m.Language,
            m.SentAtUtc))])
    {
        Attachments = detail.Attachments,
    };

    private static AdminSupportTicketDto ToDto(ClaimedSupportTicket ticket) => new(
        ticket.PublicId,
        ticket.TicketNumber,
        ticket.Category,
        ticket.Subject,
        ticket.Status,
        ticket.Priority,
        ticket.OrderPublicId,
        new AdminAssigneeSummaryDto(ticket.AssigneeAdminPublicId, ticket.AssigneeAdminDisplayName),
        ticket.CreatedAtUtc,
        ticket.LastActivityAtUtc,
        ticket.FirstResponseDueAtUtc,
        ticket.ResolutionDueAtUtc,
        ticket.FirstHumanResponseAtUtc,
        ticket.ResolvedAtUtc,
        ticket.ClosedAtUtc,
        ticket.ReopenCount,
        ticket.RowVersion);
}
