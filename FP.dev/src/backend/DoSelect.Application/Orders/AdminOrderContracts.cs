using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;

namespace DoSelect.Application.Orders;

public sealed record AdminOrderItemDto(
    Guid PublicId,
    string SkuCodeSnapshot,
    string ProductNameSnapshot,
    string SkuNameSnapshot,
    int Quantity,
    decimal ListUnitPrice,
    decimal SaleUnitPrice,
    decimal FinalUnitPrice,
    decimal UnitCostSnapshot,
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
/// 這裡把內部 Entity 欄位原樣對管理端開放（不對訪客/會員開放，本切片只有後台會用到）；
/// 待 alex 確認是否要調整欄位或遮蔽 ActorUserId。
/// </summary>
public sealed record OrderStatusHistoryDto(
    string StateDimension,
    string? FromStatus,
    string ToStatus,
    string? ReasonCode,
    string? ActorUserId,
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
    byte[] RowVersion);

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

    Task<OrderRecipientDto> GetRecipientAsync(Guid orderPublicId, CancellationToken cancellationToken);

    Task<AdminOrderDto> ExecuteActionAsync(
        Guid orderPublicId,
        string action,
        string actorUserId,
        string traceId,
        AdminOrderActionRequest request,
        CancellationToken cancellationToken);
}
