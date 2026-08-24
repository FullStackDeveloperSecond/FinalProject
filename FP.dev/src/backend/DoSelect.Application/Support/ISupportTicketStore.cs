using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support;

/// <summary>
/// Persistence port for the Support ticket use cases. Infrastructure provides the
/// EF Core-backed implementation; Application never references DbContext/DbSet types
/// directly, mirroring the existing IEmailSender port pattern.
/// </summary>
public interface ISupportTicketStore
{
    Task<bool> TicketNumberExistsAsync(string ticketNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a newly created ticket together with its first message, initial status
    /// history row, and initial SLA event as a single atomic unit — either all four rows
    /// exist afterward or none do. The artifact factories are invoked only after the ticket's
    /// generated internal Id is known (they need it as a foreign key) but before the
    /// transaction commits. Throws SupportTicketNumberCollisionException (never a raw
    /// DbUpdateException) if the insert violates the TicketNumber unique constraint, so the
    /// caller can retry with a fresh candidate.
    /// </summary>
    Task<SupportTicketCreationResult> CreateTicketWithArtifactsAsync(
        SupportTicket ticket,
        Func<long, SupportMessage> firstMessageFactory,
        Func<long, SupportStatusHistory> statusHistoryFactory,
        Func<long, SupportSlaEvent> slaEventFactory,
        CancellationToken cancellationToken);

    Task<SupportTicket?> FindOwnedTicketAsync(
        Guid ticketPublicId,
        string memberUserId,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<SupportTicket> Items, int TotalCount)> ListOwnedAsync(
        string memberUserId,
        SupportTicketQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SupportMessage>> ListPublicMessagesAsync(
        long ticketId,
        CancellationToken cancellationToken);

    Task<Guid?> FindOrderPublicIdAsync(long orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a mutation already applied to <paramref name="ticket"/> (and any newly
    /// added related rows already staged by the caller), enforcing that the row still
    /// matches <paramref name="expectedRowVersion"/>. Throws DomainProblemException with
    /// the ConcurrencyConflict code when it does not.
    /// </summary>
    Task SaveTicketChangesAsync(
        SupportTicket ticket,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken);

    Task AddMessageAsync(SupportMessage message, CancellationToken cancellationToken);

    Task AddStatusHistoryAsync(SupportStatusHistory history, CancellationToken cancellationToken);
}

public sealed record SupportTicketCreationResult(
    long TicketId,
    SupportMessage FirstMessage,
    SupportStatusHistory StatusHistory,
    SupportSlaEvent SlaEvent);

public sealed record OrderOwnershipInfo(long OrderId, Guid PublicId, string? MemberUserId);

/// <summary>Small read-only cross-module port onto haru's Order aggregate (ownership checks only).</summary>
public interface IOrderOwnershipLookup
{
    Task<OrderOwnershipInfo?> FindByPublicIdAsync(Guid orderPublicId, CancellationToken cancellationToken);
}
