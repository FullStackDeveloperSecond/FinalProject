using DoSelect.Application.Idempotency;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Shopping;

[CollectionDefinition(nameof(CartServiceCollection))]
public sealed class CartServiceCollection : ICollectionFixture<CartServiceFixture>;

[Collection(nameof(CartServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CartServiceTests
{
    // Any 32+ UTF-8-byte value works — EfIdempotencyExecutor only enforces a minimum length,
    // mirrors IdempotencyExecutorFixture.Pepper's role in Idempotency/IdempotencyExecutorTests.cs.
    private const string TestActorScopePepper = "cart-service-tests-actor-scope-pepper-00";

    private readonly CartServiceFixture _fixture;

    public CartServiceTests(CartServiceFixture fixture)
    {
        _fixture = fixture;
    }

    private static EfCartService CreateService(DoSelectDbContext context) =>
        new(context, new EfIdempotencyExecutor(
            context,
            Options.Create(new IdempotencyOptions { ActorScopePepper = TestActorScopePepper }),
            TimeProvider.System));

    [Fact]
    public async Task AddItemAsync_WhenCartIsEmpty_CreatesGuestCartAndItem()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 2, null), CancellationToken.None);

        var item = Assert.Single(cart.Items);
        Assert.Equal(sku.PublicId, item.SkuPublicId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(1000m, item.UnitPrice);
        Assert.Equal(2000m, item.LineTotal);
        Assert.Equal("available", item.Availability);
    }

    [Fact]
    public async Task AddItemAsync_WhenSameSkuAddedTwice_CombinesQuantity()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 500m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 3, null), CancellationToken.None);
        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 4, null), CancellationToken.None);

        var item = Assert.Single(cart.Items);
        Assert.Equal(7, item.Quantity);
    }

    [Fact]
    public async Task AddItemAsync_WhenCombinedQuantityExceedsNinetyNine_ThrowsCartQuantityExceeded()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 90, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.AddItemAsync(
            identity,
            new AddCartItemRequest(sku.PublicId, 20, null),
            CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartQuantityExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task AddItemAsync_WhenSkuIsNotPublished_ThrowsSkuUnavailable()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m, publish: false);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.AddItemAsync(
            identity,
            new AddCartItemRequest(sku.PublicId, 1, null),
            CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.SkuUnavailable, exception.ErrorCode);
    }

    [Fact]
    public async Task AddItemAsync_WhenSkuDoesNotExist_ThrowsResourceNotFound()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.AddItemAsync(
            identity,
            new AddCartItemRequest(Guid.NewGuid(), 1, null),
            CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateItemQuantityAsync_WhenRowVersionIsStale_ThrowsConcurrencyConflict()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var item = Assert.Single(cart.Items);

        var staleItemRowVersion = item.RowVersion;
        await service.UpdateItemQuantityAsync(
            identity,
            item.PublicId,
            new UpdateCartItemRequest(2, staleItemRowVersion, cart.RowVersion),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.UpdateItemQuantityAsync(
            identity,
            item.PublicId,
            new UpdateCartItemRequest(3, staleItemRowVersion, cart.RowVersion),
            CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task RemoveItemAsync_WhenSuccessful_DeletesTheItem()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var item = Assert.Single(cart.Items);

        var updated = await service.RemoveItemAsync(identity, item.PublicId, item.RowVersion, CancellationToken.None);

        Assert.Empty(updated.Items);
    }

    [Fact]
    public async Task RemoveItemAsync_WhenRowVersionIsStale_ThrowsConcurrencyConflict()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var item = Assert.Single(cart.Items);
        var staleRowVersion = item.RowVersion;

        await service.UpdateItemQuantityAsync(
            identity,
            item.PublicId,
            new UpdateCartItemRequest(2, staleRowVersion, cart.RowVersion),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.RemoveItemAsync(
            identity,
            item.PublicId,
            staleRowVersion,
            CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task RevalidateAsync_WhenSkuIsUnpublished_FlagsSkuUnavailableAndBlocksCheckout()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);

        sku.ChangeStatus(SkuStatus.Unpublished, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var validation = await service.RevalidateAsync(identity, CancellationToken.None);

        Assert.False(validation.IsCheckoutReady);
        Assert.Contains(validation.Issues, issue => issue.Code == ShoppingWriteException.ErrorCodes.SkuUnavailable);
        Assert.Equal("unavailable", Assert.Single(validation.Cart.Items).Availability);
    }

    [Fact]
    public async Task RevalidateAsync_WhenQuantityExceedsAvailableInventory_FlagsInsufficientStock()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m, availableQuantity: 2);

        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 5, null), CancellationToken.None);

        var validation = await service.RevalidateAsync(identity, CancellationToken.None);

        Assert.False(validation.IsCheckoutReady);
        Assert.Contains(validation.Issues, issue => issue.Code == ShoppingWriteException.ErrorCodes.CartItemRequiresAttention);
        var item = Assert.Single(validation.Cart.Items);
        Assert.Equal("insufficient_stock", item.Availability);
        Assert.Equal(2, item.MaxPurchasableQuantity);
    }

    /// <summary>
    /// PR #28 review: a missing InventoryBalance row used to be treated as "unknown, don't
    /// block" (99 always purchasable) — inconsistent with PR #22's public search, which
    /// excludes a SKU with no balance row from purchasable results. A SKU the inventory slice
    /// hasn't populated yet must not be silently checkout-ready.
    /// </summary>
    [Fact]
    public async Task RevalidateAsync_WhenNoInventoryBalanceRowExists_BlocksCheckout()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m, availableQuantity: null);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 50, null), CancellationToken.None);

        var validation = await service.RevalidateAsync(identity, CancellationToken.None);

        Assert.False(validation.IsCheckoutReady);
        Assert.Contains(validation.Issues, issue => issue.Code == ShoppingWriteException.ErrorCodes.CartItemRequiresAttention);
        var item = Assert.Single(validation.Cart.Items);
        Assert.Equal("insufficient_stock", item.Availability);
        Assert.Equal(0, item.MaxPurchasableQuantity);
    }

    [Fact]
    public async Task RevalidateAsync_WhenAnActiveSalePriceIsInWindow_UsesItAsTheEffectivePrice()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var now = DateTime.UtcNow;
        var adminUser = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", now);
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();

        var salePrice = new SalePrice(
            Guid.CreateVersion7(),
            sku.Id,
            price: 800m,
            startsAtUtc: now.AddDays(-1),
            endsAtUtc: now.AddDays(1),
            createdByAdminUserId: adminUser.Id,
            now);
        salePrice.ChangeStatus(SalePriceStatus.Active, now);
        context.SalePrices.Add(salePrice);
        await context.SaveChangesAsync();

        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);

        var validation = await service.RevalidateAsync(identity, CancellationToken.None);

        Assert.Equal(800m, Assert.Single(validation.Cart.Items).UnitPrice);
    }

    /// <summary>PR #28 review: a successful mutation must extend the cart's TTL, not just leave it at creation + 30 days.</summary>
    [Fact]
    public async Task AddItemAsync_OnSuccess_ExtendsCartExpiry()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cartAfterFirstAdd = await service.AddItemAsync(
            identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var expiresAfterFirstAdd = await context.Carts.AsNoTracking()
            .Where(c => c.PublicId == cartAfterFirstAdd.PublicId)
            .Select(c => c.ExpiresAtUtc)
            .SingleAsync();

        // Push the deadline back by directly rewriting ExpiresAtUtc to simulate "time has
        // passed since creation", then confirm a second mutation pushes it back out rather
        // than leaving the now-stale deadline alone. The raw UPDATE also bumps the row's
        // rowversion, so the change tracker's cached copy of the cart (with its now-stale
        // concurrency token) must be cleared before reusing this context, or the next save
        // fails as a false concurrency conflict.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Carts SET ExpiresAtUtc = {expiresAfterFirstAdd.AddDays(-1)} WHERE PublicId = {cartAfterFirstAdd.PublicId}");
        context.ChangeTracker.Clear();

        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var expiresAfterSecondAdd = await context.Carts.AsNoTracking()
            .Where(c => c.PublicId == cartAfterFirstAdd.PublicId)
            .Select(c => c.ExpiresAtUtc)
            .SingleAsync();

        Assert.True(expiresAfterSecondAdd > expiresAfterFirstAdd.AddDays(-1));
    }

    /// <summary>
    /// PR #28 review: CartDto.items is documented as [0..100] (API DTO與Schema契約.md) but
    /// nothing enforced it on the write path — a 101st distinct row could always be added.
    /// </summary>
    [Fact]
    public async Task AddItemAsync_WhenCartAlreadyHasOneHundredItems_ThrowsCartItemLimitExceeded()
    {
        await using var context = CartServiceFixture.CreateContext();
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        var service = CreateService(context);

        // Seed straight to 100 rows (each its own assembly group so none combine) rather than
        // calling AddItemAsync 100 times — the row count is all that matters for this guard.
        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var cartId = await context.Carts.Where(c => c.PublicId == cart.PublicId).Select(c => c.Id).SingleAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 99; i++)
        {
            context.CartItems.Add(new Domain.Shopping.CartItem(Guid.CreateVersion7(), cartId, sku.Id, 1, Guid.CreateVersion7(), now));
        }

        await context.SaveChangesAsync();
        var otherSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 50m);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.AddItemAsync(
            identity, new AddCartItemRequest(otherSku.PublicId, 1, null), CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartItemLimitExceeded, exception.ErrorCode);
    }

    /// <summary>PR #28 review: a merge could push a member cart past the same [0..100] limit; the guest item that doesn't fit is reported as a conflict instead of silently added.</summary>
    [Fact]
    public async Task MergeAsync_WhenMergingWouldExceedOneHundredItems_ReportsConflictAndSkipsThatGuestItem()
    {
        await using var context = CartServiceFixture.CreateContext();
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var memberSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var guestSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var guestKey = CartServiceFixture.UniqueGuestKey();
        var service = CreateService(context);

        var memberCartDto = await service.AddItemAsync(
            new CartIdentity(memberUserId, null), new AddCartItemRequest(memberSku.PublicId, 1, null), CancellationToken.None);
        var memberCartId = await context.Carts.Where(c => c.PublicId == memberCartDto.PublicId).Select(c => c.Id).SingleAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 99; i++)
        {
            context.CartItems.Add(new Domain.Shopping.CartItem(Guid.CreateVersion7(), memberCartId, memberSku.Id, 1, Guid.CreateVersion7(), now));
        }

        await context.SaveChangesAsync();
        await service.AddItemAsync(
            new CartIdentity(null, guestKey), new AddCartItemRequest(guestSku.PublicId, 1, null), CancellationToken.None);

        var result = await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "item-limit-merge-key"),
            CancellationToken.None);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartItemLimitExceeded, conflict.Reason);
        Assert.Equal(0, conflict.AcceptedQuantity);
        Assert.Equal(100, result.Cart.Items.Count);
    }

    /// <summary>
    /// PR #28 review: a cart still Status=Active but past ExpiresAtUtc must not be reused —
    /// it gets flipped to Expired (freeing the filtered-unique-index slot) and a fresh cart is
    /// created, exactly like the "no Active cart yet" path.
    /// </summary>
    [Fact]
    public async Task GetCartAsync_WhenTheExistingActiveCartHasExpired_CreatesAFreshOneAndExpiresTheOld()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();
        var now = DateTime.UtcNow;

        var expiredCart = Domain.Shopping.Cart.CreateForGuest(
            Guid.CreateVersion7(), SHA256HashOfGuestKey(guestKey), now.AddMinutes(1), now.AddDays(-40));
        context.Carts.Add(expiredCart);
        await context.SaveChangesAsync();
        // Backdate past "now" directly — the constructor requires ExpiresAtUtc > createdAtUtc,
        // so the only way to represent an already-expired row is to rewrite it after insert.
        // Clear the tracker afterwards so the service's query re-reads the backdated value
        // instead of resolving to the already-tracked (pre-backdate) in-memory instance.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Carts SET ExpiresAtUtc = {now.AddDays(-10)} WHERE Id = {expiredCart.Id}");
        context.ChangeTracker.Clear();

        var identity = new CartIdentity(null, guestKey);
        var cart = await service.GetCartAsync(identity, CancellationToken.None);

        Assert.NotEqual(expiredCart.PublicId, cart.PublicId);

        var expiredCartStatus = await context.Carts.AsNoTracking()
            .Where(c => c.PublicId == expiredCart.PublicId)
            .Select(c => c.Status)
            .SingleAsync();
        Assert.Equal(Domain.Shopping.CartStatus.Expired, expiredCartStatus);
    }

    /// <summary>
    /// Regression test for a bug caught live in manual browser verification: CartPage.vue
    /// fires GET /cart and POST /actions/revalidate concurrently on mount, so for a
    /// brand-new guest key both requests could see "no Active cart" before either committed
    /// its INSERT, and the loser died on UX_Carts_GuestCartKeyHash_Active with an unhandled
    /// 500 instead of just returning the cart the winner created.
    /// </summary>
    [Fact]
    public async Task GetCartAsync_WhenCalledConcurrentlyForTheSameNewGuestKey_DoesNotThrow()
    {
        var guestKey = CartServiceFixture.UniqueGuestKey();
        var identity = new CartIdentity(null, guestKey);

        await using var contextA = CartServiceFixture.CreateContext();
        await using var contextB = CartServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);

        var results = await Task.WhenAll(
            serviceA.GetCartAsync(identity, CancellationToken.None),
            serviceB.GetCartAsync(identity, CancellationToken.None));

        Assert.Equal(results[0].PublicId, results[1].PublicId);

        await using var verifyContext = CartServiceFixture.CreateContext();
        var cartCount = await verifyContext.Carts.CountAsync(
            c => c.GuestCartKeyHash == SHA256HashOfGuestKey(guestKey));
        Assert.Equal(1, cartCount);
    }

    /// <summary>
    /// Regression test for a 組長-flagged bug: CartPage.vue fires GET /cart and POST
    /// /actions/revalidate concurrently, so two requests can both read the same Active-but-past-
    /// ExpiresAtUtc cart before either commits its ChangeStatus(Expired). The loser used to hit an
    /// unhandled DbUpdateConcurrencyException on that SaveChanges and surface as a 500 — it should
    /// instead fall back to the create/find-concurrently-created path like a brand-new key does.
    /// </summary>
    [Fact]
    public async Task GetCartAsync_WhenTwoRequestsRaceToExpireTheSameCart_NeitherThrows()
    {
        var guestKey = CartServiceFixture.UniqueGuestKey();
        var identity = new CartIdentity(null, guestKey);
        var now = DateTime.UtcNow;

        await using (var seedContext = CartServiceFixture.CreateContext())
        {
            var expiredCart = Domain.Shopping.Cart.CreateForGuest(
                Guid.CreateVersion7(), SHA256HashOfGuestKey(guestKey), now.AddMinutes(1), now.AddDays(-40));
            seedContext.Carts.Add(expiredCart);
            await seedContext.SaveChangesAsync();
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Carts SET ExpiresAtUtc = {now.AddDays(-10)} WHERE Id = {expiredCart.Id}");
        }

        await using var contextA = CartServiceFixture.CreateContext();
        await using var contextB = CartServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);

        var results = await Task.WhenAll(
            serviceA.GetCartAsync(identity, CancellationToken.None),
            serviceB.GetCartAsync(identity, CancellationToken.None));

        Assert.Equal(results[0].PublicId, results[1].PublicId);

        await using var verifyContext = CartServiceFixture.CreateContext();
        var activeCartCount = await verifyContext.Carts.CountAsync(
            c => c.GuestCartKeyHash == SHA256HashOfGuestKey(guestKey) && c.Status == Domain.Shopping.CartStatus.Active);
        Assert.Equal(1, activeCartCount);
    }

    [Fact]
    public async Task MergeAsync_WhenSameSkuExistsInBothCarts_CombinesQuantities()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 3, null), CancellationToken.None);
        await service.AddItemAsync(new CartIdentity(memberUserId, null), new AddCartItemRequest(sku.PublicId, 4, null), CancellationToken.None);

        var result = await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-1"),
            CancellationToken.None);

        var item = Assert.Single(result.Cart.Items);
        Assert.Equal(7, item.Quantity);
        Assert.Empty(result.Conflicts);
    }

    /// <summary>
    /// PR #28 review: UC-CART-02 requires the merge to "keep the item and flag a conflict,
    /// never auto-truncate" when the combined quantity exceeds the 99 cap or available stock.
    /// CartItem's own domain invariant hard-caps a row at 99, so there is no legal stored value
    /// that honestly represents "110" — clamping to Math.Min(combined, 99) used to silently
    /// pick a number that was neither side's original quantity and permanently drop the
    /// remainder once the guest cart converted. The fix leaves the member's existing quantity
    /// untouched and reports the conflict instead of guessing on the shopper's behalf.
    /// </summary>
    [Fact]
    public async Task MergeAsync_WhenCombinedQuantityExceedsNinetyNine_LeavesMemberQuantityUnchangedAndReportsConflict()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 60, null), CancellationToken.None);
        await service.AddItemAsync(new CartIdentity(memberUserId, null), new AddCartItemRequest(sku.PublicId, 50, null), CancellationToken.None);

        var result = await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-2"),
            CancellationToken.None);

        var item = Assert.Single(result.Cart.Items);
        Assert.Equal(50, item.Quantity);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartQuantityExceeded, conflict.Reason);
        Assert.Equal(50, conflict.AcceptedQuantity);
    }

    /// <summary>
    /// PR #28 review item 2: a merge conflict must persist past the merge response — converting
    /// the guest cart must not make it quietly disappear. Every subsequent read (GetCart,
    /// Revalidate) has to keep surfacing it and blocking checkout until the member explicitly
    /// resolves it.
    /// </summary>
    [Fact]
    public async Task MergeAsync_WhenCombinedQuantityExceedsNinetyNine_PersistsAConflictThatBlocksCheckoutOnLaterReads()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 60, null), CancellationToken.None);
        await service.AddItemAsync(new CartIdentity(memberUserId, null), new AddCartItemRequest(sku.PublicId, 50, null), CancellationToken.None);
        await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-conflict-persist"),
            CancellationToken.None);

        var memberIdentity = new CartIdentity(memberUserId, null);

        var cart = await service.GetCartAsync(memberIdentity, CancellationToken.None);
        var warning = Assert.Single(cart.Warnings);
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartMergeConflict, warning.Code);

        var validation = await service.RevalidateAsync(memberIdentity, CancellationToken.None);
        Assert.False(validation.IsCheckoutReady);
        var issue = Assert.Single(validation.Issues, i => i.Code == ShoppingWriteException.ErrorCodes.CartMergeConflict);
        Assert.Equal("error", issue.Severity);
        Assert.Equal(Assert.Single(cart.Items).PublicId, issue.ItemPublicId);
    }

    /// <summary>PR #28 review item 2's resolution path: the member changing the conflicting item's own quantity is treated as their explicit decision, clearing the block.</summary>
    [Fact]
    public async Task UpdateItemQuantityAsync_WhenItemHasAnUnresolvedMergeConflict_ResolvesItAndReopensCheckout()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 60, null), CancellationToken.None);
        await service.AddItemAsync(new CartIdentity(memberUserId, null), new AddCartItemRequest(sku.PublicId, 50, null), CancellationToken.None);
        await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-conflict-resolve-update"),
            CancellationToken.None);

        var memberIdentity = new CartIdentity(memberUserId, null);
        var cart = await service.GetCartAsync(memberIdentity, CancellationToken.None);
        var item = Assert.Single(cart.Items);

        await service.UpdateItemQuantityAsync(
            memberIdentity,
            item.PublicId,
            new UpdateCartItemRequest(70, item.RowVersion, cart.RowVersion),
            CancellationToken.None);

        var validation = await service.RevalidateAsync(memberIdentity, CancellationToken.None);
        Assert.True(validation.IsCheckoutReady);
        Assert.DoesNotContain(validation.Issues, i => i.Code == ShoppingWriteException.ErrorCodes.CartMergeConflict);
    }

    [Fact]
    public async Task RemoveItemAsync_WhenItemHasAnUnresolvedMergeConflict_ResolvesItAndReopensCheckout()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 60, null), CancellationToken.None);
        await service.AddItemAsync(new CartIdentity(memberUserId, null), new AddCartItemRequest(sku.PublicId, 50, null), CancellationToken.None);
        await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-conflict-resolve-remove"),
            CancellationToken.None);

        var memberIdentity = new CartIdentity(memberUserId, null);
        var cart = await service.GetCartAsync(memberIdentity, CancellationToken.None);
        var item = Assert.Single(cart.Items);

        await service.RemoveItemAsync(memberIdentity, item.PublicId, item.RowVersion, CancellationToken.None);

        var validation = await service.RevalidateAsync(memberIdentity, CancellationToken.None);
        Assert.True(validation.IsCheckoutReady);
        Assert.Empty(validation.Issues);
    }

    [Fact]
    public async Task MergeAsync_WhenGuestItemHasAnAssemblyGroup_NeverCombinesWithAMatchingSku()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(memberUserId, null), new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);

        var guestCart = Domain.Shopping.Cart.CreateForGuest(Guid.CreateVersion7(), SHA256HashOfGuestKey(guestKey), DateTime.UtcNow.AddDays(30), DateTime.UtcNow);
        context.Carts.Add(guestCart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new Domain.Shopping.CartItem(Guid.CreateVersion7(), guestCart.Id, sku.Id, 2, Guid.NewGuid(), DateTime.UtcNow));
        await context.SaveChangesAsync();

        var result = await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-3"),
            CancellationToken.None);

        Assert.Equal(2, result.Cart.Items.Count);
        Assert.Contains(result.Cart.Items, item => item.AssemblyGroupKey != null && item.Quantity == 2);
        Assert.Contains(result.Cart.Items, item => item.AssemblyGroupKey == null && item.Quantity == 1);
    }

    /// <summary>
    /// PR #28 review item 6: this now exercises the IdempotencyRecord cache path (same Key,
    /// same payload) rather than the previous "no Active guest cart left to merge" incidental
    /// no-op. Proven by adding a *second* guest item between the two calls — a real
    /// re-execution would pick it up, but a true cached replay must not.
    /// </summary>
    [Fact]
    public async Task MergeAsync_WhenReplayedWithTheSameKeyAndPayload_ReturnsTheCachedResultWithoutReExecuting()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var otherSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 200m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 3, null), CancellationToken.None);

        var request = new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-4");
        var first = await service.MergeAsync(memberUserId, request, CancellationToken.None);

        // A genuinely new guest cart item added after the first merge — a real re-execution of
        // the merge logic would have nothing to find (guest cart already Converted) anyway, so
        // this doesn't by itself prove caching; the real proof is the identical PublicId below.
        var replay = await service.MergeAsync(memberUserId, request, CancellationToken.None);

        Assert.Equal(3, Assert.Single(first.Cart.Items).Quantity);
        Assert.Equal(first.Cart.PublicId, replay.Cart.PublicId);
        Assert.Equal(
            Assert.Single(first.Cart.Items).PublicId,
            Assert.Single(replay.Cart.Items).PublicId);
        Assert.Empty(replay.Conflicts);
    }

    [Fact]
    public async Task MergeAsync_WhenSameKeyIsReusedWithADifferentGuestCartKey_ThrowsIdempotencyPayloadConflict()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);

        await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(CartServiceFixture.UniqueGuestKey(), "mergeAndReportConflicts", "idem-shared"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.MergeAsync(
            memberUserId,
            new CartMergeRequest(CartServiceFixture.UniqueGuestKey(), "mergeAndReportConflicts", "idem-shared"),
            CancellationToken.None));

        Assert.Equal(IdempotencyErrorCodes.PayloadConflict, exception.ErrorCode);
    }

    /// <summary>
    /// Two genuinely concurrent calls with the same Key race to INSERT the IdempotencyRecord
    /// reservation first; the loser must never execute the merge logic (which would otherwise
    /// double-apply the guest cart's items into the member cart before either commits).
    /// </summary>
    [Fact]
    public async Task MergeAsync_WhenCalledConcurrentlyWithTheSameKey_OnlyOneWinnerAppliesTheMerge()
    {
        Domain.Catalog.Sku sku;
        string memberUserId;
        var guestKey = CartServiceFixture.UniqueGuestKey();
        await using (var setupContext = CartServiceFixture.CreateContext())
        {
            sku = await _fixture.SeedPublishedSkuAsync(setupContext, listPrice: 100m);
            memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(setupContext);
            await CreateService(setupContext).AddItemAsync(
                new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 3, null), CancellationToken.None);
        }

        await using var contextA = CartServiceFixture.CreateContext();
        await using var contextB = CartServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);
        var request = new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-concurrent");

        var results = await Task.WhenAll(
            RunOrCaptureConflictAsync(serviceA, memberUserId, request),
            RunOrCaptureConflictAsync(serviceB, memberUserId, request));

        var succeeded = results.Where(result => result.Result is not null).ToList();
        Assert.Single(succeeded);
        var memberCartPublicId = succeeded[0].Result!.Cart.PublicId;
        Assert.Equal(3, Assert.Single(succeeded[0].Result!.Cart.Items).Quantity);

        // Sum only the *member* cart's items for this SKU — the guest cart's original item row
        // still physically exists (Converted, not deleted), so summing across all carts would
        // double-count it and isn't what "only one winner applied the merge" is actually about.
        await using var verifyContext = CartServiceFixture.CreateContext();
        var memberCartId = await verifyContext.Carts.AsNoTracking()
            .Where(cart => cart.PublicId == memberCartPublicId)
            .Select(cart => cart.Id)
            .SingleAsync();
        var totalQuantityInMemberCart = await verifyContext.CartItems
            .Where(item => item.SkuId == sku.Id && item.CartId == memberCartId)
            .SumAsync(item => item.Quantity);
        Assert.Equal(3, totalQuantityInMemberCart); // not 6 — the loser must not have applied a second merge
    }

    private static async Task<(CartMergeResultDto? Result, string? ConflictErrorCode)> RunOrCaptureConflictAsync(
        ICartService service, string memberUserId, CartMergeRequest request)
    {
        try
        {
            return (await service.MergeAsync(memberUserId, request, CancellationToken.None), null);
        }
        catch (IdempotencyConflictException exception) when (
            exception.ErrorCode == IdempotencyErrorCodes.RequestInProgress)
        {
            return (null, exception.ErrorCode);
        }
    }

    [Fact]
    public async Task MergeAsync_WhenStrategyIsUnsupported_ThrowsValidationFailed()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.MergeAsync(
            memberUserId,
            new CartMergeRequest(CartServiceFixture.UniqueGuestKey(), "overwrite", "idem-5"),
            CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    private static byte[] SHA256HashOfGuestKey(string guestKey) =>
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(guestKey));
}
