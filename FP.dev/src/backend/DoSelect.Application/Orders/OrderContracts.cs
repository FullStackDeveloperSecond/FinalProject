using System.ComponentModel.DataAnnotations;
using DoSelect.Domain.Orders;

namespace DoSelect.Application.Orders;

public sealed record OrderItemDto(
    Guid PublicId,
    string SkuCodeSnapshot,
    string ProductNameSnapshot,
    string SkuNameSnapshot,
    int Quantity,
    decimal FinalUnitPrice,
    decimal LineTotal,
    int ReturnableQuantity,
    int ReturnedQuantity);

public sealed record OrderAmountsDto(
    decimal MerchandiseSubtotal,
    decimal ItemDiscountTotal,
    decimal ShippingFee,
    decimal AssemblyFee,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RefundedAmount,
    string Currency);

/// <summary>Recipient fields are already the order's own delivery snapshot, not a live address book row — no masking beyond what OrderDto already omits (no phone/address here).</summary>
public sealed record OrderRecipientSummaryDto(
    string RecipientName,
    string ShippingMethodCode,
    string? StoreName);

public sealed record OrderDto(
    Guid PublicId,
    string OrderNumber,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    AssemblyStatus AssemblyStatus,
    OrderRefundStatus OrderRefundStatus,
    IReadOnlyList<OrderItemDto> Items,
    OrderRecipientSummaryDto Recipient,
    OrderAmountsDto Amounts,
    DateTime? PaymentDueAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    DateTime? ReturnRequestDeadlineUtc,
    IReadOnlyList<string> AvailableActions,
    byte[] RowVersion);

public sealed record OrderSummaryDto(
    Guid PublicId,
    string OrderNumber,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    int ItemCount,
    decimal GrandTotal,
    string Currency,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> AvailableActions);

public sealed record OrderQuery(int PageNumber = 1, int PageSize = 20);

/// <summary>
/// 退貨與退款政策.md 只定義訪客／會員取消「只能使用顧客可選理由」，沒有列出正式代碼表；這是本切片
/// 的範圍界定 — 先提供一組最小、顧客視角的理由碼，日誌中標記待與 alex／yinyin 對齊正式清單。
/// </summary>
public static class OrderCancellationReasonCodes
{
    public const string ChangedMind = "changed_mind";
    public const string OrderedByMistake = "ordered_by_mistake";
    public const string FoundBetterPrice = "found_better_price";
    public const string ShippingTooSlow = "shipping_too_slow";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
    [
        ChangedMind,
        OrderedByMistake,
        FoundBetterPrice,
        ShippingTooSlow,
        Other,
    ];
}

public sealed record CancelOrderRequest(
    [Required, StringLength(64, MinimumLength = 1)] string ReasonCode,
    [StringLength(500)] string? Note,
    byte[] OrderRowVersion);

public interface IOrderService
{
    Task<Application.Common.PageResult<OrderSummaryDto>> GetOrdersAsync(
        string memberUserId,
        OrderQuery query,
        CancellationToken cancellationToken);

    Task<OrderDto> GetOrderAsync(
        string memberUserId,
        Guid orderPublicId,
        CancellationToken cancellationToken);

    Task<OrderDto> CancelOrderAsync(
        string memberUserId,
        Guid orderPublicId,
        CancelOrderRequest request,
        CancellationToken cancellationToken);
}
