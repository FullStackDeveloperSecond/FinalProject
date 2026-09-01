using DoSelect.Application.Idempotency;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;
using DoSelect.Infrastructure.Shopping;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Shipping;

/// <summary>
/// The COD half of the shipping-options display. These used to target a dedicated
/// EfCodEligibilityService, but that was a parallel authority: dev's Checkout (#52) makes the
/// binding COD decision itself via PaymentAttemptPolicy.FindCashOnDeliveryRejection inside the
/// order-creation transaction, and a cart-time pre-check can never be authoritative anyway (the
/// cart can change before submit). The display now delegates to that same canonical policy, and
/// these tests pin the one contract left at this layer: AllowedPaymentMethods only offers
/// cashOnDelivery when the canonical policy would accept it.
/// </summary>
[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ShippingOptionsCodDisplayTests
{
    private const string TestActorScopePepper = "cod-display-tests-actor-scope-pepper-0000";
    private const string CashOnDelivery = "cashOnDelivery";

    [Fact]
    public async Task GetOptionsForCartAsync_WhenEverythingIsWithinLimits_OffersCashOnDelivery()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 5000m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var option = options.Options.Single(candidate => candidate.MethodCode == method.Code);
        Assert.Contains(CashOnDelivery, option.AllowedPaymentMethods);
    }

    /// <summary>購物車、訂單、付款與物流.md 貨到付款: "折扣後且包含運費等費用的最終應付金額不得超過 NT$20,000".</summary>
    [Fact]
    public async Task GetOptionsForCartAsync_WhenFinalPayableExceedsTwentyThousand_WithholdsCashOnDelivery()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 20_001m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var option = options.Options.Single(candidate => candidate.MethodCode == method.Code);
        Assert.DoesNotContain(CashOnDelivery, option.AllowedPaymentMethods);
    }

    /// <summary>購物車、訂單、付款與物流.md 貨到付款: "含組裝電腦，或任一 SKU 的 RequiresPrepayment=true 時不得使用貨到付款".</summary>
    [Fact]
    public async Task GetOptionsForCartAsync_WhenCartContainsAPrepaymentRequiredSku_WithholdsCashOnDeliveryRegardlessOfAmount()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 100m, requiresPrepayment: true);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var option = options.Options.Single(candidate => candidate.MethodCode == method.Code);
        Assert.DoesNotContain(CashOnDelivery, option.AllowedPaymentMethods);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WhenCartContainsAnAssemblyItem_WithholdsCashOnDeliveryRegardlessOfAmount()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);
        await ShippingServiceFixture.AddAssemblyItemAsync(context, identity.GuestCartKey!, sku);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var option = options.Options.Single(candidate => candidate.MethodCode == method.Code);
        Assert.DoesNotContain(CashOnDelivery, option.AllowedPaymentMethods);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WhenTheMethodDoesNotAllowCod_WithholdsCashOnDelivery()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, allowsCod: false, requiresPrepayment: false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var options = await CreateService(context).GetOptionsForCartAsync(identity, CancellationToken.None);

        var option = options.Options.Single(candidate => candidate.MethodCode == method.Code);
        Assert.DoesNotContain(CashOnDelivery, option.AllowedPaymentMethods);
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
