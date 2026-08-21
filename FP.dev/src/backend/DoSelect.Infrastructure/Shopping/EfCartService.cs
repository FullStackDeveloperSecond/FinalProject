using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Idempotency;
using DoSelect.Domain.Shopping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shopping;

public sealed class EfCartService : ICartService
{
    /// <summary>
    /// No spec doc defines a cart TTL for either guest or member carts (checked the data
    /// dictionary, the final schema doc, and the Hangfire background-job design). 30 days from
    /// the *last successful mutation* (not creation) is 組長's ruling on PR #28's review —
    /// every Add/Update/Remove/Merge extends the deadline via <see cref="Cart.ExtendExpiry"/>.
    /// </summary>
    private static readonly TimeSpan CartLifetime = TimeSpan.FromDays(30);

    /// <summary>Stable command name for 資料一致性、Outbox與冪等設計.md's IdempotencyRecord.Operation.</summary>
    private const string CartMergeOperation = "cart.merge";

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

        cart.ExtendExpiry(now.Add(CartLifetime), now);

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
        cart.ExtendExpiry(now.Add(CartLifetime), now);

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
        cart.ExtendExpiry(now.Add(CartLifetime), now);

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
    /// Merges a guest cart into the caller's member cart on login (UC-CART-02). Idempotency is
    /// enforced up front by <see cref="ReserveIdempotencySlotAsync"/> per 資料一致性、Outbox與
    /// 冪等設計.md's IdempotencyRecord design: a replay with the same Key and the same request
    /// payload returns the original cached result without re-merging anything; the same Key
    /// with a different payload, or a genuinely concurrent duplicate, gets
    /// idempotency_payload_conflict instead of silently racing to convert the same guest cart
    /// twice.
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

        // Reserve the idempotency slot *before* doing any merge work: a genuinely concurrent
        // duplicate request (same Key) loses the race to INSERT this row and never executes the
        // merge logic at all, rather than both racing to convert the same guest cart.
        var actorScopeHash = HashUtf8($"user:{memberUserId}");
        var requestHash = HashUtf8($"{request.GuestCartKey} {request.Strategy}");
        var reservation = await ReserveIdempotencySlotAsync(
            actorScopeHash, CartMergeOperation, request.IdempotencyKey, requestHash, now, cancellationToken);
        if (reservation.CachedResult is not null)
        {
            return reservation.CachedResult;
        }

        var memberCart = await ResolveOrCreateCartAsync(new CartIdentity(memberUserId, null), now, cancellationToken);

