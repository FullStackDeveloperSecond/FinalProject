using DoSelect.Application.Common;
using DoSelect.Domain.Returns;

namespace DoSelect.Application.Returns;

public interface IAdminReturnService
{
    Task<PageResult<AdminReturnSummaryDto>> ListAsync(AdminReturnQuery query, CancellationToken cancellationToken);

    Task<AdminReturnDetailDto> GetDetailAsync(Guid returnPublicId, CancellationToken cancellationToken);

    Task<ReturnRequestDto> ReviewAsync(
        Guid returnPublicId, string adminUserId, ApproveReturnRequest request, CancellationToken cancellationToken);

    Task<ReturnRequestDto> ReceiveAsync(
        Guid returnPublicId, string adminUserId, ReceiveReturnRequest request, CancellationToken cancellationToken);

    Task<ReturnRequestDto> InspectAsync(
        Guid returnPublicId, string adminUserId, InspectReturnRequest request, CancellationToken cancellationToken);

    Task<ReturnRequestDto> ExtendShipmentDeadlineAsync(
        Guid returnPublicId, string adminUserId, ExtendShipmentDeadlineRequest request, CancellationToken cancellationToken);

    Task<ReturnShipmentDto> GetShipmentAsync(Guid returnPublicId, CancellationToken cancellationToken);

    Task<ReturnShipmentDto> CreateShipmentAsync(
        Guid returnPublicId, CreateReturnShipmentRequest request, CancellationToken cancellationToken);

