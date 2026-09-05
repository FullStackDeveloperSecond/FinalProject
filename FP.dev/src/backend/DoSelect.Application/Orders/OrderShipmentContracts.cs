using DoSelect.Domain.Orders;

namespace DoSelect.Application.Orders;

/// <summary>
/// C1：不新增物流查詢端點，直接擴充既有訂單 DTO。後台形狀帶 RowVersion、Actor 與由後端計算的
/// availableActions；顧客形狀（<see cref="OrderShipmentDto"/>）只有單號、狀態與時間歷程——不回
/// Actor、原因備註或內部 ID。
/// </summary>
public sealed record AdminShipmentDto(
    Guid PublicId,
    string ShipmentNumber,
    string? TrackingNumber,
    string Status,
    string ShippingMethodCode,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    IReadOnlyList<AdminShipmentHistoryDto> History,
    IReadOnlyList<string> AvailableActions,
    byte[] RowVersion);

public sealed record AdminShipmentHistoryDto(
    string? FromStatus,
    string ToStatus,
    Guid? ActorPublicId,
    DateTime OccurredAtUtc);

public sealed record OrderShipmentDto(
    string ShipmentNumber,
    string? TrackingNumber,
    FulfillmentStatus Status,
    string ShippingMethodCode,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    IReadOnlyList<OrderShipmentHistoryDto> History);

public sealed record OrderShipmentHistoryDto(
    FulfillmentStatus? FromStatus,
    FulfillmentStatus ToStatus,
    DateTime OccurredAtUtc);
