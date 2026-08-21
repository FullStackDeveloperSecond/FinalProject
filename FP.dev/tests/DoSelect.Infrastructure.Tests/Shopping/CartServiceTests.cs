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
}