        var guestHash = HashGuestCartKey(request.GuestCartKey);
        var guestCart = await _dbContext.Carts.FirstOrDefaultAsync(
            candidate => candidate.GuestCartKeyHash == guestHash &&
                candidate.Status == CartStatus.Active &&
                candidate.ExpiresAtUtc > now,
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

                        // UC-CART-02: "合併數量超過購買上限或可售庫存，保留項目並標記衝突，不自動截
                        // 斷" — CartItem's own domain invariant hard-caps a row at 99, so there is no
                        // legal row value that honestly represents "110". Silently storing
                        // Math.Min(combined, 99) used to pass a misleading number to neither side's
                        // original quantity and permanently drop the remainder once the guest cart
                        // converted. Leaving the member's existing quantity untouched and reporting
                        // the conflict (front-end already knows the guest cart's contents from
                        // before merge — it fetched them pre-login) is honest instead: nothing this
                        // merge decided for the shopper without asking.
                        if (combinedQuantity > 99)
                        {
                            landedItem = existingMemberItem;
                            conflicts.Add(new CartMergeConflictDto(
                                guestItem.PublicId,
                                sku.PublicId,
                                ShoppingWriteException.ErrorCodes.CartQuantityExceeded,
                                existingMemberItem.Quantity));
                            continue;
                        }

                        existingMemberItem.ChangeQuantity(combinedQuantity, now);
                        landedItem = existingMemberItem;
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

        memberCart.ExtendExpiry(now.Add(CartLifetime), now);

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        var cartDto = await MapCartAsync(memberCart, cancellationToken);
        var result = new CartMergeResultDto(cartDto, conflicts);

        reservation.Record!.Complete(responseStatusCode: 200, JsonSerializer.Serialize(result), now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    private readonly record struct IdempotencyReservation(IdempotencyRecord? Record, CartMergeResultDto? CachedResult);

    /// <summary>
    /// Races to INSERT a Processing <see cref="IdempotencyRecord"/> for (actorScopeHash,
    /// operation, key). On success, the caller owns this reservation and must call
    /// <see cref="IdempotencyRecord.Complete"/> then save once the command actually runs. On a
    /// unique-index collision, resolves per 資料一致性、Outbox與冪等設計.md: same RequestHash
    /// replays the cached result; different hash or a still-Processing sibling both surface as
    /// idempotency_payload_conflict (the error-code catalog doesn't define a distinct "retry
    /// shortly" code, so the "still processing" case reuses it with a message that says so).
    /// </summary>
    private async Task<IdempotencyReservation> ReserveIdempotencySlotAsync(
        byte[] actorScopeHash,
        string operation,
        string key,
        byte[] requestHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var record = new IdempotencyRecord(actorScopeHash, operation, key, requestHash, now.AddHours(24), now);
        _dbContext.IdempotencyRecords.Add(record);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new IdempotencyReservation(record, null);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(record).State = EntityState.Detached;

            var existing = await _dbContext.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.ActorScopeHash == actorScopeHash &&
                    candidate.Operation == operation &&
                    candidate.Key == key,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            if (!existing.RequestHash.AsSpan().SequenceEqual(requestHash))
            {
                throw new ShoppingWriteException(
                    ShoppingWriteException.ErrorCodes.IdempotencyPayloadConflict,
                    "This idempotency key was already used with a different request payload.");
            }

            if (existing.Status != IdempotencyStatus.Succeeded || existing.ResponseSummary is null)
            {
                throw new ShoppingWriteException(
                    ShoppingWriteException.ErrorCodes.IdempotencyPayloadConflict,
                    "An identical request is already being processed. Retry shortly.");
            }

            var cached = JsonSerializer.Deserialize<CartMergeResultDto>(existing.ResponseSummary)!;
            return new IdempotencyReservation(null, cached);
        }
    }

    private static byte[] HashUtf8(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

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
                now,
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
                now,
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
    ///
    /// Also resolves 組長's expiry ruling here: a row that's still Status=Active but past its
    /// ExpiresAtUtc is not usable — it's flipped to Expired (freeing the filtered-unique-index
    /// slot on OwnerUserId/GuestCartKeyHash) and a fresh cart is created in its place, exactly
    /// like the "no Active cart yet" path.
    /// </summary>
    private async Task<Cart> GetOrCreateCartAsync(
        Func<Task<Cart?>> findExisting,
        Func<Cart> createNew,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await findExisting();
        if (existing is not null)
        {
            if (existing.ExpiresAtUtc > now)
            {
                return existing;
            }

            existing.ChangeStatus(CartStatus.Expired, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
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
            if (concurrentlyCreated is null || concurrentlyCreated.ExpiresAtUtc <= now)
            {
                throw;
            }

            return concurrentlyCreated;
        }
    }

    /// <summary>
    /// Batch-loads every Sku/active-SalePrice/InventoryBalance the cart's items reference in
    /// 3 queries total instead of one per item — CartDto allows up to 100 items, so the old
    /// per-item pattern could round-trip ~301 times for a single GET /cart.
    /// </summary>
    private async Task<(List<CartItemDto> Items, List<CartIssueDto> Issues)> BuildItemsAsync(
        Cart cart,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cartItems = await _dbContext.CartItems.AsNoTracking()
            .Where(item => item.CartId == cart.Id)
            .ToListAsync(cancellationToken);

        var skuIds = cartItems.Select(item => item.SkuId).Distinct().ToArray();

        var skus = await _dbContext.Skus.AsNoTracking()
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);

        var effectivePricesBySkuId = await _dbContext.SalePrices.AsNoTracking()
            .Where(salePrice => skuIds.Contains(salePrice.SkuId) &&
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= now &&
                salePrice.EndsAtUtc > now)
            .GroupBy(salePrice => salePrice.SkuId)
            .Select(group => new
            {
                SkuId = group.Key,
                Price = group.OrderByDescending(salePrice => salePrice.StartsAtUtc).First().Price,
            })
            .ToDictionaryAsync(row => row.SkuId, row => row.Price, cancellationToken);

        var availableQuantitiesBySkuId = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, balance => balance.AvailableQuantity, cancellationToken);

        var items = new List<CartItemDto>();
        var issues = new List<CartIssueDto>();

        foreach (var cartItem in cartItems)
        {
            var sku = skus[cartItem.SkuId];
            var effectivePrice = effectivePricesBySkuId.GetValueOrDefault(sku.Id, sku.ListPrice);
            var availableQuantity = availableQuantitiesBySkuId.TryGetValue(sku.Id, out var quantity)
                ? quantity
                : (int?)null;
            var concern = GetAvailabilityConcern(sku, cartItem.Quantity, availableQuantity);

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
                PriceChanged: false, // No price-at-add snapshot column exists yet (see PR description / tracking issue).
                concern.MaxPurchasableQuantity,
                cartItem.AssemblyGroupKey,
                cartItem.RowVersion));
        }

