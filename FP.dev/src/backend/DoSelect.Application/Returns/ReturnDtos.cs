using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Returns;

/// <summary>Resolved caller identity for a customer-facing Returns request — exactly one of
/// MemberUserId/GuestOrderId is set. The Api layer resolves this (member cookie, or a validated
/// guest-order-access cookie scoped to one order); Application never sees raw credentials.</summary>
public sealed record ReturnActor(string? MemberUserId, long? GuestOrderId)
{
    public bool IsGuest => GuestOrderId.HasValue;
}

// ---- Customer-facing create request ----

public sealed record CreateReturnItemLine(Guid OrderItemPublicId, int Quantity, string ReasonCode, string? Description);

public sealed record CreateReturnRequest(
    IReadOnlyList<CreateReturnItemLine> Items,
    string RequestReason,
    byte[] OrderRowVersion);

// ---- Shared read DTOs ----

public sealed record ReturnItemDto(
    Guid PublicId,
    Guid OrderItemPublicId,
    string SkuCodeSnapshot,
    string ProductNameSnapshot,
    string? Description,
    int Quantity,
    string InspectionStatus,
    RestockDisposition? RestockDisposition);

public sealed record ReturnAttachmentDto(Guid PublicId, string OriginalFileName, DateTime CreatedAtUtc);

public sealed record ReturnShipmentEventSummaryDto(string Source, string EventType, DateTime OccurredAtUtc);

public sealed record ReturnShipmentDto(
    Guid PublicId,
    string ShipmentNumber,
    ReturnShipmentMethod Method,
    ReturnShipmentStatus Status,
    string? CarrierCode,
    string? TrackingNumber,
    string? MaskedRecipientName,
    string? MaskedRecipientPhone,
    string? MaskedAddress,
    string? StoreCode,
    string? StoreName,
    DateTime? ShippedAtUtc,
    DateTime? ReceivedAtUtc,
    IReadOnlyList<ReturnShipmentEventSummaryDto> Events,
    byte[] RowVersion);

public sealed record ReturnRequestDto(
    Guid PublicId,
    string ReturnNumber,
    Guid OrderPublicId,
    string OrderNumber,
    ReturnRequestStatus Status,
    CasePriority Priority,
    string ReasonCode,
    string Description,
    IReadOnlyList<ReturnItemDto> Items,
    IReadOnlyList<ReturnAttachmentDto> Attachments,
    DateTime? RequestedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? ReceivedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? ReturnShipmentDueAtUtc,
    bool ShipmentDeadlineExtended,
    ReturnShipmentDto? Shipment,
    IReadOnlyList<string> AvailableActions,
    byte[] RowVersion);

// ---- Admin queries ----

public sealed record AdminReturnQuery(
    IReadOnlyList<ReturnRequestStatus>? Statuses = null,
    IReadOnlyList<string>? ReasonCodes = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Q = null,
    int PageNumber = 1,
    int PageSize = 20);

public sealed record AdminReturnSummaryDto(
    Guid PublicId,
    string ReturnNumber,
    Guid OrderPublicId,
    string OrderNumber,
    ReturnRequestStatus Status,
    CasePriority Priority,
    int ItemCount,
    DateTime? RequestedAtUtc,
    DateTime? ReturnShipmentDueAtUtc,
    bool NeedsAttention,
    byte[] RowVersion);

public sealed record ReturnInspectionDto(
    Guid ReturnItemPublicId,
    string Result,
    string ConditionCode,
    string? Note,
    DateTime InspectedAtUtc);

public sealed record ReturnHistoryEntryDto(
    ReturnRequestStatus? FromStatus,
    ReturnRequestStatus ToStatus,
    string? ReasonCode,
    string? Note,
    DateTime OccurredAtUtc);

public sealed record RefundableItemPreviewDto(
    Guid ReturnItemPublicId,
    string SkuCodeSnapshot,
    int Quantity,
    RestockDisposition? RestockDisposition);

public sealed record AdminReturnDetailDto(
    ReturnRequestDto Return,
    IReadOnlyList<ReturnInspectionDto> Inspections,
    IReadOnlyList<RefundableItemPreviewDto> RefundableItemsPreview,
    IReadOnlyList<ReturnHistoryEntryDto> History,
    IReadOnlyList<string> AvailableActions);

// ---- Admin action requests ----

/// <summary>
/// M-12 approves/rejects the whole request as one unit (no partial-quantity approval) — the
/// domain's <c>ReturnItem.Quantity</c> has no setter and the finalized schema carries no
/// per-item "approved quantity" column, so <see cref="ApprovedQuantity"/> is validated to equal
/// the original requested quantity rather than silently accepted and ignored.
/// </summary>
public sealed record ApproveReturnItemLine(
    Guid ReturnItemPublicId,
    [Range(1, int.MaxValue)] int ApprovedQuantity,
    bool InspectionRequired);

public sealed record ApproveReturnRequest(
    bool Approved,
    [Required, MinLength(1)] IReadOnlyList<ApproveReturnItemLine> Items,
    [Required, NotWhiteSpace, StringLength(100, MinimumLength = 1)] string ReasonCode,
    [StringLength(1000)] string? Note,
    [RowVersionRequired] byte[] ReturnRowVersion);

public sealed record ReceiveReturnRequest(
    [StringLength(1000)] string? Note,
    [RowVersionRequired] byte[] ReturnRowVersion);

public sealed record InspectReturnItemLine(
    Guid ReturnItemPublicId,
    [Required, NotWhiteSpace, StringLength(50, MinimumLength = 1)] string ConditionCode,
    RestockDisposition Disposition,
    [StringLength(1000)] string? Note);

public sealed record InspectReturnRequest(
    [Required, MinLength(1)] IReadOnlyList<InspectReturnItemLine> Items,
    [RowVersionRequired] byte[] ReturnRowVersion);

public sealed record ExtendShipmentDeadlineRequest(
    [Required, NotWhiteSpace, StringLength(100, MinimumLength = 1)] string ReasonCode,
    [RowVersionRequired] byte[] ReturnRowVersion);

public sealed record CreateReturnShipmentRequest(
    ReturnShipmentMethod Method,
    [StringLength(50)] string? CarrierCode,
    [StringLength(100)] string? RecipientName,
    [StringLength(30)] string? RecipientPhone,
    [StringLength(10)] string? PostalCode,
    [StringLength(200)] string? AddressLine,
    [StringLength(50)] string? StoreCode,
    [StringLength(100)] string? StoreName,
    [RowVersionRequired] byte[] ReturnRowVersion);

public sealed record AppendReturnShipmentEventRequest(
    [Required, NotWhiteSpace, StringLength(100, MinimumLength = 1)] string Source,
    [Required, NotWhiteSpace, StringLength(200, MinimumLength = 1)] string ExternalEventId,
    [Required, NotWhiteSpace, StringLength(50, MinimumLength = 1)] string EventType,
    [UtcDateTime] DateTime OccurredAtUtc,
    [StringLength(1000)] string? Description);
