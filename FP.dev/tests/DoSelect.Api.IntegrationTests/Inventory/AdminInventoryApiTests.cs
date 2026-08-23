using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DoSelect.Api.IntegrationTests.Inventory;

[Collection(nameof(AdminInventoryApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AdminInventoryApiTests
{
    private readonly AdminInventoryApiFixture _fixture;

    public AdminInventoryApiTests(AdminInventoryApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListBalances_WhenAuthenticatedAsInventoryManager_ReturnsTheSeededBalance()
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 12);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync($"/api/v1/admin/inventory/balances?q={sku.SkuCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("skuPublicId").GetGuid() == sku.PublicId);
    }

    [Fact]
    public async Task ListBalances_WhenCallerLacksInventoryManagerRole_ReturnsForbidden()
    {
        var client = await _fixture.CreateAuthenticatedUnrelatedAdminClientAsync();

        using var response = await client.GetAsync("/api/v1/admin/inventory/balances");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListBalances_WhenAnonymous_ReturnsUnauthorized()
    {
        var client = _fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/admin/inventory/balances");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseReservation_WhenActive_ClearsReservedQuantityAndReturnsNoContent()
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/inventory/reservations/{reservationPublicId}/actions/release")
        {
            Content = JsonContent.Create(new { reasonCode = "member_cancelled", note = "customer requested", rowVersion }),
        };
        using var response = await AdminInventoryApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var balancesResponse = await client.GetAsync($"/api/v1/admin/inventory/balances?q={sku.SkuCode}");
        var balances = await balancesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var balanceItem = balances.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("skuPublicId").GetGuid() == sku.PublicId);
        Assert.Equal(0, balanceItem.GetProperty("reserved").GetInt32());
        Assert.Equal(10, balanceItem.GetProperty("available").GetInt32());
    }

    [Fact]
    public async Task ReleaseReservation_WhenAlreadyProcessed_ReturnsConflictWithReservationNotActive()
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var firstRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/inventory/reservations/{reservationPublicId}/actions/release")
        {
            Content = JsonContent.Create(new { reasonCode = "member_cancelled", note = "n/a", rowVersion }),
        };
        using var firstResponse = await AdminInventoryApiFixture.SendWithAntiforgeryAsync(client, firstRequest);
        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);

        using var secondRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/inventory/reservations/{reservationPublicId}/actions/release")
        {
            Content = JsonContent.Create(new { reasonCode = "member_cancelled", note = "n/a", rowVersion }),
        };
        using var secondResponse = await AdminInventoryApiFixture.SendWithAntiforgeryAsync(client, secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var problem = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("inventory_reservation_not_active", problem.GetProperty("code").GetString());
    }
}

[CollectionDefinition(nameof(AdminInventoryApiCollection))]
public sealed class AdminInventoryApiCollection : ICollectionFixture<AdminInventoryApiFixture>;
