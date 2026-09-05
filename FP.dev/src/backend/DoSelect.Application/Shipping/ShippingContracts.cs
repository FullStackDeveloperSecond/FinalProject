using DoSelect.Application.Common;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Shipping;

/// <summary>
/// One selectable shipping method for a given cart, evaluated against that cart's current
/// contents (assembly items, subtotal) — not a static list of <c>ShippingMethod</c> rows.
/// Shape matches API DTO與Schema契約.md's ShippingOptionsDto.options[] exactly.
/// </summary>
public sealed record ShippingOptionDto(
    string MethodCode,
    string Name,
    decimal Fee,
    bool IsEligible,
    string? IneligibleReasonCode,
    decimal? FreeShippingThreshold,
    bool RequiresAddress,
    bool RequiresStore,
    IReadOnlyList<PaymentMethod> AllowedPaymentMethods);

public sealed record ShippingOptionsDto(
    Guid CartPublicId,
    IReadOnlyList<ShippingOptionDto> Options,
    DateTime EvaluatedAtUtc,
    byte[] CartRowVersion);

public sealed record ConvenienceStoreQuery(
    string? ProviderCode,
    string? City,
    string? District,
    string? Q,
    int PageNumber,
    int PageSize);

public sealed record ConvenienceStoreOptionDto(
    Guid PublicId,
    string ProviderCode,
    string StoreCode,
    string Name,
    string City,
    string District,
    string Address,
    bool IsDemoData);

/// <summary>
/// The authoritative go/no-go decision for cash-on-delivery on one cart, re-evaluated at the
/// moment it's asked (never cached from an earlier <see cref="ShippingOptionsDto"/> read) —
/// per 購物車、訂單、付款與物流.md's "COD Eligibility 由 Application Use Case 組合配送方式能力、
/// NT$20,000 上限、組裝與 SKU 預付旗標判斷". haru's checkout and yinyin's amount math call this
/// instead of re-deriving the rule themselves.
/// </summary>

public static class ShippingErrorCodes
{
    public const string CartNotFound = "cart_not_found";
    public const string ShippingMethodNotAllowed = "shipping_method_not_allowed";

    /// <summary>UC-ADM-SHIP-02 批次出貨（API 錯誤碼目錄第 144～149 列）。</summary>
    public const string ShippingBatchLimitExceeded = "shipping_batch_limit_exceeded";
    public const string ShippingOrderNotReady = "shipping_order_not_ready";
    public const string ShippingTrackingDuplicate = "shipping_tracking_duplicate";

    /// <summary>M-11 物流狀態命令：狀態機不允許的邊，或配送方式不允許的目標狀態（宅配／超取限制）。</summary>
    public const string ShippingStatusTransitionInvalid = "shipping_status_transition_invalid";
}

public interface IShippingOptionsService
{
    Task<ShippingOptionsDto> GetOptionsForCartAsync(
        CartIdentity identity,
        CancellationToken cancellationToken,
        string? couponCode = null);
}

public interface IConvenienceStoreQueryService
{
    Task<PageResult<ConvenienceStoreOptionDto>> ListAsync(
        ConvenienceStoreQuery query,
        CancellationToken cancellationToken);
}


