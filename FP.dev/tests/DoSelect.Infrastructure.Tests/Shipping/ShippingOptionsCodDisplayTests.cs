using DoSelect.Application.Idempotency;
using DoSelect.Application.Shipping;
using DoSelect.Application.Shopping;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;
using DoSelect.Infrastructure.Shopping;
using DoSelect.Infrastructure.Promotions;
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
        Assert.Contains(PaymentMethod.CashOnDelivery, option.AllowedPaymentMethods);
        Assert.Equal(
            PaymentMethodPolicy.PrepaidMethods.Append(PaymentMethod.CashOnDelivery),
            option.AllowedPaymentMethods);
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
        Assert.DoesNotContain(PaymentMethod.CashOnDelivery, option.AllowedPaymentMethods);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WhenCouponDropsFinalPayableToTheCeiling_OffersCashOnDelivery()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 20_500m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);
        var now = DateTime.UtcNow;
        var coupon = new Coupon(
            Guid.CreateVersion7(),
            new CouponCreation(
                "SAVE1000",
                "滿額折抵",
                CouponDiscountType.FixedAmount,
                1_000m,
                0m,
                null,
                now.AddDays(-1),
                now.AddDays(1),
                100,
                100,
                false,
                false,
                CouponScopeType.All),
            now);
        coupon.ActivateNow(CouponUsageState.Unused, now);
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        var options = await CreateService(context).GetOptionsForCartAsync(
            identity,
            CancellationToken.None,
            coupon.Code);

        var option = options.Options.Single(candidate => candidate.MethodCode == method.Code);
        Assert.Equal(150m, option.Fee);
        Assert.Contains(PaymentMethod.CashOnDelivery, option.AllowedPaymentMethods);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WithFreeShippingCoupon_OnlyNonAssemblyDeliveryRemainsEligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var regularMethod = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var assemblyMethod = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDeliveryAssembly, 300m, 999_999m, false, true);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 5_000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);
        var now = DateTime.UtcNow;
        var coupon = new Coupon(
            Guid.CreateVersion7(),
            new CouponCreation(
                "FREESHIPPING",
                "一般免運",
                CouponDiscountType.FreeShipping,
                null,
                0m,
                null,
                now.AddDays(-1),
                now.AddDays(1),
                100,
                100,
                false,
                false,
                CouponScopeType.All),
            now);
        coupon.ActivateNow(CouponUsageState.Unused, now);
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        var options = await CreateService(context).GetOptionsForCartAsync(
            identity,
            CancellationToken.None,
            coupon.Code);

        var regular = options.Options.Single(candidate => candidate.MethodCode == regularMethod.Code);
        Assert.True(regular.IsEligible);
        Assert.Equal(0m, regular.Fee);

        var assembly = options.Options.Single(candidate => candidate.MethodCode == assemblyMethod.Code);
        Assert.False(assembly.IsEligible);
        Assert.Equal(ShippingErrorCodes.ShippingMethodNotAllowed, assembly.IneligibleReasonCode);
    }

    [Fact]
    public async Task GetOptionsForCartAsync_WithAssemblyFreeShippingCoupon_OnlyAssemblyDeliveryRemainsEligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var regularMethod = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var assemblyMethod = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDeliveryAssembly, 300m, 999_999m, false, true);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 5_000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);
        await ShippingServiceFixture.AddAssemblyItemAsync(context, identity.GuestCartKey!, sku);
        var now = DateTime.UtcNow;
        var coupon = new Coupon(
            Guid.CreateVersion7(),
            new CouponCreation(
                "ASSEMBLYFREE",
                "組裝免運",
                CouponDiscountType.AssemblyFreeShipping,
                null,
                0m,
                null,
                now.AddDays(-1),
                now.AddDays(1),
                100,
                100,
                false,
                false,
                CouponScopeType.All),
            now);
        coupon.ActivateNow(CouponUsageState.Unused, now);
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        var options = await CreateService(context).GetOptionsForCartAsync(
            identity,
            CancellationToken.None,
            coupon.Code);

        var regular = options.Options.Single(candidate => candidate.MethodCode == regularMethod.Code);
        Assert.False(regular.IsEligible);
        Assert.Equal(ShippingErrorCodes.ShippingMethodNotAllowed, regular.IneligibleReasonCode);

        var assembly = options.Options.Single(candidate => candidate.MethodCode == assemblyMethod.Code);
        Assert.True(assembly.IsEligible);
        Assert.Equal(0m, assembly.Fee);
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
        Assert.DoesNotContain(PaymentMethod.CashOnDelivery, option.AllowedPaymentMethods);
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
        Assert.DoesNotContain(PaymentMethod.CashOnDelivery, option.AllowedPaymentMethods);
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
        Assert.DoesNotContain(PaymentMethod.CashOnDelivery, option.AllowedPaymentMethods);
    }

    private static EfShippingOptionsService CreateService(DoSelectDbContext context)
    {
        var cartService = new EfCartService(context, new EfIdempotencyExecutor(
            context,
            Options.Create(new IdempotencyOptions { ActorScopePepper = TestActorScopePepper }),
            TimeProvider.System));
        var couponService = new ApplyCartCouponService(
            cartService,
            new EfCartCouponLineReader(context, cartService),
            new CouponQuoteService(new CouponRuleReader(context), TimeProvider.System));
        return new EfShippingOptionsService(context, cartService, couponService);
    }

    private static Task AddItemAsync(DoSelectDbContext context, CartIdentity identity, DoSelect.Domain.Catalog.Sku sku, int quantity)
    {
        var cartService = new EfCartService(context, new EfIdempotencyExecutor(
            context,
            Options.Create(new IdempotencyOptions { ActorScopePepper = TestActorScopePepper }),
            TimeProvider.System));
        return cartService.AddItemAsync(identity, new AddCartItemRequest(sku.PublicId, quantity, null), CancellationToken.None);
    }
}
