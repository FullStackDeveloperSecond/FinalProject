using DoSelect.Application.Support.Admin.Dtos;

namespace DoSelect.Application.Support.Admin;

/// <summary>
/// Admin-facing support ticket use cases. Unlike ISupportTicketService (Actor Scope: a
/// member's own tickets only), these operate across all tickets. The caller is responsible for
/// authenticating and authorizing the admin (CustomerService/CustomerServiceSupervisor policy)
/// before invoking this service; adminUserId here is trusted as already-verified.
/// </summary>
public interface IAdminSupportTicketService
{
    /// <summary>
    /// Claims an unassigned Open ticket for the calling admin. Throws DomainProblemException
    /// with ResourceNotFound (404), SupportTicketAssignmentConflict (409) when the ticket is no
    /// longer open/unassigned, or ConcurrencyConflict (409) when the RowVersion is merely stale.
    /// </summary>
    Task<AdminSupportTicketDto> ClaimAsync(
        string adminUserId,
        Guid ticketPublicId,
        ClaimSupportTicketRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the full admin-facing detail for one ticket, including internal notes. The acting
    /// admin identity and supervisor scope are required so assignment visibility is applied before
    /// sensitive data is loaded. Missing and out-of-scope tickets both map to the standard 404.
    /// </summary>
    Task<AdminSupportTicketDetailDto> GetDetailAsync(
        string adminUserId,
        bool canHandle,
        bool canSupervise,
        Guid ticketPublicId,
        CancellationToken cancellationToken);

    /// <summary>DES-23 SupportTicket.Supervise: assign an unassigned Open ticket to another admin.</summary>
    Task<AdminSupportTicketDto> AssignAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        AssignSupportTicketRequest request,
        CancellationToken cancellationToken);

    /// <summary>DES-23 SupportTicket.Supervise: move a ticket from its current assignee to another qualified admin.</summary>
    Task<AdminSupportTicketDto> TransferAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        TransferSupportTicketRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// SupportTicket.Handle (general adjustment, actor-scoped) or SupportTicket.Supervise
    /// (override, any ticket) — the single change-priority Action, dispatched by the caller's own
    /// entry-gate authorization (checked imperatively against both policies since the endpoint
    /// must admit CustomerService, CustomerServiceSupervisor, and bare SuperAdmin alike).
    /// </summary>
    Task<AdminSupportTicketDetailDto> ChangePriorityAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        ChangeSupportTicketPriorityRequest request,
        CancellationToken cancellationToken);

    /// <summary>SupportTicket.Handle: general status change via the existing Transition state machine.</summary>
    Task<AdminSupportTicketDetailDto> ChangeStatusAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        ChangeSupportTicketStatusRequest request,
        CancellationToken cancellationToken);

    /// <summary>SupportTicket.Handle: admin-initiated cancel (Open/Assigned, before a human reply).</summary>
    Task<AdminSupportTicketDetailDto> CancelAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        CancelSupportTicketByAdminRequest request,
        CancellationToken cancellationToken);

    /// <summary>SupportTicket.Handle: reopen a Resolved ticket back to InProgress.</summary>
    Task<AdminSupportTicketDetailDto> ReopenAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        ReopenSupportTicketRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// SupportTicket.Handle: append an internal note (POST .../internal-notes) — never surfaced
    /// to the member, never counted as the first human public response.
    /// </summary>
    Task<AdminSupportTicketDetailDto> AddInternalNoteAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        CreateInternalNoteRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// SupportTicket.Handle: append a member-visible admin reply, record the first human
    /// response once, and enqueue the existing support notification contracts atomically.
    /// </summary>
    Task<AdminSupportTicketDetailDto> AddPublicReplyAsync(
        SupportTicketActionContext context,
        Guid ticketPublicId,
        CreateAdminSupportReplyRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Everything about the caller that every DES-23 action needs, resolved once by the controller
/// from the authenticated principal and ambient HTTP context: identity, role snapshot (also used
/// as the AuditActor role snapshot), supervisor scope, and the observability fields the central
/// Audit contract requires. Keeping this as one parameter avoids a 6-argument signature repeated
/// across six action methods.
/// </summary>
public sealed record SupportTicketActionContext(
    string AdminUserId,
    IReadOnlyCollection<string> Roles,
    bool CanSupervise,
    string CorrelationId,
    string TraceId,
    System.Net.IPAddress? RemoteIpAddress);
