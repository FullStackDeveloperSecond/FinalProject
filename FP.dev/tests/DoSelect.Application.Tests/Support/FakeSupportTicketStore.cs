using System.Reflection;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Tests.Support;

/// <summary>
/// Hand-rolled in-memory double for ISupportTicketStore — no DB, no mocking framework
/// dependency. Mirrors just enough of EF's behavior (identity assignment, optimistic
/// concurrency) for the Application-layer unit tests to exercise real business rules.
/// </summary>
internal sealed class FakeSupportTicketStore : ISupportTicketStore
{
    // Id has no setter at all (not even private) — only EF's field-based access mode can
    // populate it after construction. Mirror that here via the compiler-generated backing
    // field rather than a property setter, which does not exist to reflect into.
    private static readonly FieldInfo IdBackingField =
        typeof(SupportTicket).BaseType!.BaseType!.BaseType!
            .GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private long _nextId = 1;

    public List<SupportTicket> Tickets { get; } = [];

    public List<SupportMessage> Messages { get; } = [];

    public List<SupportStatusHistory> StatusHistories { get; } = [];

    public List<SupportSlaEvent> SlaEvents { get; } = [];

    /// <summary>When set, the next SaveTicketChangesAsync call throws a concurrency conflict.</summary>
    public bool SimulateConcurrencyConflictOnNextSave { get; set; }

    /// <summary>
    /// When greater than zero, the next N calls to CreateTicketWithArtifactsAsync throw
    /// SupportTicketNumberCollisionException instead of inserting — exercises the Service's
    /// retry loop wiring. This is a pure in-memory logic test, not proof of real DB atomicity
    /// (that requires the Infrastructure-level integration tests against SQL Server).
    /// </summary>
    public int SimulateTicketNumberCollisionsRemaining { get; set; }

    public Task<bool> TicketNumberExistsAsync(string ticketNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Tickets.Any(t => t.TicketNumber == ticketNumber));

    public Task<SupportTicketCreationResult> CreateTicketWithArtifactsAsync(
        SupportTicket ticket,
        Func<long, SupportMessage> firstMessageFactory,
        Func<long, SupportStatusHistory> statusHistoryFactory,
        Func<long, SupportSlaEvent> slaEventFactory,
        CancellationToken cancellationToken)
    {
        if (SimulateTicketNumberCollisionsRemaining > 0)
        {
            SimulateTicketNumberCollisionsRemaining--;
            throw new SupportTicketNumberCollisionException(
                ticket.TicketNumber,
                new InvalidOperationException("Simulated collision."));
        }

        IdBackingField.SetValue(ticket, _nextId++);
        Tickets.Add(ticket);

        var firstMessage = firstMessageFactory(ticket.Id);
        var statusHistory = statusHistoryFactory(ticket.Id);
        var slaEvent = slaEventFactory(ticket.Id);
        Messages.Add(firstMessage);
        StatusHistories.Add(statusHistory);
        SlaEvents.Add(slaEvent);

        return Task.FromResult(new SupportTicketCreationResult(ticket.Id, firstMessage, statusHistory, slaEvent));
    }

    public Task<SupportTicket?> FindOwnedTicketAsync(
        Guid ticketPublicId,
        string memberUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Tickets.SingleOrDefault(t => t.PublicId == ticketPublicId && t.MemberUserId == memberUserId));

    public Task<(IReadOnlyList<SupportTicket> Items, int TotalCount)> ListOwnedAsync(
        string memberUserId,
        SupportTicketQuery query,
        CancellationToken cancellationToken)
    {
        var filtered = Tickets.Where(t => t.MemberUserId == memberUserId);
        if (query.Statuses is { Count: > 0 } statuses)
        {
            filtered = filtered.Where(t => statuses.Contains(t.Status));
        }

        if (query.Category is { } category)
        {
            filtered = filtered.Where(t => t.Category == category);
        }

        var all = filtered.OrderByDescending(t => t.LastActivityAtUtc).ThenByDescending(t => t.PublicId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Task.FromResult(((IReadOnlyList<SupportTicket>)page, all.Count));
    }

    public Task<IReadOnlyList<SupportMessage>> ListPublicMessagesAsync(
        long ticketId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SupportMessage>>(
            Messages.Where(m => m.SupportTicketId == ticketId && !m.IsInternal)
                .OrderBy(m => m.SentAtUtc)
                .ToList());

    public Task<IReadOnlyList<SupportAttachmentDto>> ListCleanAttachmentsAsync(
        long ticketId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SupportAttachmentDto>>([]);

    public Task<Guid?> FindOrderPublicIdAsync(long orderId, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);

    public Task SaveTicketChangesAsync(
        SupportTicket ticket,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        if (SimulateConcurrencyConflictOnNextSave)
        {
            SimulateConcurrencyConflictOnNextSave = false;
            throw DoSelect.Application.Common.DomainProblemException.Conflict(
                DoSelect.Application.Common.DomainErrorCodes.ConcurrencyConflict,
                "Simulated concurrency conflict.");
        }

        return Task.CompletedTask;
    }

    public Task AddMessageAsync(SupportMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public Task AddStatusHistoryAsync(SupportStatusHistory history, CancellationToken cancellationToken)
    {
        StatusHistories.Add(history);
        return Task.CompletedTask;
    }
}

internal sealed class FakeOrderOwnershipLookup : IOrderOwnershipLookup
{
    public Dictionary<Guid, OrderOwnershipInfo> Orders { get; } = [];

    public Task<OrderOwnershipInfo?> FindByPublicIdAsync(Guid orderPublicId, CancellationToken cancellationToken) =>
        Task.FromResult(Orders.GetValueOrDefault(orderPublicId));
}
