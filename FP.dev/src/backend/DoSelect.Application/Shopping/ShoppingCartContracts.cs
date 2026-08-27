using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Idempotency;

namespace DoSelect.Application.Shopping;

/// <summary>
/// Resolved caller identity for a cart request. Exactly one of the two is set — the Api
/// layer is responsible for producing this from either an authenticated member session or
/// the guest-cart-key header before calling into <see cref="ICartService"/>.
/// </summary>
public sealed record CartIdentity(string? MemberUserId, string? GuestCartKey);

public sealed record CartWarningDto(string Code, string Message);

/// <summary>
/// Placeholder shape pending yinyin's coupon integration — coupon application isn't wired
/// into Cart in this slice, so <see cref="CartDto.Coupon"/> is always null. Kept here (rather
/// than omitted) purely so the field exists in the wire shape the official contract
/// (API DTO與Schema契約.md) already promises downstream consumers.
/// </summary>
public sealed record CouponAppliedDto(string Code, decimal DiscountAmount);

public sealed record CartItemDto(
    Guid PublicId,
    Guid SkuPublicId,
    string SkuCode,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string Availability,
    bool PriceChanged,
    int MaxPurchasableQuantity,
    Guid? AssemblyGroupKey,
    byte[] RowVersion);

/// <summary>Matches API DTO與Schema契約.md's `amounts{...}` object exactly.</summary>
public sealed record CartAmountsDto(
    decimal Subtotal,
    decimal ItemDiscount,
    decimal CouponDiscount,
    decimal? ShippingEstimate,
    decimal AssemblyFee,
    decimal TotalEstimate,
    string Currency);

public sealed record CartDto(
    Guid PublicId,
    IReadOnlyList<CartItemDto> Items,
    CouponAppliedDto? Coupon,
    CartAmountsDto Amounts,
    IReadOnlyList<CartWarningDto> Warnings,
    byte[] RowVersion);

public sealed record AddCartItemRequest(
    Guid SkuPublicId,
    [Range(1, 99)] int Quantity,
    byte[]? CartRowVersion);

public sealed record UpdateCartItemRequest(
    [Range(1, 99)] int Quantity,
    byte[] ItemRowVersion,
    byte[] CartRowVersion);

public sealed record CartIssueDto(
    Guid? ItemPublicId,
    string Code,
    string Severity,
    IReadOnlyList<string> AvailableActions);

public sealed record CartValidationDto(
    CartDto Cart,
    bool IsCheckoutReady,
    IReadOnlyList<CartIssueDto> Issues,
    DateTime ValidatedAtUtc);

public sealed record CartMergeConflictDto(
    Guid GuestItemPublicId,
    Guid SkuPublicId,
    string Reason,
    int AcceptedQuantity);

public sealed record CartMergeRequest(
    [Required, StringLength(256, MinimumLength = 32)] string GuestCartKey,
    [Required] string Strategy,
    [Required, StringLength(128, MinimumLength = 8)] string IdempotencyKey);

public sealed record CartMergeResultDto(CartDto Cart, IReadOnlyList<CartMergeConflictDto> Conflicts);

/// <summary>One SKU's per-physical-unit quantity for a build-derived assembly group (see <see cref="ICartService.AddAssemblyGroupsAsync"/>).</summary>
public sealed record AssemblyGroupItemInput(Guid SkuPublicId, int Quantity);

public interface ICartService
{
    Task<CartDto> GetCartAsync(CartIdentity identity, CancellationToken cancellationToken);

    Task<CartDto> AddItemAsync(
        CartIdentity identity,
        AddCartItemRequest request,
        CancellationToken cancellationToken);

    Task<CartDto> UpdateItemQuantityAsync(
        CartIdentity identity,
        Guid itemPublicId,
        UpdateCartItemRequest request,
        CancellationToken cancellationToken);

    Task<CartDto> RemoveItemAsync(
        CartIdentity identity,
        Guid itemPublicId,
        byte[] itemRowVersion,
        CancellationToken cancellationToken);

    Task<CartValidationDto> RevalidateAsync(CartIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the full execution result (not just the body) because a whole-merge rejection
    /// (PR #28 round-3 ruling on the 100-item cap) must surface as HTTP 409, not 200 — the
    /// caller needs <see cref="IdempotencyExecutionResult{T}.StatusCode"/> to know which.
    /// </summary>
    Task<IdempotencyExecutionResult<CartMergeResultDto>> MergeAsync(
        string memberUserId,
        CartMergeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// UC-BUILD-01 加入購物車: creates <paramref name="unitCount"/> physical builds from
    /// <paramref name="perUnitItems"/> — one new <c>AssemblyGroupKey</c> per unit, each group's
    /// rows carrying the per-unit (not multiplied) quantity, matching Terry-商品庫存物流組裝與
    /// 報表最終Schema.md's "AssemblyGroupKey 仍以「一台主機」為單位分組，購買 N 台即產生 N 組不同
    /// 的 AssemblyGroupKey". Every row is a brand-new insert (assembly groups are never merged
    /// into existing cart rows, mirroring <c>MergeAsync</c>'s guest-item handling) — callers are
    /// expected to have already validated Sku status/price/stock/compatibility themselves, since
    /// this is an internal cross-slice contract, not a public HTTP one.
    /// </summary>
    Task<CartDto> AddAssemblyGroupsAsync(
        CartIdentity identity,
        IReadOnlyList<AssemblyGroupItemInput> perUnitItems,
        int unitCount,
        CancellationToken cancellationToken);
}
