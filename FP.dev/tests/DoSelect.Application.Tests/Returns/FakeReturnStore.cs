using System.Reflection;
using DoSelect.Application.Returns;
using DoSelect.Domain.Returns;

namespace DoSelect.Application.Tests.Returns;

/// <summary>
/// Hand-rolled in-memory double for IReturnStore — mirrors FakeSupportTicketStore's shape
/// (identity assignment via the compiler-generated backing field, optimistic concurrency via a
/// simulate-next-conflict flag). No DB, no mocking framework.
/// </summary>
internal sealed class FakeReturnStore : IReturnStore
{
    private static readonly FieldInfo ReturnRequestIdField =
        typeof(ReturnRequest).BaseType!.BaseType!.BaseType!
            .GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
    // Id is declared on the grandparent Entity, not on ReturnItem's immediate base
    // PublicEntity — GetField without FlattenHierarchy only sees fields declared on the type
    // itself, so this must walk one hop further than PublicEntity to find it.
    private static readonly FieldInfo PublicEntityIdField =
        typeof(ReturnItem).BaseType!.BaseType!.GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private long _nextId = 1;

    public List<ReturnRequest> Requests { get; } = [];
    public List<ReturnItem> Items { get; } = [];
    public List<ReturnAttachment> Attachments { get; } = [];
    public List<ReturnStatusHistory> Histories { get; } = [];
    public List<ReturnInspection> Inspections { get; } = [];
    public List<ReturnShipment> Shipments { get; } = [];
    public List<ReturnShipmentEvent> ShipmentEvents { get; } = [];

    public int SimulateCollisionsRemaining { get; set; }
    public bool SimulateConcurrencyConflictOnNextSave { get; set; }

    /// <summary>Simulates the store's lock-protected re-check losing a race to a concurrent
    /// create — set to make the next CreateWithItemsAsync call behave as if another request
    /// already consumed the budget, regardless of what this call's own budgets say.</summary>
    public bool SimulateQuantityConflictOnNextCreate { get; set; }

    public Task<bool> ReturnNumberExistsAsync(string returnNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.Any(r => r.ReturnNumber == returnNumber));

    public Task<ReturnCreationResult> CreateWithItemsAsync(
        ReturnRequest request,
        IReadOnlyList<ReturnItemQuantityBudget> quantityBudgets,
        Func<long, IReadOnlyList<ReturnItem>> itemsFactory,
        CancellationToken cancellationToken)
    {
        if (SimulateCollisionsRemaining > 0)
        {
            SimulateCollisionsRemaining--;
            throw new ReturnNumberCollisionException(request.ReturnNumber, new InvalidOperationException("Simulated collision."));
        }

        if (SimulateQuantityConflictOnNextCreate)
        {
            SimulateQuantityConflictOnNextCreate = false;
            throw new ReturnQuantityConflictException(quantityBudgets[0].OrderItemId);
        }

        ReturnRequestIdField.SetValue(request, _nextId++);
        Requests.Add(request);
        var items = itemsFactory(request.Id);
        foreach (var item in items)
        {
            PublicEntityIdField.SetValue(item, _nextId++);
        }

        Items.AddRange(items);
        return Task.FromResult(new ReturnCreationResult(request.Id, request, items));
    }

    public Task<ReturnRequest?> FindOwnedAsync(
        Guid returnPublicId, string? memberUserId, long? guestOrderId, CancellationToken cancellationToken)
    {
        var match = Requests.SingleOrDefault(r => r.PublicId == returnPublicId &&
            (memberUserId is not null ? r.RequesterUserId == memberUserId : guestOrderId.HasValue && r.OrderId == guestOrderId));
        return Task.FromResult(match);
    }

    public Task<ReturnRequest?> FindByPublicIdAsync(Guid returnPublicId, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.SingleOrDefault(r => r.PublicId == returnPublicId));

