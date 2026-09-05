using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Refunds;
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

/// <summary>
/// Items must never be null, but MAY be an empty array — Approved=false (rejection) requires no
/// items at all, and Approved=true's "must cover every item on this return exactly once" rule is
/// enforced by ValidateExactItemSet in the Application layer (an empty submission naturally fails
/// that check when the return has any items), not by a DTO-level MinLength that would also block
/// the legitimate empty-array rejection payload.
///
/// Property-style (not a positional primary constructor) — the generated native OpenApi document
/// only reads DataAnnotations from properties. A record with a primary constructor either omits
/// parameter-level validation metadata from the schema entirely (bare attributes), or throws
/// InvalidOperationException at request time the moment ANY property-targeted metadata exists
/// alongside it ("validation metadata ... must be associated with the constructor parameter") —
/// confirmed by testing both a property-only and a dual (property + parameter) placement on this
/// exact record before converting it. See ReturnDtosOpenApiContractTests for the regression test.
///
/// Every property that used to be a required positional-constructor parameter (no C# default
/// value, so System.Text.Json rejected a JSON body omitting it) carries the `required` MODIFIER
/// here — not just a [Required] attribute — to keep that same omission rejected at the
/// deserialization layer, and to keep it out of the schema's `required` set. [Required] alone
/// only rejects null/empty *string* values; a value type like bool or byte[] silently binds to
/// its C# default when its JSON property is absent, which would otherwise be a real behavior
/// regression versus the old positional record.
/// </summary>
public sealed record ApproveReturnRequest
{
    public required bool Approved { get; init; }

    [Required]
    public required IReadOnlyList<ApproveReturnItemLine> Items { get; init; }

    [Required, NotWhiteSpace, StringLength(64, MinimumLength = 1)]
    public required string ReasonCode { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }

    public AssemblyFeeDisposition? AssemblyFeeDisposition { get; init; }

    // alex 2026-09-05 #111 review P1：admin-web 為了不讓 JavaScript 浮點數靜默改寫可信的
    // 退款輸入，把這個欄位當原始 decimal 字串送出（例如 "1.01"）。明確加上這個屬性，
    // 不依賴目前這個專案的 JsonSerializerOptions（Web defaults + enum converter）恰好
    // 能反序列化字串——那是這個組合現在的行為，不是這個型別的保證，換一種設定或未來
    // .NET 版本都可能改變。加了這個屬性，行為就不再取決於外部設定。
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? ReturnShippingCost { get; init; }

    [RowVersionRequired]
    public required byte[] ReturnRowVersion { get; init; }
}

public sealed record ReceiveReturnRequest
{
    [StringLength(500)]
    public string? Note { get; init; }

    [RowVersionRequired]
    public required byte[] ReturnRowVersion { get; init; }
}

public sealed record InspectReturnItemLine(
    Guid ReturnItemPublicId,
    [Required, NotWhiteSpace, StringLength(50, MinimumLength = 1)] string ConditionCode,
    RestockDisposition Disposition,
    [StringLength(1000)] string? Note);

public sealed record InspectReturnRequest
{
    public InspectReturnRequest(
        IReadOnlyList<InspectReturnItemLine> items,
        byte[] returnRowVersion,
        AssemblyFeeDisposition? assemblyFeeDisposition = null,
        decimal? returnShippingCost = null)
    {
        Items = items;
        ReturnRowVersion = returnRowVersion;
        AssemblyFeeDisposition = assemblyFeeDisposition;
        ReturnShippingCost = returnShippingCost;
    }

    [Required, MinLength(1)]
    public IReadOnlyList<InspectReturnItemLine> Items { get; init; }

    [RowVersionRequired]
    public byte[] ReturnRowVersion { get; init; }

    public AssemblyFeeDisposition? AssemblyFeeDisposition { get; init; }

    // 見 ApproveReturnRequest.ReturnShippingCost 上的說明——同一個原因，同一個修正。
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? ReturnShippingCost { get; init; }
}

public sealed record ExtendShipmentDeadlineRequest
{
    [Required, NotWhiteSpace, StringLength(64, MinimumLength = 1)]
    public required string ReasonCode { get; init; }

    [RowVersionRequired]
    public required byte[] ReturnRowVersion { get; init; }
}

public sealed record CreateReturnShipmentRequest
{
    public required ReturnShipmentMethod Method { get; init; }

    [StringLength(32)]
    public string? CarrierCode { get; init; }

    [StringLength(100)]
    public string? RecipientName { get; init; }

    [StringLength(30)]
    public string? RecipientPhone { get; init; }

    [StringLength(10)]
    public string? PostalCode { get; init; }

    [StringLength(200)]
    public string? AddressLine { get; init; }

    [StringLength(50)]
    public string? StoreCode { get; init; }

    [StringLength(100)]
    public string? StoreName { get; init; }

    [RowVersionRequired]
    public required byte[] ReturnRowVersion { get; init; }
}

public sealed record AppendReturnShipmentEventRequest
{
    [Required, NotWhiteSpace, StringLength(32, MinimumLength = 1)]
    public required string Source { get; init; }

    [Required, NotWhiteSpace, StringLength(128, MinimumLength = 1)]
    public required string ExternalEventId { get; init; }

    [Required, NotWhiteSpace, StringLength(50, MinimumLength = 1)]
    public required string EventType { get; init; }

    [UtcDateTime]
    public required DateTime OccurredAtUtc { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }
}
