using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Inventory;
using DoSelect.Api.Security;
using DoSelect.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
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

    // ---------------------------------------------------------------------------------------
    // UC-ADM-INV-01 人工釋放（Endpoint 目錄「UC-ADM-INV-01 保留」列）。PR #36 round 3 撤回的路由，
    // 現在釋放與中央稽核同一次 SaveChanges 落地後補回。
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ReleaseReservation_WhenActive_ReleasesOnceAndPersistsMovementAndAuditLog()
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await ReleaseAsync(client, reservationPublicId, rowVersion, note: "客戶來電取消");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));

        await using var context = AdminInventoryApiFixture.CreateContext();
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservationPublicId);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(10, balance.AvailableQuantity);
        Assert.True(await context.InventoryMovements.AsNoTracking()
            .AnyAsync(m => m.ReservationId == reservation.Id && m.MovementType == InventoryMovementTypes.Release));

        // 驗收「保存 InventoryMovement 與 Audit Log」：稽核帶著這次請求的 Correlation／Trace，
        // 而且角色快照來自真正的 UserRoles。
        var audit = await context.AuditLogs.AsNoTracking().SingleAsync(a => a.ResourcePublicId == reservationPublicId);
        Assert.Equal("inventory_reservation.release", audit.Action);
        Assert.Equal("InventoryReservation", audit.ResourceType);
        Assert.Equal(correlationId, audit.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(audit.TraceId));
        Assert.Contains(DoSelectRoles.InventoryManager, audit.ActorRolesJson);
        Assert.Contains("客戶來電取消", audit.ChangedFieldsJson);
    }

    /// <summary>驗收：「同一請求重送，冪等識別相同，不得再次減少 ReservedQuantity」——RowVersion 就是那個識別。</summary>
    [Fact]
    public async Task ReleaseReservation_WhenResentWithTheSameRowVersion_ReturnsConflictAndDoesNotReleaseTwice()
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var first = await ReleaseAsync(client, reservationPublicId, rowVersion, note: "n/a");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var second = await ReleaseAsync(client, reservationPublicId, rowVersion, note: "n/a");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("inventory_reservation_not_active", problem.GetProperty("code").GetString());

        await using var context = AdminInventoryApiFixture.CreateContext();
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(1, await context.InventoryMovements.AsNoTracking()
            .CountAsync(m => m.SkuId == sku.Id && m.MovementType == InventoryMovementTypes.Release));
        Assert.Equal(1, await context.AuditLogs.AsNoTracking().CountAsync(a => a.ResourcePublicId == reservationPublicId));
    }

    /// <summary>驗收：「管理員未填原因，When 送出，Then API 拒絕操作且庫存數量不變」。</summary>
    [Theory]
    [InlineData("customer_cancelled", "")]
    [InlineData("", "n/a")]
    [InlineData("member_cancelled", "n/a")]
    public async Task ReleaseReservation_WhenReasonOrNoteIsMissingOrInvalid_ReturnsValidationFailedAndLeavesStockUnchanged(
        string reasonCode, string note)
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await ReleaseAsync(client, reservationPublicId, rowVersion, note, reasonCode);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());

        await using var context = AdminInventoryApiFixture.CreateContext();
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservationPublicId);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(3, balance.ReservedQuantity);
        Assert.False(await context.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reservationPublicId));
    }

    [Fact]
    public async Task ReleaseReservation_WhenCallerLacksInventoryManagerRole_ReturnsForbidden()
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedUnrelatedAdminClientAsync();

        using var response = await ReleaseAsync(client, reservationPublicId, rowVersion, note: "n/a");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseReservation_WhenReservationDoesNotExist_ReturnsNotFound()
    {
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        using var response = await ReleaseAsync(client, Guid.NewGuid(), new byte[8], note: "n/a");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A-12 頁只依 availableActions 決定要不要顯示釋放按鈕：Active 有、終態沒有。</summary>
    [Fact]
    public async Task ListReservations_AdvertisesReleaseOnlyWhileTheReservationIsActive()
    {
        var sku = await _fixture.SeedSkuWithBalanceAsync(onHandQuantity: 10);
        var (reservationPublicId, rowVersion) = await _fixture.SeedActiveReservationAsync(sku.Id, quantity: 3);
        var client = await _fixture.CreateAuthenticatedInventoryManagerClientAsync();

        Assert.Equal(["release"], await ReadAvailableActionsAsync(client, reservationPublicId, "Active"));

        using var release = await ReleaseAsync(client, reservationPublicId, rowVersion, note: "n/a");
        Assert.Equal(HttpStatusCode.NoContent, release.StatusCode);

        Assert.Empty(await ReadAvailableActionsAsync(client, reservationPublicId, "Released"));
    }

    private static async Task<HttpResponseMessage> ReleaseAsync(
        HttpClient client, Guid reservationPublicId, byte[] rowVersion, string note, string reasonCode = "customer_cancelled")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/inventory/reservations/{reservationPublicId}/actions/release")
        {
            Content = JsonContent.Create(new { reasonCode, note, rowVersion }),
        };
        return await AdminInventoryApiFixture.SendWithAntiforgeryAsync(client, request);
    }

    private static async Task<string[]> ReadAvailableActionsAsync(HttpClient client, Guid reservationPublicId, string status)
    {
        using var response = await client.GetAsync($"/api/v1/admin/inventory/reservations?status={status}&pageSize=100");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("publicId").GetGuid() == reservationPublicId);
        return row.GetProperty("availableActions").EnumerateArray().Select(action => action.GetString()!).ToArray();
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
