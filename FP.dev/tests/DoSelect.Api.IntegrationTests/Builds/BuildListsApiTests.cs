using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;

namespace DoSelect.Api.IntegrationTests.Builds;

[Collection(nameof(BuildListsApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class BuildListsApiTests
{
    private readonly BuildListsApiFixture _fixture;

    public BuildListsApiTests(BuildListsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_ReturnsCreatedWithLocation_ForAValidRequest()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync(listPrice: 500m);

        using var response = await CreateAsync(client, "我的第一份清單", (sku.PublicId, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("我的第一份清單", body.GetProperty("name").GetString());
        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
        Assert.Equal(1000m, body.GetProperty("totals").GetProperty("merchandise").GetDecimal());
        Assert.Equal(1300m, body.GetProperty("totals").GetProperty("grandTotal").GetDecimal());
        // A single Storage SKU can never reach "compatible" under the canonical evaluator (every
        // singleton role plus Memory must be present first) — this test's subject is the
        // create/totals response shape, not the compatibility verdict.
        Assert.Equal("insufficientData", body.GetProperty("compatibility").GetProperty("overall").GetString());
    }

    [Fact]
    public async Task Create_ReturnsValidationProblem_ForAnEmptyName()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();

        using var response = await CreateAsync(client, string.Empty, (sku.PublicId, 1));

        var (status, code, _) = await BuildListsApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task Get_ReturnsUnauthorized_WithoutAMemberSession()
    {
        using var response = await _fixture.CreateClient().GetAsync($"/api/v1/build-lists/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_ForAnotherMembersBuildList()
    {
        var owner = await _fixture.CreateAuthenticatedMemberClientAsync();
        var stranger = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();

        using var createResponse = await CreateAsync(owner, "Owner's list", (sku.PublicId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();

        using var response = await stranger.GetAsync($"/api/v1/build-lists/{publicId}");

        var (status, code, _) = await BuildListsApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.NotFound, status);
        Assert.Equal("resource_not_found", code);
    }

    [Fact]
    public async Task List_ReturnsOnlyTheCallersLists_InThePageResultEnvelope()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();
        using (await CreateAsync(client, "List A", (sku.PublicId, 1))) { }
        using (await CreateAsync(client, "List B", (sku.PublicId, 1))) { }

        using var response = await client.GetAsync("/api/v1/build-lists?pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(1, body.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Update_RenamesAndReplacesItems_AndReturnsANewRowVersion()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var first = await _fixture.SeedSkuAsync();
        var second = await _fixture.SeedSkuAsync();

        using var createResponse = await CreateAsync(client, "Before", (first.PublicId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/build-lists/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                name = "After",
                items = new[] { new { skuPublicId = second.PublicId, quantity = 3 } },
                rowVersion,
            }),
        };
        using var response = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("After", body.GetProperty("name").GetString());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(3, item.GetProperty("quantity").GetInt32());
        Assert.NotEqual(rowVersion, body.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Update_ReturnsConcurrencyConflict_ForAStaleRowVersion()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();

        using var createResponse = await CreateAsync(client, "Original", (sku.PublicId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();
        var staleRowVersion = created.GetProperty("rowVersion").GetString();

        using var firstEditRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/build-lists/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                name = "First edit",
                items = new[] { new { skuPublicId = sku.PublicId, quantity = 2 } },
                rowVersion = staleRowVersion,
            }),
        };
        using (await BuildListsApiFixture.SendWithAntiforgeryAsync(client, firstEditRequest)) { }

        using var secondEditRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/build-lists/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                name = "Second edit",
                items = new[] { new { skuPublicId = sku.PublicId, quantity = 3 } },
                rowVersion = staleRowVersion,
            }),
        };
        using var response = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, secondEditRequest);

        var (status, code, _) = await BuildListsApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.Conflict, status);
        Assert.Equal("concurrency_conflict", code);
    }

    [Fact]
    public async Task Delete_RemovesTheBuildListFromSubsequentReads()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();

        using var createResponse = await CreateAsync(client, "ToDelete", (sku.PublicId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/build-lists/{publicId}")
        {
            Content = JsonContent.Create(new { rowVersion }),
        };
        using var deleteResponse = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getResponse = await client.GetAsync($"/api/v1/build-lists/{publicId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateShare_ThenPublicGet_ReturnsTheDeidentifiedSharedBuild()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();
        using var createResponse = await CreateAsync(client, "Shareable", (sku.PublicId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();

        using var shareRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/build-lists/{publicId}/share");
        using var shareResponse = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, shareRequest);
        Assert.Equal(HttpStatusCode.OK, shareResponse.StatusCode);
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shareUrl = share.GetProperty("url").GetString()!;

        using var publicResponse = await _fixture.CreateClient().GetAsync(shareUrl);

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var body = await publicResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Shareable", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("canCopy").GetBoolean());
        Assert.False(body.TryGetProperty("owner", out _));
    }

    [Fact]
    public async Task RevokeShare_InvalidatesThePublicLink()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();
        using var createResponse = await CreateAsync(client, "ToRevoke", (sku.PublicId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();

        using var shareRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/build-lists/{publicId}/share");
        using var shareResponse = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, shareRequest);
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shareUrl = share.GetProperty("url").GetString()!;

        using var revokeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/build-lists/{publicId}/share");
        using var revokeResponse = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, revokeRequest);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var publicResponse = await _fixture.CreateClient().GetAsync(shareUrl);
        var (status, code, _) = await BuildListsApiFixture.ReadProblemAsync(publicResponse);
        Assert.Equal((int)HttpStatusCode.NotFound, status);
        Assert.Equal("resource_not_found", code);
    }

    [Fact]
    public async Task PublicGet_ReturnsNotFound_ForAnUnknownToken()
    {
        using var response = await _fixture.CreateClient().GetAsync("/api/v1/build-shares/not-a-real-token");

        var (status, code, _) = await BuildListsApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.NotFound, status);
        Assert.Equal("resource_not_found", code);
    }

    [Fact]
    public async Task AddToCart_ReturnsTheUpdatedCart_ForAValidRequest()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var components = await _fixture.SeedCompleteBuildComponentsAsync();
        var sku = await _fixture.SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Storage,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.StorageInterface] = "M2_NVME",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        await _fixture.SeedInventoryAsync(sku.Id, 100);
        using var createResponse = await CreateAsync(
            client, "Cart Bound", [.. components.Select(component => (component.PublicId, 1)), (sku.PublicId, 1)]);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/build-lists/{publicId}/actions/add-to-cart")
        {
            Content = JsonContent.Create(new { quantity = 2, buildRowVersion = rowVersion }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // 2 units x 9 distinct SKUs (8 required components + 1 extra storage) = 18 rows.
        Assert.Equal(18, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task AddToCart_ReturnsInventoryInsufficient_WhenStockIsTooLow()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var components = await _fixture.SeedCompleteBuildComponentsAsync();
        var sku = await _fixture.SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Storage,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.StorageInterface] = "M2_NVME",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        await _fixture.SeedInventoryAsync(sku.Id, 1);
        using var createResponse = await CreateAsync(
            client, "Short Stock", [.. components.Select(component => (component.PublicId, 1)), (sku.PublicId, 1)]);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/build-lists/{publicId}/actions/add-to-cart")
        {
            Content = JsonContent.Create(new { quantity = 2, buildRowVersion = rowVersion }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, request);

        var (status, code, _) = await BuildListsApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.Conflict, status);
        Assert.Equal("inventory_insufficient", code);
    }

    [Fact]
    public async Task AddToCart_ReturnsValidationProblem_WithoutAnIdempotencyKeyHeader()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        var sku = await _fixture.SeedSkuAsync();
        await _fixture.SeedInventoryAsync(sku.Id, 100);
        using var createResponse = await CreateAsync(client, "No Key", (sku.PublicId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetString();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/build-lists/{publicId}/actions/add-to-cart")
        {
            Content = JsonContent.Create(new { quantity = 1, buildRowVersion = rowVersion }),
        };
        using var response = await BuildListsApiFixture.SendWithAntiforgeryAsync(client, request);

        var (status, code, _) = await BuildListsApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", code);
    }

    private static async Task<HttpResponseMessage> CreateAsync(
        HttpClient client, string name, params (Guid SkuPublicId, int Quantity)[] items)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/build-lists")
        {
            Content = JsonContent.Create(new
            {
                name,
                items = items.Select(item => new { skuPublicId = item.SkuPublicId, quantity = item.Quantity }).ToArray(),
            }),
        };
        return await BuildListsApiFixture.SendWithAntiforgeryAsync(client, request);
    }
}
