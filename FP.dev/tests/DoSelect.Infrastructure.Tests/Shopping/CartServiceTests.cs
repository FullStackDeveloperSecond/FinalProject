using DoSelect.Application.Shopping;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Shopping;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Shopping;

[CollectionDefinition(nameof(CartServiceCollection))]
public sealed class CartServiceCollection : ICollectionFixture<CartServiceFixture>;

[Collection(nameof(CartServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CartServiceTests
{
    private readonly CartServiceFixture _fixture;

    public CartServiceTests(CartServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddItemAsync_WhenCartIsEmpty_CreatesGuestCartAndItem()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
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
        var service = new EfCartService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        context.InventoryBalances.Add(new InventoryBalance(Guid.CreateVersion7(), sku.Id, onHandQuantity: 2, reorderLevel: 0, DateTime.UtcNow));
        await context.SaveChangesAsync();

        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 5, null), CancellationToken.None);

        var validation = await service.RevalidateAsync(identity, CancellationToken.None);

        Assert.False(validation.IsCheckoutReady);
        Assert.Contains(validation.Issues, issue => issue.Code == ShoppingWriteException.ErrorCodes.CartItemRequiresAttention);
        var item = Assert.Single(validation.Cart.Items);
        Assert.Equal("insufficient_stock", item.Availability);
        Assert.Equal(2, item.MaxPurchasableQuantity);
    }

    [Fact]
    public async Task RevalidateAsync_WhenNoInventoryBalanceRowExists_DoesNotBlockCheckout()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, CartServiceFixture.UniqueGuestKey());
        await service.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, 50, null), CancellationToken.None);

        var validation = await service.RevalidateAsync(identity, CancellationToken.None);

        Assert.True(validation.IsCheckoutReady);
        Assert.Equal("available", Assert.Single(validation.Cart.Items).Availability);
    }

    [Fact]
    public async Task RevalidateAsync_WhenAnActiveSalePriceIsInWindow_UsesItAsTheEffectivePrice()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
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
        var serviceA = new EfCartService(contextA);
        var serviceB = new EfCartService(contextB);

        var results = await Task.WhenAll(
            serviceA.GetCartAsync(identity, CancellationToken.None),
            serviceB.GetCartAsync(identity, CancellationToken.None));

        Assert.Equal(results[0].PublicId, results[1].PublicId);

        await using var verifyContext = CartServiceFixture.CreateContext();
        var cartCount = await verifyContext.Carts.CountAsync(
            c => c.GuestCartKeyHash == SHA256HashOfGuestKey(guestKey));
        Assert.Equal(1, cartCount);
    }

    [Fact]
    public async Task MergeAsync_WhenSameSkuExistsInBothCarts_CombinesQuantities()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
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

    [Fact]
    public async Task MergeAsync_WhenCombinedQuantityExceedsNinetyNine_ClampsAndReportsConflict()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
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
        Assert.Equal(99, item.Quantity);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(ShoppingWriteException.ErrorCodes.CartQuantityExceeded, conflict.Reason);
        Assert.Equal(99, conflict.AcceptedQuantity);
    }

    [Fact]
    public async Task MergeAsync_WhenGuestItemHasAnAssemblyGroup_NeverCombinesWithAMatchingSku()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
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

    [Fact]
    public async Task MergeAsync_WhenReplayedAfterTheGuestCartIsAlreadyConverted_IsANoOp()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
        var sku = await _fixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var memberUserId = await CartServiceFixture.SeedMemberUserIdAsync(context);
        var guestKey = CartServiceFixture.UniqueGuestKey();

        await service.AddItemAsync(new CartIdentity(null, guestKey), new AddCartItemRequest(sku.PublicId, 3, null), CancellationToken.None);

        var request = new CartMergeRequest(guestKey, "mergeAndReportConflicts", "idem-4");
        var first = await service.MergeAsync(memberUserId, request, CancellationToken.None);
        var replay = await service.MergeAsync(memberUserId, request, CancellationToken.None);

        Assert.Equal(3, Assert.Single(first.Cart.Items).Quantity);
        Assert.Equal(3, Assert.Single(replay.Cart.Items).Quantity);
        Assert.Empty(replay.Conflicts);
    }

    [Fact]
    public async Task MergeAsync_WhenStrategyIsUnsupported_ThrowsValidationFailed()
    {
        await using var context = CartServiceFixture.CreateContext();
        var service = new EfCartService(context);
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
