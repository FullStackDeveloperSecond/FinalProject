using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Shopping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shopping;

public sealed class EfCartService : ICartService
{
    /// <summary>
    /// No spec doc defines a cart TTL for either guest or member carts (checked the data
    /// dictionary, the final schema doc, and the Hangfire background-job design). 30 days
    /// is a reasonable interim default, called out in the PR description as a decision that
    /// still needs sign-off rather than a silently assumed number.
    /// </summary>
    private static readonly TimeSpan CartLifetime = TimeSpan.FromDays(30);

    private readonly DoSelectDbContext _dbContext;

    public EfCartService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CartDto> GetCartAsync(CartIdentity identity, CancellationToken cancellationToken)
    {
        var cart = await ResolveOrCreateCartAsync(identity, DateTime.UtcNow, cancellationToken);
        return await MapCartAsync(cart, cancellationToken);
    }

    public async Task<CartDto> AddItemAsync(
        CartIdentity identity,
        AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var cart = await ResolveOrCreateCartAsync(identity, now, cancellationToken);

        var sku = await _dbContext.Skus
            .FirstOrDefaultAsync(candidate => candidate.PublicId == request.SkuPublicId, cancellationToken);
        if (sku is null)
        {
            throw new ShoppingWriteException(
                ShoppingWriteException.ErrorCodes.ResourceNotFound,
                $"SKU '{request.SkuPublicId}' was not found.");
        }

        if (sku.Status != SkuStatus.Published)
        {
            throw new ShoppingWriteException(
                ShoppingWriteException.ErrorCodes.SkuUnavailable,
                $"SKU '{request.SkuPublicId}' is not available for purchase.");
        }

        if (request.CartRowVersion is not null)
        {
            _dbContext.Entry(cart).Property(candidate => candidate.RowVersion).OriginalValue = request.CartRowVersion;
        }

        var existingItem = await _dbContext.CartItems.FirstOrDefaultAsync(
            item => item.CartId == cart.Id && item.SkuId == sku.Id && item.AssemblyGroupKey == null,
            cancellationToken);

        if (existingItem is not null)
        {
            var combinedQuantity = existingItem.Quantity + request.Quantity;
            if (combinedQuantity > 99)
            {
                throw new ShoppingWriteException(
                    ShoppingWriteException.ErrorCodes.CartQuantityExceeded,
                    "Combined quantity would exceed the maximum of 99 per cart item.");
            }

            existingItem.ChangeQuantity(combinedQuantity, now);
        }
        else
        {
            var item = new CartItem(Guid.CreateVersion7(), cart.Id, sku.Id, request.Quantity, null, now);
            _dbContext.CartItems.Add(item);
        }

        cart.Touch(now);

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        return await MapCartAsync(cart, cancellationToken);
    }

    public async Task<CartDto> UpdateItemQuantityAsync(
        CartIdentity identity,
        Guid itemPublicId,
        UpdateCartItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var cart = await ResolveOrCreateCartAsync(identity, now, cancellationToken);

        var item = await _dbContext.CartItems.FirstOrDefaultAsync(
            candidate => candidate.PublicId == itemPublicId && candidate.CartId == cart.Id,
            cancellationToken);
        if (item is null)
        {
            throw new ShoppingWriteException(
                ShoppingWriteException.ErrorCodes.ResourceNotFound,
                $"Cart item '{itemPublicId}' was not found.");
        }

        _dbContext.Entry(cart).Property(candidate => candidate.RowVersion).OriginalValue = request.CartRowVersion;
        _dbContext.Entry(item).Property(candidate => candidate.RowVersion).OriginalValue = request.ItemRowVersion;

        item.ChangeQuantity(request.Quantity, now);
        cart.Touch(now);

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        return await MapCartAsync(cart, cancellationToken);
    }

