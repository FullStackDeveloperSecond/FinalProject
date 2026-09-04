using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;

namespace DoSelect.Application.Orders;

/// <summary>
/// 角色與權限.md：「成本欄位只在 Catalog 維護 SKU 成本、Finance／SuperAdmin 財務報表中可見；
/// 其他角色的商品 DTO 不回傳成本」——AdminOrdersController 只掛 Order.Manage（OrderManager／
/// SuperAdmin），OrderManager 不在成本可見角色內，這裡刻意不含 UnitCostSnapshot（Alex review，
/// 2026-08-28）。若後續有 Finance 專用訂單成本檢視需求，另建專用 DTO／Policy，不要加回這裡。
/// </summary>
public sealed record AdminOrderItemDto(
    Guid PublicId,
    string SkuCodeSnapshot,
    string ProductNameSnapshot,
    string SkuNameSnapshot,
    int Quantity,
    decimal ListUnitPrice,
    decimal SaleUnitPrice,
    decimal FinalUnitPrice,
    decimal LineSubtotal,
    decimal DiscountAllocation,
    decimal LineTotal,
    int ReturnableQuantity,
    int ReturnedQuantity);

public sealed record AdminOrderAmountsDto(
    decimal MerchandiseSubtotal,
    decimal ItemDiscountTotal,
    decimal ShippingFee,
    decimal AssemblyFee,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RefundedAmount,
    string Currency);

/// <summary>
/// API DTO與Schema契約.md 沒有為 OrderStatusHistory 定義 API Schema（只有 DB 欄位定義）。
/// ActorPublicId 刻意不是內部 Identity ActorUserId——後者是 AspNetUsers 的 Identity 字串 Id，
/// 不應該固定進公開 API 契約（Alex review，2026-08-28）；改回傳解析後的管理員 PublicId，
/// 系統事件（ActorUserId 為 Null）則 ActorPublicId 也是 Null。
/// </summary>
public sealed record OrderStatusHistoryDto(
    string StateDimension,
    string? FromStatus,
    string ToStatus,
    string? ReasonCode,
    Guid? ActorPublicId,
    DateTime OccurredAtUtc);

/// <summary>
/// API DTO與Schema契約.md／API Endpoint目錄.md 都沒有為後台訂單摘要狀態／徽章定義具名列舉。
/// 這裡依 UC-ADM-ORDER-01 驗收條件——「待出貨、已出貨、已完成、已取消」是狀態機衍生顯示，且
/// 「配送中且部分退款」需同時顯示配送中主狀態與部分退款徽章——推導出最小集合。待 alex 確認正式清單。
/// </summary>
public static class AdminOrderSummaryStatuses
{
    public const string AwaitingShipment = "awaitingShipment";
    public const string Shipped = "shipped";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All =
    [
        AwaitingShipment,
        Shipped,
        Completed,
        Cancelled,
    ];
}

public static class AdminOrderBadges
{
    public const string PartiallyRefunded = "partiallyRefunded";
    public const string Refunded = "refunded";
    public const string PaymentOverdue = "paymentOverdue";

    public static readonly IReadOnlyList<string> All =
    [
        PartiallyRefunded,
        Refunded,
        PaymentOverdue,
    ];
}

public sealed record AdminOrderSummaryDto(
    Guid PublicId,
    string OrderNumber,
    string BuyerType,
    string MaskedBuyerEmail,
    string OrderStatus,
    string PaymentStatus,
    string FulfillmentStatus,
    string AssemblyStatus,
    string OrderRefundStatus,
    string SummaryStatus,
    IReadOnlyList<string> Badges,
    decimal GrandTotal,
    string Currency,
    string ShippingMethodCode,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? CompletedAtUtc,
    byte[] RowVersion);

public sealed record AdminOrderDto(
    Guid PublicId,
    string OrderNumber,
    string BuyerType,
    string MaskedBuyerEmail,
    string OrderStatus,
    string PaymentStatus,
    string FulfillmentStatus,
    string AssemblyStatus,
    string OrderRefundStatus,
    string SummaryStatus,
    IReadOnlyList<string> Badges,
    IReadOnlyList<AdminOrderItemDto> Items,
    AdminOrderAmountsDto Amounts,
    string ShippingMethodCode,
    string? StoreName,
    IReadOnlyList<OrderStatusHistoryDto> StatusHistory,
    IReadOnlyList<string> AvailableActions,
    DateTime? PaymentDueAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    DateTime CreatedAtUtc,
    byte[] RowVersion,
    // C1（組長 2026-09-04）：物流摘要、歷程與後端計算的 availableActions 直接掛在訂單明細上，
    // 不另開查詢端點。沒有物流單時為 null。
    AdminShipmentDto? Shipment = null);

/// <summary>
/// UC-ADM-ORDER-02：完整收件資料只給有履約權限者查看，且每次讀取需可稽核。本切片的
/// AdminOrdersController 只掛 Order.Manage policy（OrderManager／SuperAdmin），一般客服的
/// 遮蔽版留待客服模組（kafen）整合時再擴充判斷邏輯——所以這裡先固定回傳
/// AccessPurpose="OrderFulfillment"，不開放呼叫端自訂用途字串。
/// </summary>
public sealed record OrderRecipientDto(
    Guid OrderPublicId,
    string RecipientName,
    string RecipientPhone,
    string RecipientEmail,
    string? PostalCode,
    string? RecipientCity,
    string? RecipientDistrict,
    string? AddressLine1,
    string? AddressLine2,
    string ShippingMethodCode,
    string? StoreCode,
    string? StoreName,
    string? StoreAddress,
    string AccessPurpose);

public sealed record AdminOrderQuery(
    IReadOnlyList<string>? SummaryStatus,
    IReadOnlyList<string>? Badge,
    string? Cursor,
    int PageSize);

/// <summary>
/// API Endpoint目錄.md 沒有列出 POST /api/v1/admin/orders/{id}/actions/{action} 的白名單
/// （只有非窮舉例子）。這裡只開放 狀態機設計.md 第 1 節「轉移規則」表中確認由管理員直接觸發、
/// 而非由付款／物流投影自動衍生的兩個 OrderStatus 轉移；`Order.ChangeOrderStatus` 既有的
/// AllowedTransitions 仍是最終防線。待 alex 確認正式白名單（例如是否要納入建立出貨）。
/// </summary>
public static class AdminOrderActions
{
    public const string StartProcessing = "startProcessing";
    public const string Cancel = "cancel";

    public static readonly IReadOnlyList<string> All = [StartProcessing, Cancel];
}

public sealed record AdminOrderActionRequest(
    [StringLength(64)] string? ReasonCode,
    [StringLength(500)] string? Note,
    byte[] RowVersion);

public interface IAdminOrderService
{
    Task<CursorPage<AdminOrderSummaryDto>> ListAsync(
        AdminOrderQuery query,
        CancellationToken cancellationToken);

    Task<AdminOrderDto> GetAsync(Guid orderPublicId, CancellationToken cancellationToken);

    /// <summary>UC-ADM-ORDER-02：完整收件資料每次讀取都要寫中央 Audit（Alex review，
    /// 2026-08-28）——actorUserId 用於解析寫入 Audit 的管理員 PublicId。</summary>
    Task<OrderRecipientDto> GetRecipientAsync(
        Guid orderPublicId,
        string actorUserId,
        OrderCancellationAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<AdminOrderDto> ExecuteActionAsync(
        Guid orderPublicId,
        string action,
        string actorUserId,
        OrderCancellationAuditContext auditContext,
        AdminOrderActionRequest request,
        CancellationToken cancellationToken);
}
