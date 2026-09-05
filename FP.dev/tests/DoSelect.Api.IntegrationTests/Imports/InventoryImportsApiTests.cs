using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;

namespace DoSelect.Api.IntegrationTests.Imports;

/// <summary>
/// 組長 PR #89 item 1／2 的 HTTP 層證據：Preview 之後庫存被改過，Confirm 要回 409 並整批回滾；
/// rows 端點回的是明確型別的庫存預覽列。跑完整管線，因為 409 的錯誤碼映射與 DTO 的序列化都在這一層。
/// </summary>
[Collection(nameof(ProductImportsApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class InventoryImportsApiTests
{
    private readonly ProductImportsApiFixture _fixture;

    public InventoryImportsApiTests(ProductImportsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetRows_ReturnsTypedPreviewRowsWithBeforeDeltaAndAfter()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.InventoryManager);
        var (skuCode, _) = await _fixture.SeedSkuWithBalanceAsync(onHand: 5, reserved: 1);
        var batchId = await _fixture.StageInventoryPreviewBatchAsync(client, $"{skuCode},8,StocktakeDifference,\\N\r\n");

        using var response = await client.GetAsync($"/api/v1/admin/inventory-imports/{batchId}/rows");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(skuCode, row.GetProperty("skuCode").GetString());
        Assert.Equal("Update", row.GetProperty("action").GetString());
        Assert.Equal(5, row.GetProperty("beforeOnHand").GetInt32());
        Assert.Equal(1, row.GetProperty("reservedQuantity").GetInt32());
        Assert.Equal(8, row.GetProperty("targetOnHand").GetInt32());
        Assert.Equal(3, row.GetProperty("delta").GetInt32());
        Assert.Equal("StocktakeDifference", row.GetProperty("reasonCode").GetString());
    }

    /// <summary>
    /// NoChange 列也要驗 RowVersion：Preview 之後那個 SKU 被別的交易碰過（值剛好等於目標值），
    /// Confirm 必須回 409 concurrency_conflict，而且整批沒有任何東西寫進去。
    /// </summary>
    [Fact]
    public async Task Confirm_WhenAPreviewedBalanceMovedEvenToTheSameValue_Returns409AndRollsBackEverything()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.InventoryManager);
        var (movingCode, movingSkuId) = await _fixture.SeedSkuWithBalanceAsync(onHand: 5, reserved: 0);
        var (unchangedCode, unchangedSkuId) = await _fixture.SeedSkuWithBalanceAsync(onHand: 4, reserved: 0);
        var batchId = await _fixture.StageInventoryPreviewBatchAsync(
            client,
            $"{movingCode},7,StocktakeDifference,\\N\r\n{unchangedCode},4,DataCorrection,\\N\r\n");
        var rowVersion = await ProductImportsApiFixture.ReadBatchRowVersionAsync(client, $"/api/v1/admin/inventory-imports/{batchId}");

        await _fixture.TouchBalanceAsync(unchangedSkuId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/inventory-imports/{batchId}/actions/confirm")
        {
            Content = JsonContent.Create(new { rowVersion }),
        };
        request.Headers.Add("X-XSRF-TOKEN", await ProductImportsApiFixture.GetAdminAntiforgeryTokenAsync(client));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("concurrency_conflict", body.GetProperty("code").GetString());

        Assert.Equal(5, await _fixture.ReadOnHandAsync(movingSkuId));
        Assert.Equal(0, await _fixture.CountMovementsAsync(movingSkuId) + await _fixture.CountMovementsAsync(unchangedSkuId));
    }

    [Fact]
    public async Task Preview_AsCatalogManager_ReturnsForbidden()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("sku_code,target_on_hand,reason_code,note\r\n"), "adjustmentsFile", "stock.csv" },
            { new StringContent("1"), "templateVersion" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/inventory-imports/preview") { Content = form };
        request.Headers.Add("X-XSRF-TOKEN", await ProductImportsApiFixture.GetAdminAntiforgeryTokenAsync(client));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