    public Task<IReadOnlyList<ReturnItem>> ListItemsAsync(long returnRequestId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReturnItem>>([.. Items.Where(i => i.ReturnRequestId == returnRequestId)]);

    public Task<IReadOnlyList<ReturnItemDto>> ListItemSummariesAsync(long returnRequestId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReturnItemDto>>(
        [
            .. Items.Where(i => i.ReturnRequestId == returnRequestId)
                .Select(i => new ReturnItemDto(i.PublicId, Guid.NewGuid(), "SKU", "Product", i.Description, i.Quantity, i.InspectionStatus, i.RestockDisposition)),
        ]);

    public Task<IReadOnlyList<ReturnAttachmentDto>> ListCleanAttachmentSummariesAsync(long returnRequestId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReturnAttachmentDto>>(
        [
            .. Attachments.Where(a => a.ReturnRequestId == returnRequestId && a.DeletedAtUtc is null)
                .Select(a => new ReturnAttachmentDto(a.PublicId, a.OriginalFileName, a.CreatedAtUtc)),
        ]);

    public Task<int> CountActiveAttachmentsAsync(long returnRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(Attachments.Count(a => a.ReturnRequestId == returnRequestId && a.DeletedAtUtc is null));

    public Task AddAttachmentAsync(ReturnAttachment attachment, CancellationToken cancellationToken)
    {
        Attachments.Add(attachment);
        return Task.CompletedTask;
    }

    public Task<ReturnAttachmentAccess?> FindAttachmentAccessAsync(Guid attachmentPublicId, CancellationToken cancellationToken)
    {
        var attachment = Attachments.SingleOrDefault(a => a.PublicId == attachmentPublicId && a.DeletedAtUtc is null);
        if (attachment is null)
        {
            return Task.FromResult<ReturnAttachmentAccess?>(null);
        }

        var request = Requests.Single(r => r.Id == attachment.ReturnRequestId);
        return Task.FromResult<ReturnAttachmentAccess?>(new ReturnAttachmentAccess(
            request.Id, request.RequesterUserId, request.OrderId, attachment.StorageKey, attachment.OriginalFileName, attachment.MimeType));
    }

    public Task<int> SumActiveRequestedQuantityAsync(long orderItemId, CancellationToken cancellationToken) =>
        Task.FromResult(Items
            .Where(i => i.OrderItemId == orderItemId)
            .Join(Requests.Where(r => r.Status is not (ReturnRequestStatus.Rejected or ReturnRequestStatus.Cancelled)),
                i => i.ReturnRequestId, r => r.Id, (i, r) => i.Quantity)
            .Sum());

    public Task<(IReadOnlyList<AdminReturnSummaryDto> Items, int TotalCount)> ListForAdminAsync(
        AdminReturnQuery query, CancellationToken cancellationToken)
    {
        var filtered = Requests.AsEnumerable();
        if (query.Statuses is { Count: > 0 } statuses)
        {
            filtered = filtered.Where(r => statuses.Contains(r.Status));
        }

        var all = filtered.ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(r => new AdminReturnSummaryDto(
                r.PublicId, r.ReturnNumber, Guid.NewGuid(), "ORD-1", r.Status, r.Priority,
                Items.Count(i => i.ReturnRequestId == r.Id), r.RequestedAtUtc, r.ReturnShipmentDueAtUtc, false, r.RowVersion))
            .ToList();
        return Task.FromResult(((IReadOnlyList<AdminReturnSummaryDto>)page, all.Count));
    }

    public Task<IReadOnlyList<ReturnHistoryEntryDto>> ListHistoryAsync(long returnRequestId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReturnHistoryEntryDto>>(
        [
            .. Histories.Where(h => h.ReturnRequestId == returnRequestId)
                .Select(h => new ReturnHistoryEntryDto(h.FromStatus, h.ToStatus, h.ReasonCode, h.Note, h.OccurredAtUtc)),
        ]);

    public Task<IReadOnlyList<ReturnInspectionDto>> ListInspectionsAsync(long returnRequestId, CancellationToken cancellationToken)
    {
        var itemIds = Items.Where(i => i.ReturnRequestId == returnRequestId).Select(i => i.Id).ToHashSet();
        return Task.FromResult<IReadOnlyList<ReturnInspectionDto>>(
        [
            .. Inspections.Where(insp => itemIds.Contains(insp.ReturnItemId))
                .Select(insp => new ReturnInspectionDto(
                    Items.Single(i => i.Id == insp.ReturnItemId).PublicId, insp.Result, insp.ConditionCode, insp.Note, insp.InspectedAtUtc)),
        ]);
    }

    public Task<ReturnShipment?> FindShipmentAsync(long returnRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(Shipments.SingleOrDefault(s => s.ReturnRequestId == returnRequestId));

    public Task<IReadOnlyList<ReturnShipmentEvent>> ListShipmentEventsAsync(long returnShipmentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReturnShipmentEvent>>([.. ShipmentEvents.Where(e => e.ReturnShipmentId == returnShipmentId)]);

    public Task<bool> ShipmentEventExistsAsync(string source, string externalEventId, CancellationToken cancellationToken) =>
        Task.FromResult(ShipmentEvents.Any(e => e.Source == source && e.ExternalEventId == externalEventId));

    public Task SaveTransitionAsync(
        ReturnRequest request, IReadOnlyList<ReturnItem>? itemsToUpdate, IReadOnlyList<ReturnInspection>? inspectionsToAdd,
        ReturnStatusHistory? historyToAdd, byte[] expectedRowVersion, CancellationToken cancellationToken)
    {
        if (SimulateConcurrencyConflictOnNextSave)
        {
            SimulateConcurrencyConflictOnNextSave = false;
            throw new ReturnsWriteException(ReturnsWriteException.ErrorCodes.ConcurrencyConflict, "Simulated concurrency conflict.");
        }

        if (inspectionsToAdd is { Count: > 0 })
        {
            Inspections.AddRange(inspectionsToAdd);
        }

        if (historyToAdd is not null)
        {
            Histories.Add(historyToAdd);
        }

        return Task.CompletedTask;
    }

    public Task<ReturnShipment> CreateShipmentAsync(
        ReturnShipment shipment, long returnRequestId, byte[] expectedReturnRowVersion, CancellationToken cancellationToken)
    {
        Shipments.Add(shipment);
        return Task.FromResult(shipment);
    }

    public Task AppendShipmentEventAsync(
        ReturnShipmentEvent shipmentEvent, ReturnShipment shipment, ReturnRequest? requestToTransition,
        ReturnStatusHistory? requestHistory, CancellationToken cancellationToken)
    {
        ShipmentEvents.Add(shipmentEvent);
        if (requestHistory is not null)
        {
            Histories.Add(requestHistory);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> CancelOverdueAwaitingShipmentAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cancelled = new List<Guid>();
        foreach (var request in Requests.Where(r =>
            r.Status == ReturnRequestStatus.AwaitingShipment && r.ReturnShipmentDueAtUtc is { } due && due <= nowUtc))
        {
            var fromStatus = request.Status;
            request.Transition(ReturnRequestStatus.Cancelled, nowUtc);
            Histories.Add(new ReturnStatusHistory(
                request.Id, fromStatus, ReturnRequestStatus.Cancelled,
                "shipment-deadline-expired", "Automatically cancelled.", null, nowUtc));
            cancelled.Add(request.PublicId);
        }

        return Task.FromResult<IReadOnlyList<Guid>>(cancelled);
    }
}

internal sealed class FakeReturnOrderEligibilityPort : IReturnOrderEligibilityPort
{
    public Dictionary<Guid, OrderEligibilitySnapshot> OrdersByPublicId { get; } = [];
    public Dictionary<long, OrderEligibilitySnapshot> OrdersById { get; } = [];

    public void Register(OrderEligibilitySnapshot order)
    {
        OrdersByPublicId[order.OrderPublicId] = order;
        OrdersById[order.OrderId] = order;
    }

    public Task<OrderEligibilitySnapshot?> FindByPublicIdAsync(Guid orderPublicId, CancellationToken cancellationToken) =>
        Task.FromResult(OrdersByPublicId.GetValueOrDefault(orderPublicId));

    public Task<OrderEligibilitySnapshot?> FindByIdAsync(long orderId, CancellationToken cancellationToken) =>
        Task.FromResult(OrdersById.GetValueOrDefault(orderId));
}

internal sealed class FakeGuestOrderAccessValidator : IGuestOrderAccessValidator
{
    public long? ValidOrderId { get; set; }

    public Task<long?> ValidateAsync(string rawToken, long requestedOrderId, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(ValidOrderId == requestedOrderId ? ValidOrderId : null);

    public Task<long?> ResolveOrderIdAsync(string rawToken, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(ValidOrderId);
}