    public async Task<CartDto> RemoveItemAsync(
        CartIdentity identity,
        Guid itemPublicId,
        byte[] itemRowVersion,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var cart = await ResolveOrCreateCartAsync(identity, now, cancellationToken);

        var item = await _dbContext.CartItems.FirstOrDefaultAsync(
            candidate => candidate.PublicId == itemPublicId && candidate.CartId == cart.Id,
            cancellationToken);
        if (item is null)
        {
            throw new ShoppingWriteException(
                ShoppingWriteException.ErrorCodes.ResourceNotFound,
                $"Cart item '{itemPublicId}' was not found.");
        }

        _dbContext.Entry(item).Property(candidate => candidate.RowVersion).OriginalValue = itemRowVersion;
        _dbContext.CartItems.Remove(item);
        cart.Touch(now);

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        return await MapCartAsync(cart, cancellationToken);
    }

    public async Task<CartValidationDto> RevalidateAsync(CartIdentity identity, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var cart = await ResolveOrCreateCartAsync(identity, now, cancellationToken);

        var (items, issues) = await BuildItemsAsync(cart, now, cancellationToken);
        var cartDto = BuildCartDto(cart, items);

        return new CartValidationDto(cartDto, issues.Count == 0, issues, now);
    }

    /// <summary>
    /// Merges a guest cart into the caller's member cart on login (UC-CART-02). Idempotency
    /// is achieved through the guest cart's own status transition rather than a persisted
    /// idempotency-key column (see the PR description for why): once a guest cart is
    /// converted, a replayed merge with the same guest key simply finds no Active guest cart
    /// and returns the member cart unchanged — the one gap this doesn't cover is
    /// distinguishing "identical replay" from "same key reused with a different payload"
    /// (idempotency_payload_conflict is never raised this slice), which is an accepted,
    /// documented trade-off, not an oversight.
    /// </summary>
    public async Task<CartMergeResultDto> MergeAsync(
        string memberUserId,
        CartMergeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Strategy != "mergeAndReportConflicts")
        {
            throw new ShoppingWriteException(
                ShoppingWriteException.ErrorCodes.ValidationFailed,
                $"Unsupported merge strategy '{request.Strategy}'.");
        }

        var now = DateTime.UtcNow;
        var memberCart = await ResolveOrCreateCartAsync(new CartIdentity(memberUserId, null), now, cancellationToken);

        var guestHash = HashGuestCartKey(request.GuestCartKey);
        var guestCart = await _dbContext.Carts.FirstOrDefaultAsync(
            candidate => candidate.GuestCartKeyHash == guestHash && candidate.Status == CartStatus.Active,
            cancellationToken);

        var conflicts = new List<CartMergeConflictDto>();

        if (guestCart is not null)
        {
            var guestItems = await _dbContext.CartItems
                .Where(item => item.CartId == guestCart.Id)
                .ToListAsync(cancellationToken);
            var memberItems = await _dbContext.CartItems
                .Where(item => item.CartId == memberCart.Id)
                .ToListAsync(cancellationToken);

            foreach (var guestItem in guestItems)
            {
                var sku = await _dbContext.Skus.AsNoTracking()
                    .FirstAsync(candidate => candidate.Id == guestItem.SkuId, cancellationToken);
                CartItem landedItem;

                if (guestItem.AssemblyGroupKey is not null)
                {
                    // Assembly groups are never combined with anything, even a coincidentally
                    // identical AssemblyGroupKey — each guest group lands as its own new row.
                    landedItem = new CartItem(
                        Guid.CreateVersion7(),
                        memberCart.Id,
                        guestItem.SkuId,
                        guestItem.Quantity,
                        guestItem.AssemblyGroupKey,
                        now);
                    _dbContext.CartItems.Add(landedItem);
                }
                else
                {
                    var existingMemberItem = memberItems.FirstOrDefault(
                        candidate => candidate.SkuId == guestItem.SkuId && candidate.AssemblyGroupKey == null);

                    if (existingMemberItem is not null)
                    {
                        var combinedQuantity = existingMemberItem.Quantity + guestItem.Quantity;
                        var storedQuantity = Math.Min(combinedQuantity, 99);
                        existingMemberItem.ChangeQuantity(storedQuantity, now);
                        landedItem = existingMemberItem;

                        if (combinedQuantity > 99)
                        {
                            conflicts.Add(new CartMergeConflictDto(
                                guestItem.PublicId,
                                sku.PublicId,
                                ShoppingWriteException.ErrorCodes.CartQuantityExceeded,
                                storedQuantity));
                        }
                    }
                    else
                    {
                        landedItem = new CartItem(
                            Guid.CreateVersion7(),
                            memberCart.Id,
                            guestItem.SkuId,
                            guestItem.Quantity,
                            null,
                            now);
                        _dbContext.CartItems.Add(landedItem);
                        memberItems.Add(landedItem);
                    }
                }

                // Cart never reserves stock, so an insufficient-stock item is still fully
                // accepted into the cart — only flagged, never quantity-clamped for this reason.
                var concern = await GetAvailabilityConcernAsync(sku, landedItem.Quantity, cancellationToken);
                if (concern.IssueCode is not null)
                {
                    conflicts.Add(new CartMergeConflictDto(
                        guestItem.PublicId,
                        sku.PublicId,
                        concern.IssueCode,
                        landedItem.Quantity));
                }
            }

            guestCart.ChangeStatus(CartStatus.Converted, now);
        }

