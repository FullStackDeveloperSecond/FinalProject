using DoSelect.Application.Idempotency;
using DoSelect.Application.Shopping;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;
using DoSelect.Infrastructure.Shopping;
using Microsoft.Extensions.Options;
using DoSelect.Domain.Shipping;

namespace DoSelect.Infrastructure.Tests.Shipping;

[CollectionDefinition(nameof(ShippingServiceCollection))]
public sealed class ShippingServiceCollection : ICollectionFixture<ShippingServiceFixture>;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ShippingOptionsServiceTests
{
    private const string TestActorScopePepper = "shipping-options-tests-actor-scope-pepper-0";

    [Fact]
    public async Task GetOptionsForCartAsync_ReturnsEveryActiveMethodWithItsFee()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var storePickup = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.StorePickup, 60m, 2000m, true, false);
        await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 5000m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        Assert.Equal(2, options.Options.Count);
        var storePickupOption = options.Options.Single(option => option.MethodCode == storePickup.Code);
        Assert.Equal(60m, storePickupOption.Fee);
        Assert.True(storePickupOption.IsEligible);
        Assert.True(storePickupOption.RequiresStore);
        Assert.False(storePickupOption.RequiresAddress);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WhenSubtotalMeetsThreshold_WaivesTheFee()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.StorePickup, 60m, 2000m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 2500m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        Assert.Equal(0m, options.Options.Single().Fee);
    }

    /// <summary>購物車、訂單、付款與物流.md line 199: "組裝電腦...只能選擇宅配" — a cart containing an
    /// assembly-group item must not be able to select store pickup.</summary>
    [Fact]
    public async Task GetOptionsForCartAsync_WhenCartContainsAnAssemblyItem_StorePickupIsIneligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var storePickup = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.StorePickup, 60m, 2000m, true, false);
        var homeDelivery = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 5000m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);
        await ShippingServiceFixture.AddAssemblyItemAsync(context, identity.GuestCartKey!, sku);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var storePickupOption = options.Options.Single(option => option.MethodCode == storePickup.Code);
        Assert.False(storePickupOption.IsEligible);
        Assert.Equal("shipping_method_not_allowed", storePickupOption.IneligibleReasonCode);
        var homeDeliveryOption = options.Options.Single(option => option.MethodCode == homeDelivery.Code);
        Assert.True(homeDeliveryOption.IsEligible);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WhenMethodDoesNotAllowCod_OmitsCashOnDeliveryFromAllowedPaymentMethods()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDeliveryAssembly, 300m, 30000m, false, true);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        Assert.DoesNotContain("cashOnDelivery", options.Options.Single().AllowedPaymentMethods);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WhenFinalPayableExceedsTwentyThousand_OmitsCashOnDeliveryFromAllowedPaymentMethods()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 20_001m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        Assert.DoesNotContain("cashOnDelivery", options.Options.Single().AllowedPaymentMethods);
    }

    private static EfShippingOptionsService CreateService(DoSelectDbContext context) =>
        new(context, new EfCartService(context, new EfIdempotencyExecutor(
            context,
            Options.Create(new IdempotencyOptions { ActorScopePepper = TestActorScopePepper }),
            TimeProvider.System)));

    private static Task AddItemAsync(DoSelectDbContext context, CartIdentity identity, DoSelect.Domain.Catalog.Sku sku, int quantity)
    {
        var cartService = new EfCartService(context, new EfIdempotencyExecutor(
            context,
            Options.Create(new IdempotencyOptions { ActorScopePepper = TestActorScopePepper }),
            TimeProvider.System));
        return cartService.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, quantity, null), CancellationToken.None);
    }
}
