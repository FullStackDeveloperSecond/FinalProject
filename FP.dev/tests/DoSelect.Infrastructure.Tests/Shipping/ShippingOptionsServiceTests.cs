using DoSelect.Application.Idempotency;
using DoSelect.Application.Shipping;
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

    /// <summary>
    /// 組長 PR #73 review item 3: an over-limit cart must be reported ineligible on the options
    /// screen with the catalogued shipping_constraint_exceeded code, not discovered at checkout.
    /// Store pickup gets a tight CVS-sized limit; home delivery keeps the fixture's generous one,
    /// so the same cart shows one blocked and one available option.
    /// </summary>
    [Fact]
    public async Task GetOptionsForCartAsync_WhenTheCartExceedsAMethodsPackageLimit_ReportsThatMethodIneligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var storePickup = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.StorePickup, 60m, 2000m, true, false);
        var homeDelivery = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 5000m, true, false);
        await ShippingServiceFixture.ReplaceProviderLimitAsync(
            context, ShippingProviderCodes.StorePickup, maxWeightKg: 5m, maxSideCm: 45m, maxTotalCm: 105m);
        // 10 kg parcel: over the CVS 5 kg ceiling, within home delivery's 20 kg.
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 1000m, weightKg: 10m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var blocked = options.Options.Single(option => option.MethodCode == storePickup.Code);
        Assert.False(blocked.IsEligible);
        Assert.Equal("shipping_constraint_exceeded", blocked.IneligibleReasonCode);
        var open = options.Options.Single(option => option.MethodCode == homeDelivery.Code);
        Assert.True(open.IsEligible);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WhenTheCartSitsExactlyOnTheLimit_StaysEligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var storePickup = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.StorePickup, 60m, 2000m, true, false);
        await ShippingServiceFixture.ReplaceProviderLimitAsync(
            context, ShippingProviderCodes.StorePickup, maxWeightKg: 5m, maxSideCm: 45m, maxTotalCm: 105m);
        // Exactly 5 kg and 35+35+35=105 total: boundaries are inclusive, mirroring checkout.
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(
            context, listPrice: 1000m, weightKg: 5m, sideCm: 35m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var option = options.Options.Single(candidate => candidate.MethodCode == storePickup.Code);
        Assert.True(option.IsEligible);
    }

    /// <summary>A SKU without dimensions makes the package incomplete; checkout rejects that for
    /// every limited method, so the options screen must say so up front instead of showing 可用.</summary>
    [Fact]
    public async Task GetOptionsForCartAsync_WhenASkuHasNoDimensions_ReportsMethodsIneligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var storePickup = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.StorePickup, 60m, 2000m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuWithoutDimensionsAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var option = options.Options.Single(candidate => candidate.MethodCode == storePickup.Code);
        Assert.False(option.IsEligible);
        Assert.Equal(ShippingErrorCodes.ShippingMethodNotAllowed, option.IneligibleReasonCode);
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
