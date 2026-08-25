using DoSelect.Application.Common;
using DoSelect.Application.Files;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Returns;

public interface IReturnService
{
    Task<ReturnRequestDto> CreateAsync(
        ReturnActor actor,
        Guid orderPublicId,
        CreateReturnRequest request,
        CancellationToken cancellationToken);

    Task<ReturnRequestDto> GetDetailAsync(
        ReturnActor actor,
        Guid returnPublicId,
        CancellationToken cancellationToken);

    Task<ReturnAttachmentDto> UploadAttachmentAsync(
        ReturnActor actor,
        Guid returnPublicId,
        PrivateFileUpload upload,
        CancellationToken cancellationToken);
}

/// <summary>
/// Customer/guest-facing return use cases. Every method enforces Actor Scope at the query
/// entry (FindOwnedAsync), never by loading another owner's row and filtering afterward.
/// </summary>
public sealed class ReturnService : IReturnService
{
    private const int MaximumAttachments = 3;

    private readonly IReturnStore _store;
    private readonly IReturnOrderEligibilityPort _orderPort;
    private readonly IPrivateFileStorage _fileStorage;
    private readonly TimeProvider _timeProvider;

    public ReturnService(
        IReturnStore store,
        IReturnOrderEligibilityPort orderPort,
        IPrivateFileStorage fileStorage,
        TimeProvider timeProvider)
    {
        _store = store;
        _orderPort = orderPort;
        _fileStorage = fileStorage;
        _timeProvider = timeProvider;
    }

    public async Task<ReturnRequestDto> CreateAsync(
        ReturnActor actor,
        Guid orderPublicId,
        CreateReturnRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _orderPort.FindByPublicIdAsync(orderPublicId, cancellationToken);
        if (order is null || !ActorOwnsOrder(actor, order))
        {
            // Same 404 whether the order does not exist, belongs to someone else, or the
            // guest scope does not cover it — never reveal that another order exists.
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ResourceNotFound,
                "The referenced order was not found.");
        }

