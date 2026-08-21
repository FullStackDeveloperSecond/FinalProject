namespace DoSelect.Application.Shopping;

/// <summary>
/// Resolved caller identity for a cart request. Exactly one of the two is set — the Api
/// layer is responsible for producing this from either an authenticated member session or
/// the guest-cart-key header before calling into <see cref="ICartService"/>.
/// </summary>
public sealed record CartIdentity(string? MemberUserId, string? GuestCartKey);

public sealed record CartWarningDto(string Code, string Message);

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
    decimal CouponAllocatedDiscount,
    byte[] RowVersion);

public sealed record CartDto(
    Guid PublicId,
    IReadOnlyList<CartItemDto> Items,
    decimal Subtotal,
    decimal ItemDiscount,
    string? CouponCode,
    decimal CouponDiscountAmount,
    decimal CouponEligibleSubtotal,
    bool IsFreeShipping,
    bool IsAssemblyFreeShipping,
    decimal? ShippingEstimate,
    decimal AssemblyFee,
    decimal TotalEstimate,
    string Currency,
    IReadOnlyList<CartWarningDto> Warnings,
    byte[] RowVersion);

public sealed record AddCartItemRequest(Guid SkuPublicId, int Quantity, byte[]? CartRowVersion);

public sealed record UpdateCartItemRequest(int Quantity, byte[] ItemRowVersion, byte[] CartRowVersion);

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

public sealed record CartMergeRequest(string GuestCartKey, string Strategy, string IdempotencyKey);

public sealed record CartMergeResultDto(CartDto Cart, IReadOnlyList<CartMergeConflictDto> Conflicts);

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

    Task<CartMergeResultDto> MergeAsync(
        string memberUserId,
        CartMergeRequest request,
        CancellationToken cancellationToken);
}
