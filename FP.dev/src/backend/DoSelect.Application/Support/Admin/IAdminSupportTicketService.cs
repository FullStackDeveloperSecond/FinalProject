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
}
