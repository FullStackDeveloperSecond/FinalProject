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
    public async Task UpdateItemQuantityAsync_WhenOwnedCartHasExpired_ThrowsNotFoundWithoutChangingCart()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(
            identity,
            new AddCartItemRequest(sku.PublicId, 1, null),
            CancellationToken.None);
        var item = Assert.Single(cart.Items);
        var expiredAtUtc = DateTime.UtcNow.AddDays(-1);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Carts SET ExpiresAtUtc = {expiredAtUtc} WHERE PublicId = {cart.PublicId}");
        context.ChangeTracker.Clear();

        var cartBefore = await context.Carts.AsNoTracking()
            .Where(candidate => candidate.PublicId == cart.PublicId)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.ExpiresAtUtc,
                candidate.UpdatedAtUtc,
                candidate.RowVersion,
            })
            .SingleAsync();
        var itemBefore = await context.CartItems.AsNoTracking()
            .Where(candidate => candidate.PublicId == item.PublicId)
            .Select(candidate => new
            {
                candidate.Quantity,
                candidate.UpdatedAtUtc,
                candidate.RowVersion,
            })
            .SingleAsync();
        var cartCountBefore = await context.Carts.CountAsync();

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.UpdateItemQuantityAsync(
            identity,
            item.PublicId,
            new UpdateCartItemRequest(2, item.RowVersion, cart.RowVersion),
            CancellationToken.None));

        Assert.Equal(ShoppingWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);

        context.ChangeTracker.Clear();
        var cartAfter = await context.Carts.AsNoTracking()
            .Where(candidate => candidate.PublicId == cart.PublicId)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.ExpiresAtUtc,
                candidate.UpdatedAtUtc,
                candidate.RowVersion,
            })
            .SingleAsync();
        var itemAfter = await context.CartItems.AsNoTracking()
            .Where(candidate => candidate.PublicId == item.PublicId)
            .Select(candidate => new
            {
                candidate.Quantity,
                candidate.UpdatedAtUtc,
                candidate.RowVersion,
            })
            .SingleAsync();

        Assert.Equal(cartCountBefore, await context.Carts.CountAsync());
        Assert.Equal(cartBefore.Status, cartAfter.Status);
        Assert.Equal(cartBefore.ExpiresAtUtc, cartAfter.ExpiresAtUtc);
        Assert.Equal(cartBefore.UpdatedAtUtc, cartAfter.UpdatedAtUtc);
        Assert.Equal(cartBefore.RowVersion, cartAfter.RowVersion);
        Assert.Equal(itemBefore.Quantity, itemAfter.Quantity);
        Assert.Equal(itemBefore.UpdatedAtUtc, itemAfter.UpdatedAtUtc);
        Assert.Equal(itemBefore.RowVersion, itemAfter.RowVersion);
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

    /// <summary>
    /// PR #34 review: AddAssemblyGroupsAsync (build-list add-to-cart) added unitCount *
    /// perUnitItems.Count new rows with no cap check at all — a cart at 90 items plus two 8-item
    /// assemblies (106 rows) would have violated the same [0..100] contract AddItemAsync already
    /// enforced.
    /// </summary>
    [Fact]
    public async Task AddAssemblyGroupsAsync_WhenAddingWouldExceedOneHundredItems_ThrowsCartItemLimitExceededAndAddsNothing()
    {
        await using var context = CartServiceFixture.CreateContext();
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        var service = CreateService(context);

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var cartId = await context.Carts.Where(c => c.PublicId == cart.PublicId).Select(c => c.Id).SingleAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 93; i++)
        {
            context.CartItems.Add(new Domain.Shopping.CartItem(Guid.CreateVersion7(), cartId, sku.Id, 1, Guid.CreateVersion7(), now));
        }

        await context.SaveChangesAsync();

        // Cart now has 94 items. One assembly unit of 8 SKUs would land at 102 — over the cap.
        var assemblySkus = new List<Sku>();
        for (var i = 0; i < 8; i++)
        {
            assemblySkus.Add(await _fixture.SeedPublishedSkuAsync(context, listPrice: 50m));
        }

        var perUnitItems = assemblySkus.Select(assemblySku => new AssemblyGroupItemInput(assemblySku.PublicId, 1)).ToList();

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.AddAssemblyGroupsAsync(
            identity, perUnitItems, unitCount: 1, CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartItemLimitExceeded, exception.ErrorCode);

        var itemCountAfter = await context.CartItems.CountAsync(item => item.CartId == cartId);
        Assert.Equal(94, itemCountAfter);
    }

    /// <summary>
    /// 組長 PR #29 round-6 review, P1: an assembly-group item represents one SKU of one physical
    /// build — every member shares the same AssemblyGroupKey, one NT$300 assembly fee, and (once
    /// checkout exists) one AssemblyJob. Letting a single member's quantity change independently
    /// (e.g. 2 CPUs but still 1 motherboard) would leave the group referring to a build that was
    /// never actually configured. The frontend already refuses to offer per-item controls for a
    /// grouped item; this proves the server rejects it too, so no other client can bypass that.
    /// </summary>
    [Fact]
    public async Task UpdateItemQuantityAsync_ForAnAssemblyGroupItem_ThrowsCartAssemblyItemImmutable_AndAppliesNoChange()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        var cart = await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 1, CancellationToken.None);
        var cpuItem = Assert.Single(cart.Items, item => item.SkuPublicId == cpuSku.PublicId);
        Assert.NotNull(cpuItem.AssemblyGroupKey);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.UpdateItemQuantityAsync(
            identity,
            cpuItem.PublicId,
            new UpdateCartItemRequest(2, cpuItem.RowVersion, cart.RowVersion),
            CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartAssemblyItemImmutable, exception.ErrorCode);

        var reloaded = await service.GetCartAsync(identity, CancellationToken.None);
        var reloadedCpuItem = Assert.Single(reloaded.Items, item => item.SkuPublicId == cpuSku.PublicId);
        Assert.Equal(1, reloadedCpuItem.Quantity);
    }

    /// <summary>Same rule as the quantity-change test above, for removal — see that test's remarks.</summary>
    [Fact]
    public async Task RemoveItemAsync_ForAnAssemblyGroupItem_ThrowsCartAssemblyItemImmutable_AndLeavesTheGroupIntact()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        var cart = await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 1, CancellationToken.None);
        var cpuItem = Assert.Single(cart.Items, item => item.SkuPublicId == cpuSku.PublicId);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.RemoveItemAsync(
            identity, cpuItem.PublicId, cpuItem.RowVersion, CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartAssemblyItemImmutable, exception.ErrorCode);

        var reloaded = await service.GetCartAsync(identity, CancellationToken.None);
        Assert.Equal(2, reloaded.Items.Count);
    }

    /// <summary>Sanity check that the new assembly-group guard doesn't over-reach: a plain (non-grouped) item must still be freely editable.</summary>
    [Fact]
    public async Task UpdateItemQuantityAsync_ForAPlainNonGroupedItem_StillSucceeds()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);
        var item = Assert.Single(cart.Items);
        Assert.Null(item.AssemblyGroupKey);

        var updated = await service.UpdateItemQuantityAsync(
            identity, item.PublicId, new UpdateCartItemRequest(3, item.RowVersion, cart.RowVersion), CancellationToken.None);

        Assert.Equal(3, Assert.Single(updated.Items).Quantity);
    }

    /// <summary>
    /// PR #28 review (組長 2nd-round ruling): a merge that would push the member cart past the
    /// [0..100] limit must reject the *whole* merge — nothing lands, the guest cart stays Active
    /// (not Converted) — rather than the earlier round's per-item skip, which silently converted
    /// the guest cart anyway and lost the skipped item forever.
    /// </summary>
    [Fact]
    public async Task MergeAsync_WhenMergingWouldExceedOneHundredItems_RejectsTheWholeMergeAndKeepsTheGuestCartActive()
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
        var guestCartDto = await service.AddItemAsync(
            new CartIdentity(null, guestKey), new AddCartItemRequest(guestSku.PublicId, 1, null), CancellationToken.None);

        var result = await service.MergeAsync(
            memberUserId,
            new CartMergeRequest(guestKey, "mergeAndReportConflicts", "item-limit-merge-key"),
            CancellationToken.None);

        // PR #28 review round 4: a whole-merge rejection is a 409, not a 200.
        Assert.Equal(409, result.StatusCode);
        var conflict = Assert.Single(result.Body.Conflicts);
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartItemLimitExceeded, conflict.Reason);
        Assert.Equal(0, conflict.AcceptedQuantity);
        // Nothing merged: the member cart is exactly what it was before this call.
        Assert.Equal(100, result.Body.Cart.Items.Count);

        var guestCartStatus = await context.Carts.AsNoTracking()
            .Where(c => c.PublicId == guestCartDto.PublicId).Select(c => c.Status).SingleAsync();
        Assert.Equal(Domain.Shopping.CartStatus.Active, guestCartStatus);

        var persistedConflict = await context.CartMergeConflicts.AsNoTracking().SingleAsync(
            c => c.MemberCartId == memberCartId && c.Reason == ShoppingWriteException.ErrorCodes.CartItemLimitExceeded);
        Assert.True(persistedConflict.IsBlocking);

        var validation = await service.RevalidateAsync(new CartIdentity(memberUserId, null), CancellationToken.None);
        Assert.False(validation.IsCheckoutReady);
        // PR #28 review round 4: this must surface as cart_item_limit_exceeded (with "free up
        // space and re-merge" guidance), not the generic per-item cart_merge_conflict code.
        Assert.Contains(
            validation.Issues, issue => issue.Code == ShoppingWriteException.ErrorCodes.CartItemLimitExceeded);
    }

    /// <summary>
    /// PR #28 review round 4/5: even when the member's own cart happens to already hold the
    /// exact SKU the cart-level conflict is anchored on (a schema artifact — the conflict has to
    /// point at *some* GuestItemPublicId／SkuPublicId), touching that one SKU must not clear a
    /// block that is actually about the whole cart being over 100 items.
    /// </summary>
    [Fact]
    public async Task UpdateItemQuantityAsync_WhenTouchingTheAnchorSkuOfAnItemLimitConflict_DoesNotResolveIt()
    {
        await using var context = CartServiceFixture.CreateContext();
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var fillerSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var anchorSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var guestKey = CartServiceFixture.UniqueGuestKey();
        var service = CreateService(context);

        // The member's cart already holds the SKU that will become the merge conflict's anchor —
        // this is the scenario the schema constraint (anchor = first guest item's SKU) can
        // collide with an unrelated, already-present member item.
        var memberCartDto = await service.AddItemAsync(
            new CartIdentity(memberUserId, null), new AddCartItemRequest(anchorSku.PublicId, 1, null), CancellationToken.None);
        var memberCartId = await context.Carts.Where(c => c.PublicId == memberCartDto.PublicId).Select(c => c.Id).SingleAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 99; i++)
        {
            context.CartItems.Add(new Domain.Shopping.CartItem(Guid.CreateVersion7(), memberCartId, fillerSku.Id, 1, Guid.CreateVersion7(), now));
        }

        await context.SaveChangesAsync();

        // Insert the guest item directly as an assembly-group row (mirrors
        // MergeAsync_WhenGuestItemHasAnAssemblyGroup_NeverCombinesWithAMatchingSku) so it always
        // creates a new row on merge — pushing the projected count to 101 — even though its SKU
        // already matches an existing member row that would otherwise just combine quantities.
        var guestCart = Domain.Shopping.Cart.CreateForGuest(Guid.CreateVersion7(), SHA256HashOfGuestKey(guestKey), now.AddDays(30), now);
        context.Carts.Add(guestCart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new Domain.Shopping.CartItem(Guid.CreateVersion7(), guestCart.Id, anchorSku.Id, 1, Guid.NewGuid(), now));
        await context.SaveChangesAsync();

        var mergeResult = await service.MergeAsync(
            memberUserId, new CartMergeRequest(guestKey, "mergeAndReportConflicts", "anchor-sku-key"), CancellationToken.None);
        Assert.Equal(409, mergeResult.StatusCode);

        var cartAfterMerge = await service.GetCartAsync(new CartIdentity(memberUserId, null), CancellationToken.None);
        var anchorItem = cartAfterMerge.Items.Single(item => item.SkuPublicId == anchorSku.PublicId);

        await service.UpdateItemQuantityAsync(
            new CartIdentity(memberUserId, null),
            anchorItem.PublicId,
            new UpdateCartItemRequest(2, anchorItem.RowVersion, cartAfterMerge.RowVersion),
            CancellationToken.None);

        var conflict = await context.CartMergeConflicts.AsNoTracking().SingleAsync(
            c => c.MemberCartId == memberCartId && c.Reason == ShoppingWriteException.ErrorCodes.CartItemLimitExceeded);
        Assert.True(conflict.IsBlocking);

        var validation = await service.RevalidateAsync(new CartIdentity(memberUserId, null), CancellationToken.None);
        Assert.False(validation.IsCheckoutReady);
    }

    /// <summary>
    /// PR #28 review round 5 (組長 ruling): if the member never frees up space before the guest
    /// cart's (extended) expiry finally elapses, there is no successful re-merge left to resolve
    /// the block — it must self-heal instead of trapping the member forever.
    /// </summary>
    [Fact]
    public async Task RevalidateAsync_WhenTheBlockingGuestCartHasFinallyExpired_ResolvesTheConflictAndReopensCheckout()
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
        var guestCartDto = await service.AddItemAsync(
            new CartIdentity(null, guestKey), new AddCartItemRequest(guestSku.PublicId, 1, null), CancellationToken.None);

        await service.MergeAsync(
            memberUserId, new CartMergeRequest(guestKey, "mergeAndReportConflicts", "expiry-key"), CancellationToken.None);

        var blockedValidation = await service.RevalidateAsync(new CartIdentity(memberUserId, null), CancellationToken.None);
        Assert.False(blockedValidation.IsCheckoutReady);

        // Simulate the guest cart's (extended) expiry finally elapsing — no member action ever
        // came to free up space or retry the merge.
        await context.Carts
            .Where(c => c.PublicId == guestCartDto.PublicId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.ExpiresAtUtc, DateTime.UtcNow.AddDays(-1)));

        var validation = await service.RevalidateAsync(new CartIdentity(memberUserId, null), CancellationToken.None);

        Assert.True(validation.IsCheckoutReady);
        var conflict = await context.CartMergeConflicts.AsNoTracking().SingleAsync(
            c => c.MemberCartId == memberCartId && c.Reason == ShoppingWriteException.ErrorCodes.CartItemLimitExceeded);
        Assert.False(conflict.IsBlocking);
        Assert.Equal("guest_cart_expired", conflict.ResolutionCode);
    }

    /// <summary>
    /// PR #28 review: once the member frees up room, a fresh merge attempt (new Idempotency-Key,
    /// same guest cart) must succeed, resolve the earlier cart-level conflict, and convert the
    /// guest cart — no separate Resolve API, per 組長's ruling.
    /// </summary>
    [Fact]
    public async Task MergeAsync_WhenRetriedAfterFreeingSpace_ResolvesThePriorConflictAndConvertsTheGuestCart()
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
        var extraMemberItems = new List<Domain.Shopping.CartItem>();
        for (var i = 0; i < 99; i++)
        {
            var extra = new Domain.Shopping.CartItem(Guid.CreateVersion7(), memberCartId, memberSku.Id, 1, Guid.CreateVersion7(), now);
            extraMemberItems.Add(extra);
            context.CartItems.Add(extra);
        }

        await context.SaveChangesAsync();
        var guestCartDto = await service.AddItemAsync(
            new CartIdentity(null, guestKey), new AddCartItemRequest(guestSku.PublicId, 1, null), CancellationToken.None);

        var firstAttempt = await service.MergeAsync(
            memberUserId, new CartMergeRequest(guestKey, "mergeAndReportConflicts", "first-attempt"), CancellationToken.None);
        Assert.Equal(409, firstAttempt.StatusCode);

        // Free up room: remove one of the extra member rows.
        context.CartItems.Remove(extraMemberItems[0]);
        await context.SaveChangesAsync();

        var retryResult = await service.MergeAsync(
            memberUserId, new CartMergeRequest(guestKey, "mergeAndReportConflicts", "second-attempt"), CancellationToken.None);

        Assert.Equal(200, retryResult.StatusCode);
        Assert.Empty(retryResult.Body.Conflicts);
        Assert.Contains(retryResult.Body.Cart.Items, item => item.SkuPublicId == guestSku.PublicId);

        var guestCartStatus = await context.Carts.AsNoTracking()
            .Where(c => c.PublicId == guestCartDto.PublicId).Select(c => c.Status).SingleAsync();
        Assert.Equal(Domain.Shopping.CartStatus.Converted, guestCartStatus);

        var priorConflict = await context.CartMergeConflicts.AsNoTracking().SingleAsync(
            c => c.MemberCartId == memberCartId && c.Reason == ShoppingWriteException.ErrorCodes.CartItemLimitExceeded);
        Assert.False(priorConflict.IsBlocking);
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

        Assert.Equal(200, result.StatusCode);
        var item = Assert.Single(result.Body.Cart.Items);
        Assert.Equal(7, item.Quantity);
        Assert.Empty(result.Body.Conflicts);
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

        Assert.Equal(200, result.StatusCode);
        var item = Assert.Single(result.Body.Cart.Items);
        Assert.Equal(50, item.Quantity);
        var conflict = Assert.Single(result.Body.Conflicts);
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

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, result.Body.Cart.Items.Count);
        Assert.Contains(result.Body.Cart.Items, item => item.AssemblyGroupKey != null && item.Quantity == 2);
        Assert.Contains(result.Body.Cart.Items, item => item.AssemblyGroupKey == null && item.Quantity == 1);
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

        Assert.Equal(200, first.StatusCode);
        Assert.Equal(200, replay.StatusCode);
        Assert.Equal(3, Assert.Single(first.Body.Cart.Items).Quantity);
        Assert.Equal(first.Body.Cart.PublicId, replay.Body.Cart.PublicId);
        Assert.Equal(
            Assert.Single(first.Body.Cart.Items).PublicId,
            Assert.Single(replay.Body.Cart.Items).PublicId);
        Assert.Empty(replay.Body.Conflicts);
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
    /// Two calls launched together with the same Key race to INSERT the IdempotencyRecord
    /// reservation first. Depending on scheduling, the second call either overlaps and receives
    /// request-in-progress, or starts after commit and replays the stored result. In both cases,
    /// only one original execution may apply the merge.
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

        var original = Assert.Single(results, result => result.Result is { IsReplay: false });
        var memberCartPublicId = original.Result!.Body.Cart.PublicId;
        Assert.Equal(3, Assert.Single(original.Result.Body.Cart.Items).Quantity);

        var replayed = results.Where(result => result.Result is { IsReplay: true }).ToList();
        var conflicted = results
            .Where(result => result.ConflictErrorCode == IdempotencyErrorCodes.RequestInProgress)
            .ToList();
        Assert.Equal(1, replayed.Count + conflicted.Count);

        if (replayed.Count == 1)
        {
            Assert.Equal(memberCartPublicId, replayed[0].Result!.Body.Cart.PublicId);
            Assert.Equal(3, Assert.Single(replayed[0].Result!.Body.Cart.Items).Quantity);
        }

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

    private static async Task<(IdempotencyExecutionResult<CartMergeResultDto>? Result, string? ConflictErrorCode)> RunOrCaptureConflictAsync(
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

    /// <summary>
    /// 組長 PR #29 round 7 review (P1): BuildCartDto used to hardcode AssemblyFee to 0m and
    /// TotalEstimate to just the merchandise subtotal — a cart holding one or more assembly
    /// groups (each an AssemblyGroupKey shared by every SKU of one physical build,
    /// AddAssemblyGroupsAsync above) always undercounted its own total by the NT$300／台 assembly
    /// fee UC-BUILD-01/EfBuildListService already charges for the exact same "one build" concept.
    /// Covers 0／1／3 groups plus a mix with a plain (non-grouped) SKU, matching 組長's explicit
    /// ask.
    /// </summary>
    [Fact]
    public async Task GetCartAsync_WithNoAssemblyGroups_ChargesNoAssemblyFee()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 2, null), CancellationToken.None);

        Assert.Equal(0m, cart.Amounts.AssemblyFee);
        Assert.Equal(2000m, cart.Amounts.Subtotal);
        Assert.Equal(2000m, cart.Amounts.TotalEstimate);
    }

    [Fact]
    public async Task GetCartAsync_WithOneAssemblyGroup_ChargesOneAssemblyFee()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        var cart = await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 1, CancellationToken.None);

        Assert.Equal(300m, cart.Amounts.AssemblyFee);
        Assert.Equal(8000m, cart.Amounts.Subtotal);
        Assert.Equal(8300m, cart.Amounts.TotalEstimate);
    }

    [Fact]
    public async Task GetCartAsync_WithThreeAssemblyGroupUnits_ChargesThreeAssemblyFees()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        // unitCount: 3 -> AddAssemblyGroupsAsync mints one distinct AssemblyGroupKey per unit
        // (see its own remarks), i.e. 3 separately-billed physical builds.
        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        var cart = await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 3, CancellationToken.None);

        var distinctGroupKeys = cart.Items.Select(item => item.AssemblyGroupKey).Distinct().ToArray();
        Assert.Equal(3, distinctGroupKeys.Length);
        Assert.Equal(900m, cart.Amounts.AssemblyFee);
        Assert.Equal(24000m, cart.Amounts.Subtotal);
        Assert.Equal(24900m, cart.Amounts.TotalEstimate);
    }

    [Fact]
    public async Task GetCartAsync_WithAnAssemblyGroupMixedWithAPlainSku_ChargesOnlyForTheGroup()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var plainSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 500m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 1, CancellationToken.None);
        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(plainSku.PublicId, 2, null), CancellationToken.None);

        Assert.Equal(300m, cart.Amounts.AssemblyFee);
        Assert.Equal(9000m, cart.Amounts.Subtotal);
        Assert.Equal(9300m, cart.Amounts.TotalEstimate);
    }

    /// <summary>
    /// 組長 PR #29 round 7 review (P1，AUTO-DEC-015)：「群組 SKU 變成缺貨／下架 → Checkout 被阻止
    /// → 整組移除 → Cart 恢復可重新驗證」的完整跨層回歸。Before this fix an assembly group whose
    /// SKU went unavailable had no legal recovery path at all — every per-item write rejected it
    /// with CartAssemblyItemImmutable, so the blocking issue could never be cleared and checkout
    /// stayed gated forever.
    /// </summary>
    [Fact]
    public async Task RemoveAssemblyGroupAsync_AfterAGroupSkuBecomesUnavailable_UnblocksCheckoutAtomically()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var plainSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 500m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 1, CancellationToken.None);
        await service.AddItemAsync(identity, new AddCartItemRequest(plainSku.PublicId, 1, null), CancellationToken.None);

        var healthy = await service.RevalidateAsync(identity, CancellationToken.None);
        Assert.True(healthy.IsCheckoutReady);

        // One SKU inside the group is delisted after it was already added to the cart.
        var trackedCpu = await context.Skus.FirstAsync(sku => sku.Id == cpuSku.Id);
        trackedCpu.ChangeStatus(SkuStatus.Unpublished, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var blocked = await service.RevalidateAsync(identity, CancellationToken.None);
        Assert.False(blocked.IsCheckoutReady);
        var groupIssue = Assert.Single(blocked.Issues, issue => issue.Code == ShoppingWriteException.ErrorCodes.SkuUnavailable);
        // The advertised action must be one the backend will actually honor — `reduce-quantity`
        // and `remove` are both rejected for a grouped item.
        Assert.Equal(["remove-group"], groupIssue.AvailableActions);

        var groupKey = blocked.Cart.Items
            .Where(item => item.SkuPublicId == cpuSku.PublicId)
            .Select(item => item.AssemblyGroupKey)
            .Single();
        Assert.NotNull(groupKey);

        var afterRemoval = await service.RemoveAssemblyGroupAsync(
            identity, groupKey.Value, blocked.Cart.RowVersion, CancellationToken.None);

        // Both group rows gone in one shot — never half-removed — and the unrelated plain SKU kept.
        Assert.DoesNotContain(afterRemoval.Items, item => item.AssemblyGroupKey is not null);
        Assert.Single(afterRemoval.Items, item => item.SkuPublicId == plainSku.PublicId);
        Assert.Equal(0m, afterRemoval.Amounts.AssemblyFee);

        var recovered = await service.RevalidateAsync(identity, CancellationToken.None);
        Assert.True(recovered.IsCheckoutReady);
        Assert.Empty(recovered.Issues);
    }

    [Fact]
    public async Task RemoveAssemblyGroupAsync_WithOneOfSeveralGroups_RemovesOnlyThatGroup()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        var cart = await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 2, CancellationToken.None);
        var groupKeys = cart.Items.Select(item => item.AssemblyGroupKey!.Value).Distinct().ToArray();
        Assert.Equal(2, groupKeys.Length);

        var afterRemoval = await service.RemoveAssemblyGroupAsync(
            identity, groupKeys[0], cart.RowVersion, CancellationToken.None);

        var remainingGroupKeys = afterRemoval.Items.Select(item => item.AssemblyGroupKey!.Value).Distinct().ToArray();
        Assert.Equal([groupKeys[1]], remainingGroupKeys);
        Assert.Equal(2, afterRemoval.Items.Count);
        Assert.Equal(300m, afterRemoval.Amounts.AssemblyFee);
    }

    [Fact]
    public async Task RemoveAssemblyGroupAsync_WithAStaleCartRowVersion_ThrowsConcurrencyConflictAndRemovesNothing()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var plainSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 500m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        var cart = await service.AddAssemblyGroupsAsync(identity, perUnitItems, unitCount: 1, CancellationToken.None);
        var staleRowVersion = cart.RowVersion;
        var groupKey = cart.Items.Select(item => item.AssemblyGroupKey!.Value).Distinct().Single();

        // Someone else mutates the cart, bumping its RowVersion out from under the stale copy.
        await service.AddItemAsync(identity, new AddCartItemRequest(plainSku.PublicId, 1, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.RemoveAssemblyGroupAsync(
            identity, groupKey, staleRowVersion, CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);

        await using var verifyContext = CartServiceFixture.CreateContext();
        var reloaded = await CreateService(verifyContext).GetCartAsync(identity, CancellationToken.None);
        Assert.Equal(2, reloaded.Items.Count(item => item.AssemblyGroupKey is not null));
    }

    [Fact]
    public async Task RemoveAssemblyGroupAsync_ForAnUnknownGroupKey_ThrowsResourceNotFound()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var cart = await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 1, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.RemoveAssemblyGroupAsync(
            identity, Guid.CreateVersion7(), cart.RowVersion, CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    /// <summary>
    /// A group belonging to a *different* cart must be invisible here — the group lookup is scoped
    /// to the caller's own cart, so this is a not-found, never a cross-cart removal.
    /// </summary>
    [Fact]
    public async Task RemoveAssemblyGroupAsync_ForAnotherCartsGroup_ThrowsResourceNotFoundAndLeavesThatGroupIntact()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = CreateService(context);
        var cpuSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 5000m);
        var boardSku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 3000m);
        var victimIdentity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        var attackerIdentity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());

        var perUnitItems = new[] { new AssemblyGroupItemInput(cpuSku.PublicId, 1), new AssemblyGroupItemInput(boardSku.PublicId, 1) };
        var victimCart = await service.AddAssemblyGroupsAsync(victimIdentity, perUnitItems, unitCount: 1, CancellationToken.None);
        var victimGroupKey = victimCart.Items.Select(item => item.AssemblyGroupKey!.Value).Distinct().Single();

        var attackerCart = await service.AddItemAsync(
            attackerIdentity, new AddCartItemRequest(cpuSku.PublicId, 1, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShoppingWriteException>(() => service.RemoveAssemblyGroupAsync(
            attackerIdentity, victimGroupKey, attackerCart.RowVersion, CancellationToken.None));
        Assert.Equal(ShoppingWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);

        var victimReloaded = await service.GetCartAsync(victimIdentity, CancellationToken.None);
        Assert.Equal(2, victimReloaded.Items.Count(item => item.AssemblyGroupKey == victimGroupKey));
    }

    private static byte[] SHA256HashOfGuestKey(string guestKey) =>
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(guestKey));
}