        return (items, issues);
    }

    private readonly record struct AvailabilityConcern(string Availability, string? IssueCode, int MaxPurchasableQuantity);

    /// <summary>
    /// A missing InventoryBalance row is treated the same as zero stock (not "unknown, don't
    /// block") — mirrors PR #22's public search, which excludes a SKU with no balance row from
    /// purchasable results. Previously this returned MaxPurchasableQuantity=99 and no issue,
    /// so a SKU the inventory slice hasn't populated yet was silently checkout-ready.
    /// </summary>
    private static AvailabilityConcern GetAvailabilityConcern(Sku sku, int quantity, int? availableQuantity)
    {
        if (sku.Status != SkuStatus.Published)
        {
            return new AvailabilityConcern("unavailable", ShoppingWriteException.ErrorCodes.SkuUnavailable, 0);
        }

        if (availableQuantity is null || availableQuantity.Value <= 0)
        {
            return new AvailabilityConcern(
                "insufficient_stock",
                ShoppingWriteException.ErrorCodes.CartItemRequiresAttention,
                0);
        }

        var maxPurchasableQuantity = Math.Clamp(availableQuantity.Value, 0, 99);
        if (quantity > availableQuantity.Value)
        {
            return new AvailabilityConcern(
                "insufficient_stock",
                ShoppingWriteException.ErrorCodes.CartItemRequiresAttention,
                maxPurchasableQuantity);
        }

        return new AvailabilityConcern("available", null, maxPurchasableQuantity);
    }

    /// <summary>Single-item variant of <see cref="GetAvailabilityConcern"/> for call sites (Merge) that haven't already batch-loaded balances.</summary>
    private async Task<AvailabilityConcern> GetAvailabilityConcernAsync(
        Sku sku,
        int quantity,
        CancellationToken cancellationToken)
    {
        var availableQuantity = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => balance.SkuId == sku.Id)
            .Select(balance => (int?)balance.AvailableQuantity)
            .FirstOrDefaultAsync(cancellationToken);

        return GetAvailabilityConcern(sku, quantity, availableQuantity);
    }

    private async Task<CartDto> MapCartAsync(Cart cart, CancellationToken cancellationToken)
    {
        var (items, _) = await BuildItemsAsync(cart, DateTime.UtcNow, cancellationToken);
        return BuildCartDto(cart, items);
    }

    private static CartDto BuildCartDto(Cart cart, List<CartItemDto> items)
    {
        var subtotal = items.Sum(item => item.LineTotal);

        var amounts = new CartAmountsDto(
            Subtotal: subtotal,
            ItemDiscount: 0m,
            CouponDiscount: 0m,
            ShippingEstimate: null,
            AssemblyFee: 0m,
            TotalEstimate: subtotal,
            Currency: "TWD");

        return new CartDto(
            cart.PublicId,
            items,
            Coupon: null, // Coupon logic belongs to yinyin's slice; see ShoppingCartContracts.cs's CouponAppliedDto remarks.
            amounts,
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
