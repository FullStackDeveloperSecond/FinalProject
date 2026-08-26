using DoSelect.Domain.Common;
using DoSelect.Domain.Support;

namespace DoSelect.Domain.Returns;

public enum ReturnRequestStatus { Requested, UnderReview, Approved, AwaitingShipment, InTransit, Received, Inspecting, AwaitingRefund, Completed, Rejected, Cancelled }
public enum RestockDisposition { Resellable, Quarantine, Scrap }
public enum ReturnShipmentMethod { HomePickup, ConvenienceStore, SelfShip }
public enum ReturnShipmentStatus { Pending, Scheduled, PickedUp, InTransit, Delivered, Failed, Cancelled }

public sealed class ReturnRequest : MutablePublicEntity
{
    private static readonly IReadOnlyDictionary<ReturnRequestStatus, ReturnRequestStatus[]> Allowed = new Dictionary<ReturnRequestStatus, ReturnRequestStatus[]>
    {
        [ReturnRequestStatus.Requested] = [ReturnRequestStatus.UnderReview, ReturnRequestStatus.Cancelled],
        [ReturnRequestStatus.UnderReview] = [ReturnRequestStatus.Approved, ReturnRequestStatus.Rejected],
        [ReturnRequestStatus.Approved] = [ReturnRequestStatus.AwaitingShipment, ReturnRequestStatus.AwaitingRefund],
        [ReturnRequestStatus.AwaitingShipment] = [ReturnRequestStatus.InTransit, ReturnRequestStatus.Cancelled],
        [ReturnRequestStatus.InTransit] = [ReturnRequestStatus.Received],
        [ReturnRequestStatus.Received] = [ReturnRequestStatus.Inspecting],
        [ReturnRequestStatus.Inspecting] = [ReturnRequestStatus.AwaitingRefund, ReturnRequestStatus.Rejected],
        [ReturnRequestStatus.AwaitingRefund] = [ReturnRequestStatus.Completed],
        [ReturnRequestStatus.Completed] = [],
        [ReturnRequestStatus.Rejected] = [],
        [ReturnRequestStatus.Cancelled] = [],
    };
    private ReturnRequest() { }
    public ReturnRequest(Guid publicId, string returnNumber, long orderId, string? requesterUserId, string reasonCode, string description, int policyVersion, DateTime requestedAtUtc)
        : base(publicId, requestedAtUtc)
    { if (orderId <= 0 || policyVersion <= 0) throw new ArgumentOutOfRangeException(nameof(orderId)); ReturnNumber = RequireText(returnNumber, nameof(returnNumber)); OrderId = orderId; RequesterUserId = Optional(requesterUserId); Status = ReturnRequestStatus.Requested; Priority = CasePriority.Normal; ReasonCode = RequireText(reasonCode, nameof(reasonCode)); Description = RequireText(description, nameof(description)); PolicyVersion = policyVersion; RequestedAtUtc = RequireUtc(requestedAtUtc, nameof(requestedAtUtc)); }
    public string ReturnNumber { get; private set; } = string.Empty; public long OrderId { get; private set; }
    public string? RequesterUserId { get; private set; }
    public ReturnRequestStatus Status { get; private set; }
    public CasePriority Priority { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty; public string Description { get; private set; } = string.Empty; public string? AssigneeAdminUserId { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public int PolicyVersion { get; private set; }
    public DateTime? RequestedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime? ReturnShipmentDueAtUtc { get; private set; }
    public void Assign(string adminUserId, DateTime occurredAtUtc) { AssigneeAdminUserId = RequireText(adminUserId, nameof(adminUserId)); MarkUpdated(occurredAtUtc); }
    public void ChangePriority(CasePriority priority, DateTime occurredAtUtc)
    { if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority)); Priority = priority; MarkUpdated(occurredAtUtc); }
    public void Approve(string adminUserId, bool requiresShipment, DateTime occurredAtUtc)
    { if (Status != ReturnRequestStatus.UnderReview) throw new InvalidOperationException("The return is not under review."); ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId)); Transition(ReturnRequestStatus.Approved, occurredAtUtc); ApprovedAtUtc = occurredAtUtc; if (requiresShipment) { ReturnShipmentDueAtUtc = occurredAtUtc.AddDays(7); Transition(ReturnRequestStatus.AwaitingShipment, occurredAtUtc); } else Transition(ReturnRequestStatus.AwaitingRefund, occurredAtUtc); }
    /// <summary>Rejects the request from UnderReview (ineligible) or Inspecting (failed inspection) — both are legal Transition targets already.</summary>
    public void Reject(string adminUserId, DateTime occurredAtUtc)
    { ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId)); Transition(ReturnRequestStatus.Rejected, occurredAtUtc); }
    /// <summary>
    /// True once the shipment deadline no longer equals its original Approve-time value
    /// (Approved + 7 days) — derives the one-time-extension guard from two fields that are
    /// already persisted, instead of adding a new column or a synthetic history marker row.
    /// </summary>
    public bool HasShipmentDeadlineBeenExtended =>
        ApprovedAtUtc.HasValue && ReturnShipmentDueAtUtc.HasValue &&
        ReturnShipmentDueAtUtc.Value != ApprovedAtUtc.Value.AddDays(7);
    public void ExtendShipmentDeadline(DateTime occurredAtUtc)
    {
        if (Status != ReturnRequestStatus.AwaitingShipment) throw new InvalidOperationException("Only a return awaiting shipment can have its deadline extended.");
        if (ReturnShipmentDueAtUtc is null) throw new InvalidOperationException("The return has no shipment deadline to extend.");
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (occurredAtUtc >= ReturnShipmentDueAtUtc.Value) throw new InvalidOperationException("The shipment deadline has already expired.");
        if (HasShipmentDeadlineBeenExtended) throw new InvalidOperationException("The shipment deadline has already been extended once.");
        ReturnShipmentDueAtUtc = ReturnShipmentDueAtUtc.Value.AddDays(7);
        MarkUpdated(occurredAtUtc);
    }
    public void Transition(ReturnRequestStatus next, DateTime occurredAtUtc)
    { if (!Allowed[Status].Contains(next)) throw new InvalidOperationException($"Return cannot move from {Status} to {next}."); occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc)); Status = next; ReceivedAtUtc = next == ReturnRequestStatus.Received ? occurredAtUtc : ReceivedAtUtc; ClosedAtUtc = next is ReturnRequestStatus.Completed or ReturnRequestStatus.Rejected or ReturnRequestStatus.Cancelled ? occurredAtUtc : ClosedAtUtc; MarkUpdated(occurredAtUtc); }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReturnItem : PublicEntity
{
    private ReturnItem() { }
    public ReturnItem(Guid publicId, long returnRequestId, long orderItemId, int quantity, decimal requestedRefund, string inspectionStatus, DateTime createdAtUtc, string? description = null) : base(publicId, createdAtUtc)
    {
        if (returnRequestId <= 0 || orderItemId <= 0 || quantity <= 0 || requestedRefund < 0) throw new ArgumentOutOfRangeException(nameof(returnRequestId));
        ReturnRequestId = returnRequestId;
        OrderItemId = orderItemId;
        Quantity = quantity;
        RequestedRefund = requestedRefund;
        InspectionStatus = RequireText(inspectionStatus, nameof(inspectionStatus));
        Description = OptionalDescription(description);
    }
    public long ReturnRequestId { get; private set; }
    public long OrderItemId { get; private set; }
    public int Quantity { get; private set; }
    public decimal RequestedRefund { get; private set; }
    public string? Description { get; private set; }
    public string InspectionStatus { get; private set; } = string.Empty; public RestockDisposition? RestockDisposition { get; private set; }
    public void RecordInspectionSummary(string inspectionStatus, RestockDisposition? disposition)
    { InspectionStatus = RequireText(inspectionStatus, nameof(inspectionStatus)); RestockDisposition = disposition; }
    private static string? OptionalDescription(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is { Length: > 500 })
        {
            throw new ArgumentException("Return item description cannot exceed 500 characters.", nameof(value));
        }

        return normalized;
    }
}

