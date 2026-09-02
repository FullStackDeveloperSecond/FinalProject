using System.ComponentModel.DataAnnotations;
using System.Net;
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

/// <summary>Alex 於 PR #43 review 裁定（2026-08-27，B1）的顧客自助取消正式原因碼。</summary>
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

/// <summary>由受信任的 API 邊界建立，不接受客戶端直接指定。</summary>
public sealed record OrderCancellationAuditContext(
    string CorrelationId,
    string TraceId,
    IPAddress? RemoteIpAddress);

/// <summary>
/// 由 API 的受信任驗證邊界建立。會員身分來自 Member Cookie；訪客身分來自已經過
/// <see cref="GuestOrderAccessScopeAuthorizer"/> 限單核對的 GuestOrderAccess Cookie。
/// </summary>
public abstract record OrderActor
{
    private OrderActor()
    {
    }

    public sealed record Member(string UserId) : OrderActor;

    public sealed record Guest(Guid TokenPublicId) : OrderActor;
}

public interface IOrderService
{
    Task<Application.Common.PageResult<OrderSummaryDto>> GetOrdersAsync(
        string memberUserId,
        OrderQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// 這個會員是不是這張訂單的擁有者。
    /// </summary>
    /// <remarks>
    /// 授權解析要在<b>寫入之前</b>知道這件事：會員不是擁有者時，仍要讓同一個瀏覽器裡
    /// 有效的 Guest token 證明權限（alex 2026-09-01 Issue #86 C1）。把判斷留到寫入內部
    /// 才做就太晚了 —— 那時已經在冪等交易裡，沒辦法再換一條授權路徑重試。
    /// <para>
    /// 訂單不存在時回 <c>false</c>：呼叫端本來就把「不存在」與「不是你的」折成同一個 404。
    /// </para>
    /// </remarks>
    Task<bool> IsMemberOwnerAsync(
        string memberUserId,
        Guid orderPublicId,
        CancellationToken cancellationToken);

    Task<OrderDto> GetOrderAsync(
        OrderActor actor,
        Guid orderPublicId,
        CancellationToken cancellationToken);

    Task<OrderDto> CancelOrderAsync(
        OrderActor actor,
        Guid orderPublicId,
        CancelOrderRequest request,
        OrderCancellationAuditContext auditContext,
        CancellationToken cancellationToken);
}
