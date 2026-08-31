using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Inventory;
using DoSelect.Domain.Inventory;
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
        // UC-ADM-INV-01's manual release must persist an Audit Log entry to meet its acceptance
        // criteria. The central IAuditWriter now exists on dev, but this PR does not wire the
        // release up to it, so 組長's PR #36 round-3 ruling to withdraw the HTTP action still
        // stands — the wiring is deferred to a follow-up PR. This test exists so re-adding the
        // route without also wiring Audit Log gets caught immediately.
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
        // Same class of gap as the manual-release action: the central IAuditWriter exists on dev,
        // but UC-ADM-INV-01's real stock correction is not wired to it in this PR — 組長's PR #36
        // round-4 ruling withdrew the whole Resolve action (both Dismiss and the real-correction
        // path share it) until that follow-up PR lands.
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
    public void MovementTypeFilterCapMatchesTheVocabulary()
    {
        // InventoryMovementListRequest.MovementTypes is capped at "every type at once", but the
        // attribute has to spell the number out. Adding a type to InventoryMovementTypes without
        // raising the cap would reject a filter that names them all, before it ever reaches the
        // whitelist check — this is what stopped that from being noticed when CostChange was added.
        var cap = typeof(InventoryMovementListRequest)
            .GetProperty(nameof(InventoryMovementListRequest.MovementTypes))!
            .GetCustomAttributes(typeof(MaxLengthAttribute), inherit: false)
            .Cast<MaxLengthAttribute>()
            .Single();

        Assert.Equal(InventoryMovementTypes.All.Count, cap.Length);
    }

    [Fact]
    public async Task ListMovements_WhenFilteringByCostChange_ReturnsOnlyTheCostChangeRows()
    {
        // 組長 PR #36 ruling A1: CostChange is written by the SKU cost-change flow and consumed by the
        // M-15 turnover report, and the unfiltered movement list already returns it — so it has to be
        // an accepted `movementTypes` value too, otherwise the filter contradicts the data the same
        // endpoint just returned. Seeds one CostChange plus one Adjustment on the same SKU so a filter
        // that simply ignored the parameter would still fail this test.
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 8);
        var costChangePublicId = await _fixture.SeedMovementAsync(
            sku.Id, InventoryMovementTypes.CostChange, "sku_unit_cost_changed");
        await _fixture.SeedMovementAsync(sku.Id, InventoryMovementTypes.Adjustment, "reconciliation_correction");
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await client.GetAsync(
            $"/api/v1/admin/inventory/movements?skuPublicId={sku.PublicId}&movementTypes=CostChange");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        var item = Assert.Single(items);
        Assert.Equal(costChangePublicId, item.GetProperty("publicId").GetGuid());
        Assert.Equal(InventoryMovementTypes.CostChange, item.GetProperty("movementType").GetString());
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