public sealed class ReturnInspection : PublicEntity
{
    private ReturnInspection() { }
    public ReturnInspection(Guid publicId, long returnItemId, string result, string conditionCode, string? note, string inspectedByAdminUserId, DateTime inspectedAtUtc) : base(publicId, inspectedAtUtc)
    { if (returnItemId <= 0) throw new ArgumentOutOfRangeException(nameof(returnItemId)); ReturnItemId = returnItemId; Result = RequireText(result, nameof(result)); ConditionCode = RequireText(conditionCode, nameof(conditionCode)); Note = Optional(note); InspectedByAdminUserId = RequireText(inspectedByAdminUserId, nameof(inspectedByAdminUserId)); InspectedAtUtc = RequireUtc(inspectedAtUtc, nameof(inspectedAtUtc)); }
    public long ReturnItemId { get; private set; }
    public string Result { get; private set; } = string.Empty; public string ConditionCode { get; private set; } = string.Empty; public string? Note { get; private set; }
    public string InspectedByAdminUserId { get; private set; } = string.Empty; public DateTime InspectedAtUtc { get; private set; }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReturnAttachment : MutablePublicEntity
{
    private ReturnAttachment() { }
    /// <summary>
    /// Exactly one of <paramref name="uploadedByUserId"/> (a real Member FK) or
    /// <paramref name="uploadedByGuestOrderId"/> (a real Orders FK) must be supplied — never a
    /// synthetic string like "guest-order:{id}" crammed into the Member FK column, which is a
    /// required ApplicationUser reference and cannot legitimately hold a guest identity.
    /// </summary>
    public ReturnAttachment(Guid publicId, long returnRequestId, string? uploadedByUserId, long? uploadedByGuestOrderId, string originalFileName, string storageKey, string extension, string mimeType, long fileSizeBytes, byte[] sha256, DateTime createdAtUtc) : base(publicId, createdAtUtc)
    {
        AttachmentRules.Validate(fileSizeBytes, sha256);
        if (returnRequestId <= 0) throw new ArgumentOutOfRangeException(nameof(returnRequestId));
        var hasMember = !string.IsNullOrWhiteSpace(uploadedByUserId);
        var hasGuest = uploadedByGuestOrderId is > 0;
        if (hasMember == hasGuest) throw new ArgumentException("Exactly one of uploadedByUserId or uploadedByGuestOrderId is required.");
        ReturnRequestId = returnRequestId;
        UploadedByUserId = hasMember ? uploadedByUserId!.Trim() : null;
        UploadedByGuestOrderId = hasGuest ? uploadedByGuestOrderId : null;
        OriginalFileName = RequireText(originalFileName, nameof(originalFileName)); StorageKey = RequireText(storageKey, nameof(storageKey)); Extension = RequireText(extension, nameof(extension)); MimeType = RequireText(mimeType, nameof(mimeType)); FileSizeBytes = fileSizeBytes; Sha256 = sha256.ToArray(); ScanStatus = PrivateAttachmentScanStatus.Pending;
    }
    public long ReturnRequestId { get; private set; }
    public string? UploadedByUserId { get; private set; }
    public long? UploadedByGuestOrderId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty; public string StorageKey { get; private set; } = string.Empty;
    public string Extension { get; private set; } = string.Empty; public string MimeType { get; private set; } = string.Empty; public long FileSizeBytes { get; private set; }
    public byte[] Sha256 { get; private set; } = [];
    public PrivateAttachmentScanStatus ScanStatus { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public DateTime? RetentionUntilUtc { get; private set; }
    public bool LegalHold { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public void RecordScan(PrivateAttachmentScanStatus result, DateTime scannedAtUtc)
    { if (result == PrivateAttachmentScanStatus.Pending) throw new ArgumentException("A completed scan result is required.", nameof(result)); ScanStatus = result; ScannedAtUtc = RequireUtc(scannedAtUtc, nameof(scannedAtUtc)); MarkUpdated(scannedAtUtc); }
    public void ScheduleRetention(DateTime retentionUntilUtc, DateTime occurredAtUtc)
    { RetentionUntilUtc = RequireUtc(retentionUntilUtc, nameof(retentionUntilUtc)); MarkUpdated(occurredAtUtc); }
    public void SetLegalHold(bool enabled, DateTime occurredAtUtc)
    { LegalHold = enabled; MarkUpdated(occurredAtUtc); }
    public void SoftDelete(DateTime deletedAtUtc)
    { if (LegalHold) throw new InvalidOperationException("An attachment under legal hold cannot be deleted."); DeletedAtUtc = RequireUtc(deletedAtUtc, nameof(deletedAtUtc)); MarkUpdated(deletedAtUtc); }
}

public sealed class ReturnAssignmentHistory
{
    private ReturnAssignmentHistory() { }
    public ReturnAssignmentHistory(long returnRequestId, string? fromAdminUserId, string? toAdminUserId, AssignmentAction action, string? reason, string? actorUserId, DateTime occurredAtUtc)
    { if (returnRequestId <= 0 || occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid return assignment history."); ReturnRequestId = returnRequestId; FromAdminUserId = Optional(fromAdminUserId); ToAdminUserId = Optional(toAdminUserId); Action = action; Reason = Optional(reason); ActorUserId = Optional(actorUserId); OccurredAtUtc = occurredAtUtc; }
    public long Id { get; private set; }
    public long ReturnRequestId { get; private set; }
    public string? FromAdminUserId { get; private set; }
    public string? ToAdminUserId { get; private set; }
    public AssignmentAction Action { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReturnStatusHistory
{
    private ReturnStatusHistory() { }
    public ReturnStatusHistory(long returnRequestId, ReturnRequestStatus? fromStatus, ReturnRequestStatus toStatus, string? reasonCode, string? note, string? actorUserId, DateTime occurredAtUtc)
    { if (returnRequestId <= 0 || occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid return status history."); ReturnRequestId = returnRequestId; FromStatus = fromStatus; ToStatus = toStatus; ReasonCode = Optional(reasonCode); Note = Optional(note); ActorUserId = Optional(actorUserId); OccurredAtUtc = occurredAtUtc; }
    public long Id { get; private set; }
    public long ReturnRequestId { get; private set; }
    public ReturnRequestStatus? FromStatus { get; private set; }
    public ReturnRequestStatus ToStatus { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? Note { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReturnShipment : MutablePublicEntity
{
    private ReturnShipment() { }
    /// <summary>
    /// Recipient/store fields are an immutable snapshot taken at creation (per policy: home
    /// pickup captures the recipient/address actually used for that pickup, never the order's
    /// live address); there is deliberately no setter for them after construction.
    /// </summary>
    public ReturnShipment(
        Guid publicId, long returnRequestId, string shipmentNumber, ReturnShipmentMethod method,
        string? carrierCode, string? trackingNumber,
        string? recipientName, string? recipientPhone, string? postalCode, string? addressLine,
        string? storeCode, string? storeName,
        DateTime createdAtUtc) : base(publicId, createdAtUtc)
    {
        if (returnRequestId <= 0) throw new ArgumentOutOfRangeException(nameof(returnRequestId));
        if (method == ReturnShipmentMethod.HomePickup &&
            (string.IsNullOrWhiteSpace(recipientName) || string.IsNullOrWhiteSpace(recipientPhone) ||
             string.IsNullOrWhiteSpace(postalCode) || string.IsNullOrWhiteSpace(addressLine)))
        {
            throw new ArgumentException("Home pickup requires a recipient, phone, postal code and address.", nameof(method));
        }
        if (method == ReturnShipmentMethod.ConvenienceStore &&
            (string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(storeName)))
        {
            throw new ArgumentException("Convenience-store drop-off requires a store code and name.", nameof(method));
        }
        ReturnRequestId = returnRequestId; ShipmentNumber = RequireText(shipmentNumber, nameof(shipmentNumber)); Method = method; CarrierCode = Optional(carrierCode); TrackingNumber = Optional(trackingNumber);
        RecipientName = Optional(recipientName); RecipientPhone = Optional(recipientPhone); PostalCode = Optional(postalCode); AddressLine = Optional(addressLine);
        StoreCode = Optional(storeCode); StoreName = Optional(storeName);
        Status = ReturnShipmentStatus.Pending;
    }
    public long ReturnRequestId { get; private set; }
    public string ShipmentNumber { get; private set; } = string.Empty; public ReturnShipmentMethod Method { get; private set; }
    public string? CarrierCode { get; private set; }
    public string? TrackingNumber { get; private set; }
    public ReturnShipmentStatus Status { get; private set; }
    public string? RecipientName { get; private set; }
    public string? RecipientPhone { get; private set; }
    public string? PostalCode { get; private set; }
    public string? AddressLine { get; private set; }
    public string? StoreCode { get; private set; }
    public string? StoreName { get; private set; }
    public DateTime? ScheduledPickupAtUtc { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    /// <summary>
    /// Denormalized carrier-facing status, driven by append-only ReturnShipmentEvents. The
    /// authoritative business flow is ReturnRequestStatus's own AwaitingShipment → InTransit →
    /// Received transitions; this only tracks the shipment sub-entity's own last-known state
    /// and refuses to move once it reaches a terminal value.
    /// </summary>
    public void ApplyEventStatus(ReturnShipmentStatus status, DateTime occurredAtUtc)
    {
        if (Status is ReturnShipmentStatus.Delivered or ReturnShipmentStatus.Cancelled)
        {
            throw new InvalidOperationException($"A {Status} shipment cannot change status.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Status = status;
        ShippedAtUtc = status is ReturnShipmentStatus.InTransit or ReturnShipmentStatus.PickedUp
            ? ShippedAtUtc ?? occurredAtUtc
            : ShippedAtUtc;
        ReceivedAtUtc = status == ReturnShipmentStatus.Delivered ? occurredAtUtc : ReceivedAtUtc;
        MarkUpdated(occurredAtUtc);
    }
    public void RecordTrackingNumber(string trackingNumber, DateTime occurredAtUtc)
    { TrackingNumber = RequireText(trackingNumber, nameof(trackingNumber)); MarkUpdated(occurredAtUtc); }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReturnShipmentEvent
{
    private ReturnShipmentEvent() { }
    public ReturnShipmentEvent(long returnShipmentId, string externalEventId, string source, string eventType, string? eventCode, string? description, DateTime occurredAtUtc, DateTime receivedAtUtc, byte[]? payloadHash, string? payloadSummaryJson)
    { if (returnShipmentId <= 0 || occurredAtUtc.Kind != DateTimeKind.Utc || receivedAtUtc.Kind != DateTimeKind.Utc || payloadHash is not null && payloadHash.Length != 32) throw new ArgumentException("Invalid return shipment event."); ReturnShipmentId = returnShipmentId; ExternalEventId = Required(externalEventId); Source = Required(source); EventType = Required(eventType); EventCode = Optional(eventCode); Description = Optional(description); OccurredAtUtc = occurredAtUtc; ReceivedAtUtc = receivedAtUtc; PayloadHash = payloadHash?.ToArray(); PayloadSummaryJson = Optional(payloadSummaryJson); }
    public long Id { get; private set; }
    public long ReturnShipmentId { get; private set; }
    public string ExternalEventId { get; private set; } = string.Empty; public string Source { get; private set; } = string.Empty; public string EventType { get; private set; } = string.Empty;
    public string? EventCode { get; private set; }
    public string? Description { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public byte[]? PayloadHash { get; private set; }
    public string? PayloadSummaryJson { get; private set; }
    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim(); private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
