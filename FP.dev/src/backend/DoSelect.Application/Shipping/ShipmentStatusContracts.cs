using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Auditing;
using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;

namespace DoSelect.Application.Shipping;

/// <summary>
/// M-11 物流狀態命令（組長 2026-09-04 裁定 A1）：`POST /api/v1/admin/shipments/{shipmentPublicId}/actions/{action}`。
/// 六個 action 對應 Shipment 狀態機（狀態機設計.md §5）Shipped 之後的六個目標狀態；
/// 每個命令都只推進一步，邊是否合法由 <see cref="Shipment.ChangeStatus"/> 最終把關。
/// </summary>
public static class ShipmentStatusActions
{
    public const string InTransit = "in-transit";
    public const string Delivered = "delivered";
    public const string PickupReady = "pickup-ready";
    public const string PickedUp = "picked-up";
    public const string DeliveryFailed = "delivery-failed";
    public const string Returned = "returned";

    public static readonly IReadOnlyList<string> All =
        [InTransit, Delivered, PickupReady, PickedUp, DeliveryFailed, Returned];

    public static bool TryGetTarget(string? action, out FulfillmentStatus target)
    {
        switch (action)
        {
            case InTransit:
                target = FulfillmentStatus.InTransit;
                return true;
            case Delivered:
                target = FulfillmentStatus.Delivered;
                return true;
            case PickupReady:
                target = FulfillmentStatus.PickupReady;
                return true;
            case PickedUp:
                target = FulfillmentStatus.PickedUp;
                return true;
            case DeliveryFailed:
                target = FulfillmentStatus.DeliveryFailed;
                return true;
            case Returned:
                target = FulfillmentStatus.Returned;
                return true;
            default:
                target = default;
                return false;
        }
    }

    public static string ActionOf(FulfillmentStatus target) => target switch
    {
        FulfillmentStatus.InTransit => InTransit,
        FulfillmentStatus.Delivered => Delivered,
        FulfillmentStatus.PickupReady => PickupReady,
        FulfillmentStatus.PickedUp => PickedUp,
        FulfillmentStatus.DeliveryFailed => DeliveryFailed,
        FulfillmentStatus.Returned => Returned,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    /// <summary>A1：delivery-failed／returned 必須提供穩定的 reasonCode。</summary>
    public static bool RequiresReason(string action) => action is DeliveryFailed or Returned;
}

/// <summary>
/// 物流狀態命令的原因碼白名單。A1 要求 delivery-failed／returned 帶「穩定的 reasonCode」，其他動作
/// 可選；值會進 OrderStatusHistory 與中央 Audit 的 reason，所以必須是安全代碼而不是自由文字。
/// </summary>
public static class ShipmentStatusReasonCodes
{
    public const string RecipientAbsent = "recipient_absent";
    public const string AddressInvalid = "address_invalid";
    public const string RecipientRefused = "recipient_refused";
    public const string PickupExpired = "pickup_expired";
    public const string PackageDamaged = "package_damaged";
    public const string CarrierIssue = "carrier_issue";
    public const string Redelivery = "redelivery";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [RecipientAbsent, AddressInvalid, RecipientRefused, PickupExpired, PackageDamaged, CarrierIssue, Redelivery, Other];
}

/// <summary>
/// B1 的配送方式限制：宅配才允許 InTransit → Delivered；超取才允許 InTransit → PickupReady → PickedUp。
/// <see cref="AvailableActions"/> 是 C1 要求「Admin DTO 由後端計算的 availableActions」的唯一來源，
/// 命令執行時用同一份規則擋，前端不自己猜。
/// </summary>
public static class ShipmentStatusPolicy
{
    private static readonly IReadOnlyDictionary<FulfillmentStatus, FulfillmentStatus[]> Transitions =
        new Dictionary<FulfillmentStatus, FulfillmentStatus[]>
        {
            [FulfillmentStatus.Shipped] = [FulfillmentStatus.InTransit],
            [FulfillmentStatus.InTransit] =
                [FulfillmentStatus.Delivered, FulfillmentStatus.PickupReady, FulfillmentStatus.DeliveryFailed],
            [FulfillmentStatus.PickupReady] = [FulfillmentStatus.PickedUp, FulfillmentStatus.DeliveryFailed],
            [FulfillmentStatus.DeliveryFailed] = [FulfillmentStatus.InTransit, FulfillmentStatus.Returned],
        };

    public static bool IsStorePickup(string shippingMethodKind) =>
        string.Equals(shippingMethodKind, ShippingMethodKinds.StorePickup, StringComparison.Ordinal);

    public static bool IsAllowedForMethod(FulfillmentStatus target, string shippingMethodKind)
    {
        var storePickup = IsStorePickup(shippingMethodKind);
        return target switch
        {
            FulfillmentStatus.Delivered => !storePickup,
            FulfillmentStatus.PickupReady or FulfillmentStatus.PickedUp => storePickup,
            _ => true,
        };
    }

    public static IReadOnlyList<string> AvailableActions(FulfillmentStatus current, string shippingMethodKind) =>
        Transitions.TryGetValue(current, out var targets)
            ? targets.Where(target => IsAllowedForMethod(target, shippingMethodKind))
                .Select(ShipmentStatusActions.ActionOf)
                .ToList()
            : [];

    /// <summary>進入 Delivered／PickedUp 是「交付完成」：COD 收款與 Order Completed 都掛在這裡（B1）。</summary>
    public static bool IsDeliveryCompletion(FulfillmentStatus target) =>
        target is FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp;
}

public sealed record ShipmentStatusCommand(
    Guid ShipmentPublicId,
    string Action,
    byte[] ShipmentRowVersion,
    string? ReasonCode,
    string? Note,
    string IdempotencyKey);

/// <summary>C1：狀態命令成功後回傳更新的 AdminOrderDto；<see cref="IsReplay"/> 表示這是同鍵同 payload 的重播。</summary>
public sealed record ShipmentStatusResult(AdminOrderDto Order, bool IsReplay);

public interface IShipmentStatusService
{
    /// <summary>
    /// B1：狀態轉移、ShipmentStatusHistory、Order 的 Fulfillment 投影、OrderStatusHistory、中央 Audit、
    /// 必要 Outbox（通知、COD 付款事件、模擬發票）在同一個 SQL Server Transaction；任何一步失敗整體回滾。
    /// A1：同 Idempotency-Key＋同 payload 重播原結果，不重複任何副作用；不同 payload
    /// 回 <c>idempotency_payload_conflict</c>。
    /// </summary>
    Task<ShipmentStatusResult> ExecuteAsync(
        ShipmentStatusCommand command,
        string adminUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);
}

/// <summary>A1 的 Body：`shipmentRowVersion`、`reasonCode?`、`note?`；不接受 occurredAtUtc。</summary>
public sealed class ShipmentStatusActionRequest
{
    [Required]
    [MinLength(1)]
    public byte[] ShipmentRowVersion { get; init; } = [];

    [StringLength(64)]
    public string? ReasonCode { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}
