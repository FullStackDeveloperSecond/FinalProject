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
    public async Task ReleaseReservation_EndpointIsWithdrawn_ReturnsNotFound()
    {
        // UC-ADM-INV-01's manual release cannot meet its own acceptance criteria (must persist an
        // Audit Log entry) until the shared Audit Log subsystem exists — 組長's PR #36 round-3
        // ruling withdrew the HTTP action rather than ship it half-done. This test exists so
        // re-adding the route without also wiring Audit Log gets caught immediately.
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/inventory/reservations/{reservationPublicId}/actions/release")
        {
            Content = JsonContent.Create(new { reasonCode = "customer_cancelled", note = "n/a", rowVersion }),
        };
        using var response = await AdminInventoryApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBalances_WhenStockStateIsInvalid_ReturnsValidationFailed()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync("/api/v1/admin/inventory/balances?stockState=not-a-real-state");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListReservations_WhenStatusIsInvalid_ReturnsValidationFailed()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync("/api/v1/admin/inventory/reservations?status=not-a-real-status");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListReservations_WhenStatusIsAnUndefinedNumber_ReturnsValidationFailed()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        // Enum.TryParse accepts any numeric string convertible to the enum's underlying type even
        // when it names no defined member — "999" parses "successfully" without Enum.IsDefined.
        using var response = await client.GetAsync("/api/v1/admin/inventory/reservations?status=999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListReservations_WhenCursorIsMalformed_ReturnsValidationFailed()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync("/api/v1/admin/inventory/reservations?cursor=not-valid-base64!!!");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListBalances_WhenPageNumberIsExtreme_ReturnsOkWithAnEmptyPage()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync($"/api/v1/admin/inventory/balances?pageNumber={int.MaxValue / 100}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ListBalances_WhenPageSizeExceedsTheContractCap_ReturnsBadRequest()
    {
        // The API contract caps PageSize at 100 (組長 PR #36 review, item 6) — a caller sending 101
        // used to be silently clamped by the service instead of rejected.
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync("/api/v1/admin/inventory/balances?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResolveReconciliationCase_EndpointIsWithdrawn_ReturnsNotFound()
    {
        // Same class of gap as the manual-release action: UC-ADM-INV-01's real stock correction
        // can't meet its own Audit Log acceptance criteria yet — 組長's PR #36 round-4 ruling
        // withdrew the whole Resolve action (both Dismiss and the real-correction path share it).
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/inventory/reconciliation-cases/{Guid.NewGuid()}/actions/resolve")
        {
            Content = JsonContent.Create(new { dismissed = true, reason = "n/a", rowVersion = new byte[8] }),
        };
        using var response = await AdminInventoryApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListMovements_WhenMovementTypeIsUnknown_ReturnsValidationFailed()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync("/api/v1/admin/inventory/movements?movementTypes=NotARealType");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListMovements_WhenFromIsAfterTo_ReturnsValidationFailed()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();
        var to = DateTime.UtcNow;
        var from = to.AddDays(1);

        using var response = await client.GetAsync(
            $"/api/v1/admin/inventory/movements?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListReservations_WhenCursorWasIssuedUnderADifferentStatusFilter_ReturnsValidationFailed()
    {
        var skuA = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var skuB = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        await _fixture.SeedActiveReservationAsync(skuA.Id, quantity: 1);
        await _fixture.SeedActiveReservationAsync(skuB.Id, quantity: 1);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var firstPageResponse = await client.GetAsync("/api/v1/admin/inventory/reservations?pageSize=1");
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);

        // The cursor above was issued with no Status filter — reusing it with one now must be
        // rejected rather than silently splicing two different orderings together.
        using var response = await client.GetAsync(
            $"/api/v1/admin/inventory/reservations?status=Active&cursor={Uri.EscapeDataString(cursor!)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }
}

[CollectionDefinition(nameof(AdminInventoryApiCollection))]
public sealed class AdminInventoryApiCollection : ICollectionFixture<AdminInventoryApiFixture>;
