using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin.Dtos;

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
