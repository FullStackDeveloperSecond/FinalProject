using System.Net;
using System.Net.Http.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Shipping;

namespace DoSelect.Api.IntegrationTests.Shipping;

[Collection(nameof(ShippingApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ShippingAdminApiTests
{
    private readonly ShippingApiFixture _fixture;

    public ShippingAdminApiTests(ShippingApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListPackageLimitVersions_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/admin/shipping-providers/StorePickup/package-limit-versions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreatePackageLimitVersion_AsCatalogManager_ReturnsForbidden()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var body = new CreatePackageLimitVersionRequest("StorePickup", 5m, 45m, 45m, 45m, 105m, 20000m, null, null);

        using var response = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/shipping-providers/StorePickup/package-limit-versions")
            {
                Content = JsonContent.Create(body),
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenPublishPackageLimitVersion_AsOrderManager_Succeeds()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var providerCode = "HomeDelivery";
        var createBody = new CreatePackageLimitVersionRequest(providerCode, 20m, 150m, 150m, 150m, 150m, 50000m, null, null);

        using var createResponse = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/shipping-providers/{providerCode}/package-limit-versions")
            {
                Content = JsonContent.Create(createBody),
            });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PackageLimitVersionDto>();

        using var publishResponse = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/admin/shipping-providers/{providerCode}/package-limit-versions/{created!.PublicId}/actions/publish")
            {
                Content = JsonContent.Create(new PublishPackageLimitVersionRequest(created.RowVersion)),
            });

        publishResponse.EnsureSuccessStatusCode();
        var published = await publishResponse.Content.ReadFromJsonAsync<PackageLimitVersionDto>();
        Assert.Equal("Published", published!.Status);
    }

    [Fact]
    public async Task CreatePackageLimitVersion_OutsideTheSafeRange_ReturnsValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var body = new CreatePackageLimitVersionRequest("StorePickup", 5m, 46m, 45m, 45m, 105m, 20000m, null, null);

        using var response = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/shipping-providers/StorePickup/package-limit-versions")
            {
                Content = JsonContent.Create(body),
            });

        var (status, code, _) = await ShippingApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task CreateThenUpdateConvenienceStore_AsOrderManager_CanDeactivateIt()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var storeCode = $"API-TEST-{Guid.NewGuid():N}"[..24];
        var createBody = new CreateConvenienceStoreRequest("7-11", storeCode, "測試門市", "測試路 1 號", "台北市", "大安區");

        using var createResponse = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/convenience-stores") { Content = JsonContent.Create(createBody) });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ConvenienceStoreDto>();

        var updateBody = new UpdateConvenienceStoreRequest("測試門市", "測試路 1 號", "台北市", "大安區", false, created!.RowVersion);
        using var updateResponse = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/convenience-stores/{created.PublicId}")
            {
                Content = JsonContent.Create(updateBody),
            });

        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<ConvenienceStoreDto>();
        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task CreateConvenienceStore_WithADuplicateStoreCode_ReturnsConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var storeCode = $"API-DUP-{Guid.NewGuid():N}"[..24];
        var body = new CreateConvenienceStoreRequest("7-11", storeCode, "測試門市", "測試路 1 號", "台北市", "大安區");
        using var first = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/convenience-stores") { Content = JsonContent.Create(body) });
        first.EnsureSuccessStatusCode();

        using var second = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/convenience-stores") { Content = JsonContent.Create(body) });

        var (status, code, _) = await ShippingApiFixture.ReadProblemAsync(second);
        Assert.Equal((int)HttpStatusCode.Conflict, status);
        Assert.Equal("store_code_duplicate", code);
    }

    [Fact]
    public async Task ListConvenienceStores_AsCatalogManager_ReturnsOk()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);

        using var response = await client.GetAsync("/api/v1/admin/convenience-stores");

        response.EnsureSuccessStatusCode();
    }

    /// <summary>組長 PR #73 round-3, item 5：時間欄位的契約測試走完整 HTTP 綁定——沒有 Z 的字串綁成
    /// Unspecified、帶 offset 的綁成 Local，兩者在 Domain 都會丟例外變成 500，必須先擋成穩定的
    /// 400 validation_failed；帶 Z 的正常值則照常建立。</summary>
    [Theory]
    [InlineData("2027-01-01T00:00:00")]
    [InlineData("2027-01-01T08:00:00+08:00")]
    public async Task CreatePackageLimitVersion_WithANonUtcEffectiveTime_ReturnsValidationFailed(string effectiveFrom)
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var body = new
        {
            providerCode = "StorePickup",
            maxWeightKg = 5m,
            maxLengthCm = 45m,
            maxWidthCm = 45m,
            maxHeightCm = 45m,
            maxTotalCm = 105m,
            maxDeclaredValue = 20000m,
            effectiveFromUtc = effectiveFrom,
        };

        using var response = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/shipping-providers/StorePickup/package-limit-versions")
            {
                Content = JsonContent.Create(body),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreatePackageLimitVersion_WithAUtcEffectiveTime_IsAccepted()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var body = new
        {
            providerCode = "StorePickup",
            maxWeightKg = 5m,
            maxLengthCm = 45m,
            maxWidthCm = 45m,
            maxHeightCm = 45m,
            maxTotalCm = 105m,
            maxDeclaredValue = 20000m,
            effectiveFromUtc = DateTime.UtcNow.AddYears(5).ToString("O"),
        };

        using var response = await ShippingApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/shipping-providers/StorePickup/package-limit-versions")
            {
                Content = JsonContent.Create(body),
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>組長 PR #73 round-3, item 4：大頁碼在後台與前台列表都不得溢位成 500。</summary>
    [Fact]
    public async Task ListConvenienceStores_WithAnExtremePageNumber_ReturnsAnEmptyPage()
    {
        using var adminClient = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);

        using var adminResponse = await adminClient.GetAsync(
            $"/api/v1/admin/convenience-stores?pageNumber={int.MaxValue}&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var adminPage = await adminResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Empty(adminPage.GetProperty("items").EnumerateArray());

        using var publicClient = _fixture.CreateClient();
        using var publicResponse = await publicClient.GetAsync(
            $"/api/v1/convenience-stores?pageNumber={int.MaxValue}&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var publicPage = await publicResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Empty(publicPage.GetProperty("items").EnumerateArray());
    }
}
