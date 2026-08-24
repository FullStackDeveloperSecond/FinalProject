using System.Net;
using System.Net.Http.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Shipping;
using Xunit;

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
    public async Task GetShippingOptions_IsPubliclyAccessibleWithoutAuthentication()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/cart/shipping-options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListConvenienceStores_AllowsCatalogManagerReadOnly_ButForbidsCatalogManagerFromWriting()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);

        using var listResponse = await client.GetAsync("/api/v1/admin/convenience-stores");
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/convenience-stores")
        {
            Content = JsonContent.Create(new CreateConvenienceStoreRequest(
                "7-ELEVEN", ShippingApiFixture.UniqueCode("STORE"), "測試門市", "測試地址", "台北市", "信義區")),
        };
        using var createResponse = await ShippingApiFixture.SendWithAntiforgeryAsync(client, createRequest);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    [Fact]
    public async Task CreateConvenienceStore_ReturnsCreatedForOrderManager()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/convenience-stores")
        {
            Content = JsonContent.Create(new CreateConvenienceStoreRequest(
                "7-ELEVEN", ShippingApiFixture.UniqueCode("STORE"), "測試門市", "測試地址", "台北市", "信義區")),
        };

        using var response = await ShippingApiFixture.SendWithAntiforgeryAsync(client, createRequest);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AdminShipmentsBatch_RequiresOrderManagerPolicy_RejectsPlainMember()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/shipments/batches", new BatchShipmentRequest([]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
