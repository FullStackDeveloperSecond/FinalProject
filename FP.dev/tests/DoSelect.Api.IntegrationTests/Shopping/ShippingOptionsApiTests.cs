using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Shipping;

namespace DoSelect.Api.IntegrationTests.Shopping;

[Collection(nameof(CartApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ShippingOptionsApiTests
{
    private readonly CartApiFixture _fixture;

    public ShippingOptionsApiTests(CartApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetShippingOptions_WhenAnonymous_ReturnsSeededMethod()
    {
        ShippingMethod method;
        await using (var context = _fixture.CreateScopedContext())
        {
            method = await CheckoutApiSeeding.SeedHomeDeliveryShippingMethodAsync(context);
        }

        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/cart/shipping-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var methods = body.GetProperty("methods").EnumerateArray().ToList();
        var found = methods.Single(candidate => candidate.GetProperty("code").GetString() == method.Code);
        Assert.Equal("HomeDeliveryStandard", found.GetProperty("kind").GetString());
        Assert.Equal(150m, found.GetProperty("baseFee").GetDecimal());
    }
}