    Task<ReturnShipmentDto> AppendShipmentEventAsync(
        Guid returnPublicId, AppendReturnShipmentEventRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Back-office return use cases. Every action is gated by the caller's own [Authorize(Policy =
/// DoSelectPolicies.ReturnApprove)] at the Controller — see the implementation report for why
/// query/process/review all reuse that one policy instead of new ones.
/// </summary>
public sealed class AdminReturnService : IAdminReturnService
{
    private static readonly string[] ConditionCodes =
        ["Unopened", "OpenedForInspection", "Installed", "Used", "Damaged", "MissingAccessories", "Activated"];

    private readonly IReturnStore _store;
    private readonly IReturnOrderEligibilityPort _orderPort;
    private readonly TimeProvider _timeProvider;

    public AdminReturnService(IReturnStore store, IReturnOrderEligibilityPort orderPort, TimeProvider timeProvider)
    {
        _store = store;
        _orderPort = orderPort;
        _timeProvider = timeProvider;
    }

    public async Task<PageResult<AdminReturnSummaryDto>> ListAsync(AdminReturnQuery query, CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(query.PageNumber, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var normalized = query with { PageNumber = pageNumber, PageSize = pageSize };

        var (items, totalCount) = await _store.ListForAdminAsync(normalized, cancellationToken);
        return new PageResult<AdminReturnSummaryDto>(items, pageNumber, pageSize, totalCount);
    }

    public async Task<AdminReturnDetailDto> GetDetailAsync(Guid returnPublicId, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(returnPublicId, cancellationToken);
        var order = await _orderPort.FindByIdAsync(request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException("A return's own order must always resolve.");

        var items = await _store.ListItemSummariesAsync(request.Id, cancellationToken);
        var attachments = await _store.ListCleanAttachmentSummariesAsync(request.Id, cancellationToken);
        var shipment = await _store.FindShipmentAsync(request.Id, cancellationToken);
        var events = shipment is null ? [] : await _store.ListShipmentEventsAsync(shipment.Id, cancellationToken);
        var inspections = await _store.ListInspectionsAsync(request.Id, cancellationToken);
        var history = await _store.ListHistoryAsync(request.Id, cancellationToken);

        var dto = ToDto(request, items, order.OrderPublicId, order.OrderNumber, attachments, shipment, events);
        var availableActions = ComputeAdminAvailableActions(request, shipment);

        return new AdminReturnDetailDto(
            dto with { AvailableActions = availableActions },
            [.. inspections.OrderBy(i => i.InspectedAtUtc)],
            [.. items
                .Where(i => i.RestockDisposition.HasValue)
                .Select(i => new RefundableItemPreviewDto(i.PublicId, i.SkuCodeSnapshot, i.Quantity, i.RestockDisposition))],
            [.. history.OrderBy(h => h.OccurredAtUtc)],
            availableActions);
    }

    public async Task<ReturnRequestDto> ReviewAsync(
        Guid returnPublicId, string adminUserId, ApproveReturnRequest request, CancellationToken cancellationToken)
    {
        var returnRequest = await LoadAsync(returnPublicId, cancellationToken);
        if (returnRequest.Status is not (ReturnRequestStatus.Requested or ReturnRequestStatus.UnderReview))
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                $"The return cannot be reviewed while it is {returnRequest.Status}.");
        }

        var items = await _store.ListItemsAsync(returnRequest.Id, cancellationToken);
        if (request.Approved)
        {
            ValidateFullQuantityApproval(items, request.Items);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var fromStatus = returnRequest.Status;
        if (returnRequest.Status == ReturnRequestStatus.Requested)
        {
            returnRequest.Transition(ReturnRequestStatus.UnderReview, nowUtc);
        }

        if (request.Approved)
        {
            // A return only needs a physical shipment back when at least one approved item
            // requires inspection — otherwise (e.g. low-value goodwill approvals) it goes
            // straight to AwaitingRefund. The documented ApproveReturnRequest DTO has no
            // separate "requiresShipment" field, so this derives it from the one it does have
            // (inspectionRequired); see the implementation report.
            var requiresShipment = request.Items.Any(i => i.InspectionRequired);
            returnRequest.Approve(adminUserId, requiresShipment, nowUtc);
        }
        else
        {
            returnRequest.Reject(adminUserId, nowUtc);
        }

        var history = new ReturnStatusHistory(
            returnRequest.Id, fromStatus, returnRequest.Status, request.ReasonCode, request.Note, adminUserId, nowUtc);

        await _store.SaveTransitionAsync(returnRequest, null, null, history, request.ReturnRowVersion, cancellationToken);

        return await GetDetailDtoAsync(returnRequest, cancellationToken);
    }

    public async Task<ReturnRequestDto> ReceiveAsync(
        Guid returnPublicId, string adminUserId, ReceiveReturnRequest request, CancellationToken cancellationToken)
    {
        var returnRequest = await LoadAsync(returnPublicId, cancellationToken);
        if (returnRequest.Status is not (ReturnRequestStatus.AwaitingShipment or ReturnRequestStatus.InTransit))
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                $"The return cannot be received while it is {returnRequest.Status}.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var fromStatus = returnRequest.Status;
        if (returnRequest.Status == ReturnRequestStatus.AwaitingShipment)
        {
            returnRequest.Transition(ReturnRequestStatus.InTransit, nowUtc);
        }

        returnRequest.Transition(ReturnRequestStatus.Received, nowUtc);

        var history = new ReturnStatusHistory(
            returnRequest.Id, fromStatus, returnRequest.Status, "manual-receive", request.Note, adminUserId, nowUtc);

        await _store.SaveTransitionAsync(returnRequest, null, null, history, request.ReturnRowVersion, cancellationToken);

        return await GetDetailDtoAsync(returnRequest, cancellationToken);
    }

    public async Task<ReturnRequestDto> InspectAsync(
        Guid returnPublicId, string adminUserId, InspectReturnRequest request, CancellationToken cancellationToken)
    {
        var returnRequest = await LoadAsync(returnPublicId, cancellationToken);
        if (returnRequest.Status is not (ReturnRequestStatus.Received or ReturnRequestStatus.Inspecting))
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                $"The return cannot be inspected while it is {returnRequest.Status}.");
        }

        var items = await _store.ListItemsAsync(returnRequest.Id, cancellationToken);
        ValidateExactItemSet(items, [.. request.Items.Select(l => l.ReturnItemPublicId)]);
        var itemsById = items.ToDictionary(i => i.PublicId);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var updatedItems = new List<ReturnItem>();
        var newInspections = new List<ReturnInspection>();
        foreach (var line in request.Items)
        {
            if (!ConditionCodes.Contains(line.ConditionCode, StringComparer.Ordinal))
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ValidationFailed,
                    $"Unknown condition code '{line.ConditionCode}'.");
            }

            var item = itemsById[line.ReturnItemPublicId];
            item.RecordInspectionSummary(line.Disposition.ToString(), line.Disposition);
            updatedItems.Add(item);
            newInspections.Add(new ReturnInspection(
                Guid.CreateVersion7(), item.Id, line.Disposition.ToString(), line.ConditionCode, line.Note, adminUserId, nowUtc));
        }

