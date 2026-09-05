using DoSelect.Application.Common;
using DoSelect.Application.Auditing;
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
        bool canHandle,
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
        return ToDetailDto(detail, canHandle, canSupervise, nowUtc);
    }

    public async Task<AdminSupportTicketDto> AssignAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        AssignSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.AssignAsync(
            new SupportTicketAssignCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.TargetAdminPublicId,
                request.Reason),
            cancellationToken);
        return ToDto(result, "assign");
    }

    public async Task<AdminSupportTicketDto> TransferAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        TransferSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.TransferAsync(
            new SupportTicketAssignCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.TargetAdminPublicId,
                request.Reason),
            cancellationToken);
        return ToDto(result, "transfer");
    }

    public async Task<AdminSupportTicketDetailDto> ChangePriorityAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        ChangeSupportTicketPriorityRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.ChangePriorityAsync(
            new SupportTicketChangePriorityCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.Priority,
                request.Reason),
            cancellationToken);
        return ToDetailDto(result, DomainErrorCodes.SupportTicketStateConflict, CanHandle(context), context.CanSupervise, nowUtc);
    }

    public async Task<AdminSupportTicketDetailDto> ChangeStatusAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        ChangeSupportTicketStatusRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.ChangeStatusAsync(
            new SupportTicketChangeStatusCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.Status,
                request.Reason),
            cancellationToken);
        return ToDetailDto(result, DomainErrorCodes.SupportTicketStateConflict, CanHandle(context), context.CanSupervise, nowUtc);
    }

    public async Task<AdminSupportTicketDetailDto> CancelAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        CancelSupportTicketByAdminRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.CancelAsync(
            new SupportTicketReasonCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.Reason),
            cancellationToken);
        return ToDetailDto(result, DomainErrorCodes.SupportTicketCancelNotAllowed, CanHandle(context), context.CanSupervise, nowUtc);
    }

    public async Task<AdminSupportTicketDetailDto> ReopenAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        ReopenSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.ReopenAsync(
            new SupportTicketReasonCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.Reason),
            cancellationToken);
        return ToDetailDto(result, DomainErrorCodes.SupportTicketStateConflict, CanHandle(context), context.CanSupervise, nowUtc);
    }

    public async Task<AdminSupportTicketDetailDto> AddInternalNoteAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        CreateInternalNoteRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.AddInternalNoteAsync(
            new SupportTicketAddInternalNoteCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.Body),
            cancellationToken);
        return ToDetailDto(result, DomainErrorCodes.SupportTicketStateConflict, CanHandle(context), context.CanSupervise, nowUtc);
    }

    public async Task<AdminSupportTicketDetailDto> AddPublicReplyAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        CreateAdminSupportReplyRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _store.AddPublicReplyAsync(
            new SupportTicketAddPublicReplyCommand(
                ticketPublicId,
                context.AdminUserId,
                context.Roles,
                context.CanSupervise,
                request.RowVersion,
                nowUtc,
                context.CorrelationId,
                context.TraceId,
                context.RemoteIpAddress,
                request.Body),
            cancellationToken);
        return ToDetailDto(result, DomainErrorCodes.SupportTicketStateConflict, CanHandle(context), context.CanSupervise, nowUtc);
    }

    private static AdminSupportTicketDto ToDto(SupportTicketAssignResult result, string actionName) =>
        result.Outcome switch
        {
            SupportTicketAssignOutcome.Success => ToDto(result.Ticket!),
            SupportTicketAssignOutcome.NotFound => throw DomainProblemException.NotFound(
                "The support ticket was not found."),
            SupportTicketAssignOutcome.TargetNotEligible => throw DomainProblemException.Validation(
                "The target admin must be an active, customer-service-qualified administrator."),
            SupportTicketAssignOutcome.AssignmentConflict => throw DomainProblemException.Conflict(
                DomainErrorCodes.SupportTicketAssignmentConflict,
                $"The ticket is no longer eligible to {actionName}; another admin may have already acted on it."),
            SupportTicketAssignOutcome.ConcurrencyConflict => throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The ticket was modified by another request. Reload and try again."),
            SupportTicketAssignOutcome.AdminNotEligible => throw DomainProblemException.Forbidden(
                "The acting admin does not have an active support profile."),
            _ => throw new InvalidOperationException($"Unhandled assign outcome '{result.Outcome}'."),
        };

    private static AdminSupportTicketDetailDto ToDetailDto(
        SupportTicketMutationResult result,
        string stateConflictCode,
        bool canHandle,
        bool canSupervise,
        DateTime nowUtc) =>
        result.Outcome switch
        {
            SupportTicketMutationOutcome.Success => ToDetailDto(result.Ticket!, canHandle, canSupervise, nowUtc),
            SupportTicketMutationOutcome.NotFound => throw DomainProblemException.NotFound(
                "The support ticket was not found."),
            SupportTicketMutationOutcome.StateConflict => throw DomainProblemException.Conflict(
                stateConflictCode,
                "The ticket's current status does not allow this operation."),
            SupportTicketMutationOutcome.ConcurrencyConflict => throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The ticket was modified by another request. Reload and try again."),
            SupportTicketMutationOutcome.AdminNotEligible => throw DomainProblemException.Forbidden(
                "The acting admin does not have an active support profile."),
            _ => throw new InvalidOperationException($"Unhandled mutation outcome '{result.Outcome}'."),
        };

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
    /// Mirrors each store's eligibility precondition exactly (claim/assign/transfer/cancel/reopen
    /// are all kept in lockstep by design intent, the same way ComputeIsOverdue is) — a public-
    /// safe, usability-only hint of what this caller may currently attempt. The server's Policy
    /// checks and each store's own conditional/tracked mutation remain the sole authorization
    /// source; this list never grants an action the server would otherwise reject.
    ///
    /// canSupervise alone gates assign/transfer/priority-override visibility because GetDetailAsync
    /// itself already enforced actor scope before this projection was ever built: a Handle-only
    /// caller only ever sees a ticket that is unassigned or assigned to them, so change-priority/
    /// change-status/cancel/reopen need no further per-caller check here — only the ticket's own
    /// state matters for those four once visibility is established.
    /// </summary>
    private static IReadOnlyList<string> ComputeAvailableActions(
        AdminSupportTicketDetail detail,
        bool canHandle,
        bool canSupervise,
        DateTime nowUtc)
    {
        var actions = new List<string>();
        var isTerminal = detail.Status is SupportTicketStatus.Closed or SupportTicketStatus.Cancelled;
        var isUnassignedOpen = detail.Status == SupportTicketStatus.Open && detail.AssigneeAdminPublicId is null;

        if (canHandle && isUnassignedOpen)
        {
            actions.Add("claim");
        }

        if (canSupervise)
        {
            if (isUnassignedOpen)
            {
                actions.Add("assign");
            }

            if (detail.AssigneeAdminPublicId is not null && !isTerminal)
            {
                actions.Add("transfer");
            }
        }

        if ((canHandle || canSupervise) && !isTerminal)
        {
            actions.Add("change-priority");
        }

        if (canHandle && !isTerminal)
        {
            actions.Add("change-status");
            actions.Add("internal-note");
        }

        if (canHandle &&
            detail.AssigneeAdminPublicId is not null &&
            detail.Status is (SupportTicketStatus.Assigned or
                SupportTicketStatus.InProgress or
                SupportTicketStatus.WaitingForCustomer or
                SupportTicketStatus.WaitingForInternal))
        {
            actions.Add("reply");
        }

        if (canHandle &&
            detail.Status is SupportTicketStatus.Open or SupportTicketStatus.Assigned &&
            detail.FirstHumanResponseAtUtc is null)
        {
            actions.Add("cancel");
        }

        // Mirrors SupportTicket.Reopen's own gate exactly (Resolved + within 3 days of
        // ResolvedAtUtc) so this hint never offers a reopen the store would just 409 on.
        if (canHandle &&
            detail.Status == SupportTicketStatus.Resolved &&
            detail.ResolvedAtUtc is { } resolvedAtUtc &&
            nowUtc <= resolvedAtUtc.AddDays(3))
        {
            actions.Add("reopen");
        }

        return actions;
    }

    private static bool CanHandle(SupportTicketActionContext context) =>
        context.Roles.Contains(AuditRoleNames.CustomerService, StringComparer.Ordinal) ||
        context.Roles.Contains(AuditRoleNames.CustomerServiceSupervisor, StringComparer.Ordinal);

    private static AdminSupportTicketDetailDto ToDetailDto(
        AdminSupportTicketDetail detail,
        bool canHandle,
        bool canSupervise,
        DateTime nowUtc) => new(
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
        ComputeAvailableActions(detail, canHandle, canSupervise, nowUtc),
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
