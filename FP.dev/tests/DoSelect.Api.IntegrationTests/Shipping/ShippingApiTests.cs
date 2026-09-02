using System.Net;
using System.Net.Http.Json;
using DoSelect.Domain.Shipping;

namespace DoSelect.Api.IntegrationTests.Shipping;

[Collection(nameof(ShippingApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ShippingApiTests
{
    private readonly ShippingApiFixture _fixture;

    public ShippingApiTests(ShippingApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetShippingOptions_WithoutIdentity_ReturnsValidationFailed()
    {
        using var response = await _fixture.Client.GetAsync("/api/v1/cart/shipping-options");

        var (status, code, _) = await ShippingApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task GetShippingOptions_ForAGuestCart_ReturnsTheSeededMethods()
    {
        await using (var context = _fixture.CreateScopedContext())
        {
            await SeedShippingMethodAsync(context, "StorePickup", ShippingMethodKinds.StorePickup, 60m, 2000m, true, false);
            await SeedShippingMethodAsync(context, "HomeDelivery", ShippingMethodKinds.HomeDelivery, 150m, 5000m, true, false);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cart/shipping-options");
        request.Headers.Add("X-DoSelect-Guest-Cart-Key", ShippingApiFixture.UniqueGuestKey());
        using var response = await _fixture.Client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShippingOptionsResponse>();
        Assert.Equal(2, body!.Options.Count);
        Assert.All(body.Options, option =>
        {
            Assert.Equal(
                ["creditCard", "atm", "convenienceCode", "linePay", "applePay", "googlePay"],
                option.AllowedPaymentMethods.Take(6));
            Assert.DoesNotContain("prepaid", option.AllowedPaymentMethods);
        });
    }

    [Fact]
    public async Task GetShippingOptions_WhenCouponCodeExceedsContractMaximum_ReturnsValidationFailed()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/cart/shipping-options?couponCode={new string('A', 65)}");
        request.Headers.Add("X-DoSelect-Guest-Cart-Key", ShippingApiFixture.UniqueGuestKey());

        using var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListConvenienceStores_ReturnsSeededStores()
    {
        await using (var context = _fixture.CreateScopedContext())
        {
            var now = DateTime.UtcNow;
            context.ConvenienceStores.Add(new ConvenienceStore(
                Guid.CreateVersion7(), "7-11", "TEST-STORE-001", "測試門市", "測試路 1 號", "台北市", "大安區", true, now));
            await context.SaveChangesAsync();
        }

        using var response = await _fixture.Client.GetAsync("/api/v1/convenience-stores?city=台北市");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConvenienceStorePageResponse>();
        Assert.Contains(body!.Items, store => store.StoreCode == "TEST-STORE-001");
    }

    [Fact]
    public async Task ListConvenienceStores_WhenPageSizeExceedsTheContractMaximum_ReturnsValidationFailed()
    {
        using var response = await _fixture.Client.GetAsync("/api/v1/convenience-stores?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task SeedShippingMethodAsync(
        DoSelect.Infrastructure.Persistence.DoSelectDbContext context,
        string code,
        string kind,
        decimal baseFee,
        decimal? freeShippingThreshold,
        bool allowsCod,
        bool requiresPrepayment)
    {
        context.ShippingMethods.Add(new ShippingMethod(
            Guid.CreateVersion7(), code, code, kind, baseFee, freeShippingThreshold, allowsCod, requiresPrepayment,
            kind == ShippingMethodKinds.StorePickup ? ShippingProviderCodes.StorePickup : ShippingProviderCodes.HomeDelivery,
            DateTime.UtcNow));
        await context.SaveChangesAsync();
    }

    private sealed record ShippingOptionsResponse(List<ShippingOptionResponse> Options);

    private sealed record ShippingOptionResponse(List<string> AllowedPaymentMethods);

    private sealed record ConvenienceStorePageResponse(List<ConvenienceStoreItem> Items);

    private sealed record ConvenienceStoreItem(string StoreCode);
}