        var fromStatus = returnRequest.Status;
        if (returnRequest.Status == ReturnRequestStatus.Received)
        {
            returnRequest.Transition(ReturnRequestStatus.Inspecting, nowUtc);
        }

        returnRequest.Transition(ReturnRequestStatus.AwaitingRefund, nowUtc);

        var history = new ReturnStatusHistory(
            returnRequest.Id, fromStatus, returnRequest.Status, "inspection-complete", null, adminUserId, nowUtc);

        await _store.SaveTransitionAsync(returnRequest, updatedItems, newInspections, history, request.ReturnRowVersion, cancellationToken);

        return await GetDetailDtoAsync(returnRequest, cancellationToken);
    }

    public async Task<ReturnRequestDto> ExtendShipmentDeadlineAsync(
        Guid returnPublicId, string adminUserId, ExtendShipmentDeadlineRequest request, CancellationToken cancellationToken)
    {
        var returnRequest = await LoadAsync(returnPublicId, cancellationToken);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            returnRequest.ExtendShipmentDeadline(nowUtc);
        }
        catch (InvalidOperationException exception)
        {
            throw new ReturnsWriteException(ReturnsWriteException.ErrorCodes.ReturnShipmentExtensionNotAllowed, exception.Message);
        }

        var history = new ReturnStatusHistory(
            returnRequest.Id,
            ReturnRequestStatus.AwaitingShipment,
            ReturnRequestStatus.AwaitingShipment,
            request.ReasonCode,
            "Shipment deadline extended by 7 days.",
            adminUserId,
            nowUtc);

        await _store.SaveTransitionAsync(returnRequest, null, null, history, request.ReturnRowVersion, cancellationToken);

        return await GetDetailDtoAsync(returnRequest, cancellationToken);
    }

    public async Task<ReturnShipmentDto> GetShipmentAsync(Guid returnPublicId, CancellationToken cancellationToken)
    {
        var returnRequest = await LoadAsync(returnPublicId, cancellationToken);
        var shipment = await _store.FindShipmentAsync(returnRequest.Id, cancellationToken)
            ?? throw new ReturnsWriteException(ReturnsWriteException.ErrorCodes.ResourceNotFound, "No shipment exists for this return.");
        var events = await _store.ListShipmentEventsAsync(shipment.Id, cancellationToken);
        return ReturnDtoMapper.ToShipmentDto(shipment, events);
    }

    public async Task<ReturnShipmentDto> CreateShipmentAsync(
        Guid returnPublicId, CreateReturnShipmentRequest request, CancellationToken cancellationToken)
    {
        var returnRequest = await LoadAsync(returnPublicId, cancellationToken);
        if (returnRequest.Status != ReturnRequestStatus.AwaitingShipment)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                $"A shipment cannot be created while the return is {returnRequest.Status}.");
        }

        if (await _store.FindShipmentAsync(returnRequest.Id, cancellationToken) is not null)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                "This return already has an active shipment.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var shipmentNumber = $"RS-{nowUtc:yyyyMMdd}-{Guid.NewGuid():N}"[..20];
        ReturnShipment shipment;
        try
        {
            shipment = new ReturnShipment(
                Guid.CreateVersion7(), returnRequest.Id, shipmentNumber, request.Method,
                request.CarrierCode, trackingNumber: null,
                request.RecipientName, request.RecipientPhone, request.PostalCode, request.AddressLine,
                request.StoreCode, request.StoreName,
                nowUtc);
        }
        catch (ArgumentException exception)
        {
            throw new ReturnsWriteException(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.Message);
        }

        var created = await _store.CreateShipmentAsync(shipment, returnRequest.Id, request.ReturnRowVersion, cancellationToken);
        return ReturnDtoMapper.ToShipmentDto(created, []);
    }

    public async Task<ReturnShipmentDto> AppendShipmentEventAsync(
        Guid returnPublicId, AppendReturnShipmentEventRequest request, CancellationToken cancellationToken)
    {
        var returnRequest = await LoadAsync(returnPublicId, cancellationToken);
        var shipment = await _store.FindShipmentAsync(returnRequest.Id, cancellationToken)
            ?? throw new ReturnsWriteException(ReturnsWriteException.ErrorCodes.ResourceNotFound, "No shipment exists for this return.");

        if (await _store.ShipmentEventExistsAsync(request.Source, request.ExternalEventId, cancellationToken))
        {
            // Idempotent replay: the same (Source, ExternalEventId) was already applied.
            var existingEvents = await _store.ListShipmentEventsAsync(shipment.Id, cancellationToken);
            return ReturnDtoMapper.ToShipmentDto(shipment, existingEvents);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var newEvent = new ReturnShipmentEvent(
            shipment.Id, request.ExternalEventId, request.Source, request.EventType,
            eventCode: null, request.Description, request.OccurredAtUtc, nowUtc, payloadHash: null, payloadSummaryJson: null);

        // Append-only: every distinct (Source, ExternalEventId) is recorded regardless of
        // whether it can move state forward — a delayed/out-of-order carrier webhook must never
        // be rejected or lost, only prevented from moving the shipment/request backward.
        // shipmentToUpdate stays null (so AppendShipmentEventAsync changes no shipment/request
        // state at all) unless CanAdvanceTo says this specific event is actually newer than the
        // last one applied AND moves the main sequence strictly forward.
        ReturnShipment? shipmentToUpdate = null;
        ReturnRequest? requestToTransition = null;
        ReturnStatusHistory? requestHistory = null;
        if (Enum.TryParse<ReturnShipmentStatus>(request.EventType, ignoreCase: false, out var mappedStatus) &&
            shipment.CanAdvanceTo(mappedStatus, request.OccurredAtUtc))
        {
            // Business/audit timestamps reflect when the carrier event actually occurred, not
            // when this server happened to receive/process it (nowUtc is only ever used above,
            // for the event's own ReceivedAtUtc ingestion stamp).
            shipment.ApplyEventStatus(mappedStatus, request.OccurredAtUtc);
            shipmentToUpdate = shipment;

            if (mappedStatus is ReturnShipmentStatus.PickedUp or ReturnShipmentStatus.InTransit &&
                returnRequest.Status == ReturnRequestStatus.AwaitingShipment)
            {
                requestToTransition = returnRequest;
                requestToTransition.Transition(ReturnRequestStatus.InTransit, request.OccurredAtUtc);
                requestHistory = new ReturnStatusHistory(
                    returnRequest.Id, ReturnRequestStatus.AwaitingShipment, ReturnRequestStatus.InTransit,
                    "shipment-event", request.EventType, actorUserId: null, request.OccurredAtUtc);
            }
            else if (mappedStatus == ReturnShipmentStatus.Delivered && returnRequest.Status == ReturnRequestStatus.InTransit)
            {
                requestToTransition = returnRequest;
                requestToTransition.Transition(ReturnRequestStatus.Received, request.OccurredAtUtc);
                requestHistory = new ReturnStatusHistory(
                    returnRequest.Id, ReturnRequestStatus.InTransit, ReturnRequestStatus.Received,
                    "shipment-event", request.EventType, actorUserId: null, request.OccurredAtUtc);
            }
        }

        await _store.AppendShipmentEventAsync(newEvent, shipmentToUpdate, requestToTransition, requestHistory, cancellationToken);

        var events = await _store.ListShipmentEventsAsync(shipment.Id, cancellationToken);
        return ReturnDtoMapper.ToShipmentDto(shipment, events);
    }

    private async Task<ReturnRequest> LoadAsync(Guid returnPublicId, CancellationToken cancellationToken) =>
        await _store.FindByPublicIdAsync(returnPublicId, cancellationToken)
        ?? throw new ReturnsWriteException(ReturnsWriteException.ErrorCodes.ResourceNotFound, "The return request was not found.");

    private async Task<ReturnRequestDto> GetDetailDtoAsync(ReturnRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderPort.FindByIdAsync(request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException("A return's own order must always resolve.");
        var items = await _store.ListItemSummariesAsync(request.Id, cancellationToken);
        var attachments = await _store.ListCleanAttachmentSummariesAsync(request.Id, cancellationToken);
        var shipment = await _store.FindShipmentAsync(request.Id, cancellationToken);
        var events = shipment is null ? [] : await _store.ListShipmentEventsAsync(shipment.Id, cancellationToken);
        return ToDto(request, items, order.OrderPublicId, order.OrderNumber, attachments, shipment, events) with
        {
            AvailableActions = ComputeAdminAvailableActions(request, shipment),
        };
    }

    private static void ValidateFullQuantityApproval(IReadOnlyList<ReturnItem> items, IReadOnlyList<ApproveReturnItemLine> approvalLines)
    {
        ValidateExactItemSet(items, [.. approvalLines.Select(l => l.ReturnItemPublicId)]);

        var itemsById = items.ToDictionary(i => i.PublicId);
        foreach (var line in approvalLines)
        {
            // Partial-quantity approval is not implemented in M-12 — ReturnItem.Quantity has
            // no setter and the finalized schema carries no per-item approved-quantity
            // column. See the implementation report. Exact-set validation above already
            // guarantees line.ReturnItemPublicId resolves here.
            if (line.ApprovedQuantity != itemsById[line.ReturnItemPublicId].Quantity)
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ValidationFailed,
                    "Partial-quantity approval is not supported; approvedQuantity must equal the requested quantity.");
            }
        }
    }

    /// <summary>
    /// Shared by ReviewAsync's approval path and InspectAsync: the distinct submitted ReturnItem
    /// PublicId set must equal the return's full persisted item set exactly. Equal cardinality
    /// alone is not sufficient — it lets a duplicated ID silently stand in for an omitted
    /// different item while still passing a naive count check. Duplicate, missing, and foreign
    /// IDs are all validation_failed, and this must run before any status/inspection/history
    /// mutation is applied.
    /// </summary>
    private static void ValidateExactItemSet(IReadOnlyList<ReturnItem> items, IReadOnlyList<Guid> submittedItemPublicIds)
    {
        var distinctSubmitted = new HashSet<Guid>();
        foreach (var id in submittedItemPublicIds)
        {
            if (!distinctSubmitted.Add(id))
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ValidationFailed,
                    $"Return item '{id}' was submitted more than once.");
            }
        }

        var actualItemIds = items.Select(i => i.PublicId).ToHashSet();
        if (!distinctSubmitted.SetEquals(actualItemIds))
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                "The submission must cover every item on this return exactly once.");
        }
    }

    private static IReadOnlyList<string> ComputeAdminAvailableActions(ReturnRequest request, ReturnShipment? shipment)
    {
        var actions = new List<string>();
        switch (request.Status)
        {
            case ReturnRequestStatus.Requested:
            case ReturnRequestStatus.UnderReview:
                actions.Add("review");
                break;
            case ReturnRequestStatus.AwaitingShipment:
                actions.Add("receive");
                if (shipment is null)
                {
                    actions.Add("createShipment");
                }

                if (!request.HasShipmentDeadlineBeenExtended)
                {
                    actions.Add("extendShipmentDeadline");
                }

                break;
            case ReturnRequestStatus.InTransit:
                actions.Add("receive");
                break;
            case ReturnRequestStatus.Received:
            case ReturnRequestStatus.Inspecting:
                actions.Add("inspect");
                break;
        }

        return actions;
    }

    private static ReturnRequestDto ToDto(
        ReturnRequest request,
        IReadOnlyList<ReturnItemDto> items,
        Guid orderPublicId,
        string orderNumber,
        IReadOnlyList<ReturnAttachmentDto> attachments,
        ReturnShipment? shipment,
        IReadOnlyList<ReturnShipmentEvent> shipmentEvents) =>
        new(
            request.PublicId,
            request.ReturnNumber,
            orderPublicId,
            orderNumber,
            request.Status,
            request.Priority,
            request.ReasonCode,
            request.Description,
            items,
            attachments,
            request.RequestedAtUtc,
            request.ApprovedAtUtc,
            request.ReceivedAtUtc,
            request.ClosedAtUtc,
            request.ReturnShipmentDueAtUtc,
            request.HasShipmentDeadlineBeenExtended,
            shipment is null ? null : ReturnDtoMapper.ToShipmentDto(shipment, shipmentEvents),
            [],
            request.RowVersion);
}
