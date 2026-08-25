using DoSelect.Application.Idempotency;
using DoSelect.Application.Shopping;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;
using DoSelect.Infrastructure.Shopping;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Shipping;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CodEligibilityServiceTests
{
    private const string TestActorScopePepper = "cod-eligibility-tests-actor-scope-pepper-000";

    [Fact]
    public async Task EvaluateAsync_WhenEverythingIsWithinLimits_IsEligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 5000m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 1000m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var result = await CreateService(context).EvaluateAsync(identity, method.Code, CancellationToken.None);

        Assert.True(result.IsEligible);
        Assert.Null(result.IneligibleReasonCode);
    }

    /// <summary>購物車、訂單、付款與物流.md 貨到付款: "折扣後且包含運費等費用的最終應付金額不得超過 NT$20,000".</summary>
    [Fact]
    public async Task EvaluateAsync_WhenFinalPayableExceedsTwentyThousand_IsNotEligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 20_001m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var result = await CreateService(context).EvaluateAsync(identity, method.Code, CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal("payment_cod_amount_exceeded", result.IneligibleReasonCode);
    }

    /// <summary>購物車、訂單、付款與物流.md 貨到付款: "含組裝電腦，或任一 SKU 的 RequiresPrepayment=true 時不得使用貨到付款".</summary>
    [Fact]
    public async Task EvaluateAsync_WhenCartContainsAPrepaymentRequiredSku_IsNotEligibleRegardlessOfAmount()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 100m, requiresPrepayment: true);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var result = await CreateService(context).EvaluateAsync(identity, method.Code, CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal("payment_cod_restricted_item", result.IneligibleReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCartContainsAnAssemblyItem_IsNotEligibleRegardlessOfAmount()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDelivery, 150m, 999_999m, true, false);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);
        await ShippingServiceFixture.AddAssemblyItemAsync(context, identity.GuestCartKey!, sku);

        var result = await CreateService(context).EvaluateAsync(identity, method.Code, CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal("payment_cod_restricted_item", result.IneligibleReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_WhenMethodDoesNotAllowCod_IsNotEligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var method = await ShippingServiceFixture.SeedShippingMethodAsync(
            context, ShippingMethodKinds.HomeDeliveryAssembly, 300m, 30000m, false, true);
        var sku = await ShippingServiceFixture.SeedPublishedSkuAsync(context, listPrice: 100m);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());
        await AddItemAsync(context, identity, sku, quantity: 1);

        var result = await CreateService(context).EvaluateAsync(identity, method.Code, CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal("payment_method_not_allowed", result.IneligibleReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_WhenMethodCodeIsUnknown_IsNotEligible()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearShippingMethodsAsync(context);
        var identity = new CartIdentity(null, ShippingServiceFixture.UniqueGuestKey());

        var result = await CreateService(context).EvaluateAsync(identity, "DoesNotExist", CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal("shipping_method_not_allowed", result.IneligibleReasonCode);
    }

    private static EfCodEligibilityService CreateService(DoSelectDbContext context) =>
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