        memberCart.Touch(now);

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        var cartDto = await MapCartAsync(memberCart, cancellationToken);
        return new CartMergeResultDto(cartDto, conflicts);
    }

    private async Task<Cart> ResolveOrCreateCartAsync(
        CartIdentity identity,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (identity.MemberUserId is { } memberUserId)
        {
            return await GetOrCreateCartAsync(
                () => _dbContext.Carts.FirstOrDefaultAsync(
                    candidate => candidate.OwnerUserId == memberUserId && candidate.Status == CartStatus.Active,
                    cancellationToken),
                () => Cart.CreateForMember(Guid.CreateVersion7(), memberUserId, now.Add(CartLifetime), now),
                cancellationToken);
        }

        if (identity.GuestCartKey is { } guestCartKey)
        {
            var hash = HashGuestCartKey(guestCartKey);
            return await GetOrCreateCartAsync(
                () => _dbContext.Carts.FirstOrDefaultAsync(
                    candidate => candidate.GuestCartKeyHash == hash && candidate.Status == CartStatus.Active,
                    cancellationToken),
                () => Cart.CreateForGuest(Guid.CreateVersion7(), hash, now.Add(CartLifetime), now),
                cancellationToken);
        }

        throw new ShoppingWriteException(
            ShoppingWriteException.ErrorCodes.ValidationFailed,
            "A member session or guest cart key is required.");
    }

    /// <summary>
    /// A plain check-then-insert races: CartPage.vue fires GET /cart and POST
    /// /actions/revalidate concurrently on mount, so for a brand-new guest key both requests
    /// can see "no Active cart" before either commits its INSERT, and the second one dies on
    /// UX_Carts_OwnerUserId_Active / UX_Carts_GuestCartKeyHash_Active (caught live in manual
    /// browser verification — DbUpdateException wrapping a SqlException 2601). Retrying the
    /// same lookup after a failed insert picks up whichever request won the race instead of
    /// surfacing a 500 for what is actually a successful, idempotent "get the cart" outcome.
    /// </summary>
    private async Task<Cart> GetOrCreateCartAsync(
        Func<Task<Cart?>> findExisting,
        Func<Cart> createNew,
        CancellationToken cancellationToken)
    {
        var existing = await findExisting();
        if (existing is not null)
        {
            return existing;
        }

        var cart = createNew();
        _dbContext.Carts.Add(cart);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return cart;
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(cart).State = EntityState.Detached;
            var concurrentlyCreated = await findExisting();
            if (concurrentlyCreated is null)
            {
                throw;
            }

            return concurrentlyCreated;
        }
    }

    private async Task<(List<CartItemDto> Items, List<CartIssueDto> Issues)> BuildItemsAsync(
        Cart cart,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cartItems = await _dbContext.CartItems.AsNoTracking()
            .Where(item => item.CartId == cart.Id)
            .ToListAsync(cancellationToken);

        var items = new List<CartItemDto>();
        var issues = new List<CartIssueDto>();

        foreach (var cartItem in cartItems)
        {
            var sku = await _dbContext.Skus.AsNoTracking()
                .FirstAsync(candidate => candidate.Id == cartItem.SkuId, cancellationToken);
            var effectivePrice = await GetEffectivePriceAsync(sku, now, cancellationToken);
            var concern = await GetAvailabilityConcernAsync(sku, cartItem.Quantity, cancellationToken);

            if (concern.IssueCode is not null)
            {
                issues.Add(new CartIssueDto(
                    cartItem.PublicId,
                    concern.IssueCode,
                    concern.IssueCode == ShoppingWriteException.ErrorCodes.SkuUnavailable ? "error" : "warning",
                    concern.IssueCode == ShoppingWriteException.ErrorCodes.SkuUnavailable
                        ? ["remove"]
                        : ["reduce-quantity", "remove"]));
            }

            items.Add(new CartItemDto(
                cartItem.PublicId,
                sku.PublicId,
                sku.SkuCode,
                sku.NameZhTw,
                cartItem.Quantity,
                effectivePrice,
                effectivePrice * cartItem.Quantity,
                concern.Availability,
                PriceChanged: false, // No price-at-add snapshot column exists yet (see PR description).
                concern.MaxPurchasableQuantity,
                cartItem.AssemblyGroupKey,
                CouponAllocatedDiscount: 0m, // Coupon logic belongs to yinyin's slice; see 回覆.md field alignment.
                cartItem.RowVersion));
        }

        return (items, issues);
    }

    private async Task<decimal> GetEffectivePriceAsync(Sku sku, DateTime now, CancellationToken cancellationToken)
    {
        var activeSalePrice = await _dbContext.SalePrices.AsNoTracking()
            .Where(salePrice => salePrice.SkuId == sku.Id &&
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= now &&
                salePrice.EndsAtUtc > now)
            .OrderByDescending(salePrice => salePrice.StartsAtUtc)
            .Select(salePrice => (decimal?)salePrice.Price)
            .FirstOrDefaultAsync(cancellationToken);

        return activeSalePrice ?? sku.ListPrice;
    }

    private readonly record struct AvailabilityConcern(string Availability, string? IssueCode, int MaxPurchasableQuantity);

    private async Task<AvailabilityConcern> GetAvailabilityConcernAsync(
        Sku sku,
        int quantity,
        CancellationToken cancellationToken)
    {
        // No InventoryBalance row exists yet until the inventory slice populates one —
        // treated as "unknown, don't block" rather than assuming zero stock.
        var availableQuantity = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => balance.SkuId == sku.Id)
            .Select(balance => (int?)balance.AvailableQuantity)
            .FirstOrDefaultAsync(cancellationToken);

        var maxPurchasableQuantity = availableQuantity.HasValue
            ? Math.Clamp(availableQuantity.Value, 0, 99)
            : 99;

        if (sku.Status != SkuStatus.Published)
        {
            return new AvailabilityConcern("unavailable", ShoppingWriteException.ErrorCodes.SkuUnavailable, maxPurchasableQuantity);
        }

        if (availableQuantity.HasValue && quantity > availableQuantity.Value)
        {
            return new AvailabilityConcern(
                "insufficient_stock",
                ShoppingWriteException.ErrorCodes.CartItemRequiresAttention,
                maxPurchasableQuantity);
        }

        return new AvailabilityConcern("available", null, maxPurchasableQuantity);
    }

    private async Task<CartDto> MapCartAsync(Cart cart, CancellationToken cancellationToken)
    {
        var (items, _) = await BuildItemsAsync(cart, DateTime.UtcNow, cancellationToken);
        return BuildCartDto(cart, items);
    }

    private static CartDto BuildCartDto(Cart cart, List<CartItemDto> items)
    {
        var subtotal = items.Sum(item => item.LineTotal);

        return new CartDto(
            cart.PublicId,
            items,
            subtotal,
            ItemDiscount: 0m,
            CouponCode: null,
            CouponDiscountAmount: 0m,
            CouponEligibleSubtotal: 0m,
            IsFreeShipping: false,
            IsAssemblyFreeShipping: false,
            ShippingEstimate: null,
            AssemblyFee: 0m,
            TotalEstimate: subtotal,
            Currency: "TWD",
            Warnings: [],
            cart.RowVersion);
    }

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShoppingWriteException(
                ShoppingWriteException.ErrorCodes.ConcurrencyConflict,
                "The cart was updated by someone else. Reload and try again.");
        }
    }

    private static byte[] HashGuestCartKey(string guestCartKey) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(guestCartKey));
}
