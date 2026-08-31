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
/// 購物車試算套用成功的優惠券。
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="IsFreeShipping"/> 與 <paramref name="IsAssemblyFreeShipping"/> 不能省略：
/// <c>CouponCalculator</c> 對免運券回傳的 <c>DiscountAmount</c> **固定是 0**，
/// 真正的結果放在這兩個旗標上。只保留金額的話，四種券裡的一般免運與組裝免運會顯示成
/// 「套用成功但折扣 0 元、總額不變」，前端也沒有任何欄位可以判斷免運是否成立。
/// </para>
/// <para>
/// 購物車階段不一定有確切運費，因此免運券可以不改變 <c>totalEstimate</c>；
/// 但回應必須表達免運資格，不得把成功結果靜默變成 0 元折扣（DEC B1）。
/// </para>
/// </remarks>
public sealed record CouponAppliedDto(
    string Code,
    decimal DiscountAmount,
    bool IsFreeShipping,
    bool IsAssemblyFreeShipping);

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

    /// <summary>
    /// 組長 PR #29 round 7 review, P1（AUTO-DEC-015）: an assembly group's rows are immutable
    /// individually (<see cref="RemoveItemAsync"/>／<c>UpdateItemQuantityAsync</c> both reject a
    /// grouped item with <see cref="ShoppingWriteException.ErrorCodes.CartAssemblyItemImmutable"/>)
    /// but there was no atomic way to remove the whole group either — a member whose group became
    /// unavailable/insufficient-stock had no legal recovery path at all. Removes every
    /// <c>CartItem</c> row (<c>DoSelect.Domain.Shopping.CartItem</c>) sharing
    /// <paramref name="assemblyGroupKey"/> in
    /// one <c>SaveChangesAsync</c> call (EF Core's own single-transaction guarantee — no explicit
    /// <c>BeginTransactionAsync</c> needed), so a mid-way failure can never leave the group
    /// half-removed. Cart-level (not item-level) RowVersion, since a group spans multiple rows and
    /// there is no single item RowVersion that could represent it.
    ///
    /// 組長 PR #29 round 8 review, P2: removing a group does NOT resolve any
    /// <c>CartMergeConflict</c>. Conflicts only ever belong to ordinary items, so a group that
    /// merely shares a SKU with a conflicted ordinary item is not the member acting on that item;
    /// only that item's own Update／Remove clears it. See EfCartService for the full reasoning.
    /// </summary>
    Task<CartDto> RemoveAssemblyGroupAsync(
        CartIdentity identity,
        Guid assemblyGroupKey,
        byte[] cartRowVersion,
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