        if (!request.OrderRowVersion.AsSpan().SequenceEqual(order.RowVersion))
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
                "The order has changed since it was loaded. Reload and try again.");
        }

        ValidateShape(request);

        var reasonType = ParseSharedReasonType(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var resolvedLines = new List<(EligibleOrderItem Item, CreateReturnItemLine Line)>();
        foreach (var line in request.Items)
        {
            var item = order.Items.SingleOrDefault(i => i.OrderItemPublicId == line.OrderItemPublicId);
            if (item is null)
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ValidationFailed,
                    $"Order item '{line.OrderItemPublicId}' does not belong to this order.");
            }

            resolvedLines.Add((item, line));
        }

        if (order.DeliveredAtUtc is null)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                "The order has not been delivered yet.");
        }

        if (ReturnEligibilityPolicy.RequiresCoolingOffDeadlineCheck(reasonType) &&
            nowUtc >= ReturnEligibilityPolicy.ComputeCoolingOffDeadlineUtc(order.DeliveredAtUtc.Value))
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnDeadlineExpired,
                "The 7-day no-reason return window has passed.");
        }

        foreach (var (item, line) in resolvedLines)
        {
            if (ReturnEligibilityPolicy.BlocksOnStartedAssembly(reasonType) && item.AssemblyStarted)
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ReturnQuantityExceeded,
                    "This custom-assembly item can no longer be returned for a no-reason cancellation.");
            }

            var alreadyRequested = await _store.SumActiveRequestedQuantityAsync(item.OrderItemId, cancellationToken);
            var remaining = item.ReturnableQuantity - item.ReturnedQuantity - alreadyRequested;
            if (line.Quantity > remaining)
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ReturnQuantityExceeded,
                    $"Requested quantity exceeds the remaining returnable quantity for '{item.SkuCodeSnapshot}'.");
            }
        }

        // Only the static ceiling (ReturnableQuantity − ReturnedQuantity) travels from this
        // already-RowVersion-checked snapshot; the dynamic part — how much of it concurrent
        // creates have already consumed — is re-verified by the store itself under lock, inside
        // the same transaction as the insert. See ReturnItemQuantityBudget's doc comment.
        var quantityBudgets = resolvedLines
            .Select(pair => new ReturnItemQuantityBudget(
                pair.Item.OrderItemId,
                pair.Line.Quantity,
                pair.Item.ReturnableQuantity - pair.Item.ReturnedQuantity))
            .ToList();

        const int maxReturnNumberAttempts = 5;
        for (var attempt = 1; attempt <= maxReturnNumberAttempts; attempt++)
        {
            var returnNumber = await GenerateCandidateReturnNumberAsync(nowUtc, cancellationToken);
            var returnRequest = new ReturnRequest(
                Guid.CreateVersion7(),
                returnNumber,
                order.OrderId,
                actor.MemberUserId,
                reasonType.ToString(),
                request.RequestReason.Trim(),
                order.ReturnPolicyVersion,
                nowUtc);

            try
            {
                var creation = await _store.CreateWithItemsAsync(
                    returnRequest,
                    quantityBudgets,
                    returnRequestId => resolvedLines
                        .Select(pair => new ReturnItem(
                            Guid.CreateVersion7(),
                            returnRequestId,
                            pair.Item.OrderItemId,
                            pair.Line.Quantity,
                            requestedRefund: 0m,
                            inspectionStatus: "NotInspected",
                            nowUtc))
                        .ToList(),
                    cancellationToken);

                var createdItemDtos = creation.Items
                    .Zip(resolvedLines, (createdItem, pair) => new ReturnItemDto(
                        createdItem.PublicId,
                        pair.Item.OrderItemPublicId,
                        pair.Item.SkuCodeSnapshot,
                        pair.Item.ProductNameSnapshot,
                        createdItem.Quantity,
                        createdItem.InspectionStatus,
                        createdItem.RestockDisposition))
                    .ToList();

                return ToDto(creation.Request, createdItemDtos, order.OrderPublicId, order.OrderNumber, [], shipment: null, shipmentEvents: []);
            }
            catch (ReturnNumberCollisionException) when (attempt < maxReturnNumberAttempts)
            {
                // Another request took this ReturnNumber between our existence check and the
                // insert; retry the whole create-with-items transaction on a new number.
            }
            catch (ReturnQuantityConflictException)
            {
                // The store's lock-protected re-check found the remaining quantity already
                // consumed by a request that committed after our snapshot was taken. Retrying
                // with a new ReturnNumber cannot help here — this is a stable business outcome,
                // not a transient collision — so it surfaces immediately as the documented error.
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ReturnQuantityExceeded,
                    "Requested quantity exceeds the remaining returnable quantity.");
            }
        }

        throw new ReturnsWriteException(
            ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
            "Unable to allocate a unique return number after multiple attempts. Please try again.");
    }

    public async Task<ReturnRequestDto> GetDetailAsync(
        ReturnActor actor,
        Guid returnPublicId,
        CancellationToken cancellationToken)
    {
        var request = await LoadOwnedAsync(actor, returnPublicId, cancellationToken);
        var order = await _orderPort.FindByIdAsync(request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException("A return's own order must always resolve.");

        var items = await _store.ListItemSummariesAsync(request.Id, cancellationToken);
        var attachments = await _store.ListCleanAttachmentSummariesAsync(request.Id, cancellationToken);
        var shipment = await _store.FindShipmentAsync(request.Id, cancellationToken);
        var events = shipment is null
            ? []
            : await _store.ListShipmentEventsAsync(shipment.Id, cancellationToken);

        return ToDto(request, items, order.OrderPublicId, order.OrderNumber, attachments, shipment, events);
    }

    public async Task<ReturnAttachmentDto> UploadAttachmentAsync(
        ReturnActor actor,
        Guid returnPublicId,
        PrivateFileUpload upload,
        CancellationToken cancellationToken)
    {
        var request = await LoadOwnedAsync(actor, returnPublicId, cancellationToken);

        if (request.Status is ReturnRequestStatus.Rejected or ReturnRequestStatus.Cancelled or ReturnRequestStatus.Completed)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                $"An attachment cannot be added while the return is {request.Status}.");
        }

        var activeCount = await _store.CountActiveAttachmentsAsync(request.Id, cancellationToken);
        if (activeCount >= MaximumAttachments)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.FileCountExceeded,
                "This return already has the maximum of 3 attachments.");
        }

        var result = await _fileStorage.StoreAsync(upload, cancellationToken);
        if (!result.IsStored)
        {
            throw MapStoreFailure(result.Status);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var stored = result.File!;
        var attachment = new ReturnAttachment(
            Guid.CreateVersion7(),
            request.Id,
            actor.MemberUserId ?? $"guest-order:{actor.GuestOrderId}",
            stored.OriginalFileName,
            stored.StorageKey,
            stored.Extension,
            stored.ContentType,
            stored.FileSizeBytes,
            stored.Sha256,
            nowUtc);
        attachment.RecordScan(PrivateAttachmentScanStatus.Clean, nowUtc);

        await _store.AddAttachmentAsync(attachment, cancellationToken);

        return new ReturnAttachmentDto(attachment.PublicId, attachment.OriginalFileName, attachment.CreatedAtUtc);
    }

    private async Task<ReturnRequest> LoadOwnedAsync(ReturnActor actor, Guid returnPublicId, CancellationToken cancellationToken)
    {
        var request = await _store.FindOwnedAsync(returnPublicId, actor.MemberUserId, actor.GuestOrderId, cancellationToken);
        if (request is null)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ResourceNotFound,
                "The return request was not found.");
        }

        return request;
    }

    private static bool ActorOwnsOrder(ReturnActor actor, OrderEligibilitySnapshot order) =>
        actor.IsGuest ? order.OrderId == actor.GuestOrderId : order.MemberUserId == actor.MemberUserId;

    private static void ValidateShape(CreateReturnRequest request)
    {
        if (request.Items.Count is 0 or > ReturnEligibilityPolicy.MaximumLineCount)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                $"Between 1 and {ReturnEligibilityPolicy.MaximumLineCount} items are required.");
        }

        if (request.Items.Select(i => i.OrderItemPublicId).Distinct().Count() != request.Items.Count)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                "Each order item can only appear once per return request.");
        }

        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ValidationFailed,
                    "Quantity must be positive.");
            }

            if (string.IsNullOrWhiteSpace(line.ReasonCode))
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ValidationFailed,
                    "A reason code is required for every item.");
            }

            if (line.Description is { Length: > 500 })
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ValidationFailed,
                    "Item description must be 500 characters or fewer.");
            }
        }

        var trimmedReason = request.RequestReason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason) || trimmedReason.Length > 1000)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                "A request reason of 1–1000 characters is required.");
        }
    }

    /// <summary>
    /// A ReturnRequest carries one aggregate ReasonCode (no per-item column exists on the
    /// finalized ReturnItem schema — see 資料字典-購物交易與售後.md), and the policy doc itself
    /// treats a return as following one path ("一般退貨與瑕疵、保固流程必須分開處理"). All lines
    /// in one request must therefore share the same reasonCode; per-item free-text descriptions
    /// are validated for shape but not separately persisted (documented decision, not silently
    /// dropped — see the implementation report).
    /// </summary>
    private static ReturnReasonType ParseSharedReasonType(CreateReturnRequest request)
    {
        var distinctReasons = request.Items.Select(i => i.ReasonCode).Distinct(StringComparer.Ordinal).ToList();
        if (distinctReasons.Count != 1)
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                "All items in one return request must share the same reason code.");
        }

        if (!ReturnEligibilityPolicy.TryParseReasonType(distinctReasons[0], out var reasonType))
        {
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                $"Unknown reason code '{distinctReasons[0]}'.");
        }

        return reasonType;
    }

    private async Task<string> GenerateCandidateReturnNumberAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidate = $"RT-{nowUtc:yyyyMMdd}-{Random.Shared.Next(0, 10_000):D4}";
            if (!await _store.ReturnNumberExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique return number.");
    }

    private static ReturnsWriteException MapStoreFailure(PrivateFileStoreStatus status) => status switch
    {
        PrivateFileStoreStatus.SizeExceeded => new ReturnsWriteException(
            ReturnsWriteException.ErrorCodes.FileSizeExceeded, "The file exceeds the 10 MB limit."),
        PrivateFileStoreStatus.FormatInvalid => new ReturnsWriteException(
            ReturnsWriteException.ErrorCodes.FileFormatInvalid, "Only PNG, JPEG and PDF files are accepted."),
        PrivateFileStoreStatus.MalwareDetected => new ReturnsWriteException(
            ReturnsWriteException.ErrorCodes.FileMalwareDetected, "The file failed the security scan."),
        _ => new ReturnsWriteException(
            ReturnsWriteException.ErrorCodes.FileScanUnavailable, "The security scan is temporarily unavailable."),
    };

    internal static IReadOnlyList<string> ComputeCustomerAvailableActions(ReturnRequest request, int attachmentCount)
    {
        var actions = new List<string>();
        if (request.Status is not (ReturnRequestStatus.Rejected or ReturnRequestStatus.Cancelled or ReturnRequestStatus.Completed) &&
            attachmentCount < MaximumAttachments)
        {
            actions.Add("uploadAttachment");
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
            ComputeCustomerAvailableActions(request, attachments.Count),
            request.RowVersion);
}
