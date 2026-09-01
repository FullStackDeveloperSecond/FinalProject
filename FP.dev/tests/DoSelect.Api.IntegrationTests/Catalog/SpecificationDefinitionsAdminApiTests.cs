using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Catalog;

namespace DoSelect.Api.IntegrationTests.Catalog;

/// <summary>
/// HTTP-layer coverage for A-09 `/api/v1/admin/specification-definitions`: routing, model binding,
/// base64 round-tripping of `byte[] RowVersion`, and ProblemDetails status mapping for the two
/// specification error codes. The business rules themselves are covered in-process by
/// <c>SpecificationDefinitionAdminServiceTests</c>; 401/403 for these routes live in the shared
/// <c>CatalogAdminAuthorizationTests</c> matrix.
/// </summary>
[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class SpecificationDefinitionsAdminApiTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public SpecificationDefinitionsAdminApiTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateThenList_RoundTripsTheDefinitionAndItsOptions()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(client);
        var semanticKey = CatalogAdminApiFixture.UniqueCode("SPEC");

        using var created = await PostDefinitionAsync(client, category.PublicId, semanticKey, valueType: "Option", options:
        [
            new { code = "A", displayNameZhTw = "選項A", sortOrder = 0, isActive = true },
            new { code = "B", displayNameZhTw = "選項B", sortOrder = 1, isActive = true },
        ]);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        // Semantic keys are normalized to upper case, like every other catalog code.
        Assert.Equal(semanticKey.ToUpperInvariant(), body.GetProperty("semanticKey").GetString());
        Assert.Equal(2, body.GetProperty("options").GetArrayLength());

        using var listResponse = await client.GetAsync(
            $"/api/v1/admin/specification-definitions?categoryPublicId={category.PublicId}&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            list.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("semanticKey").GetString() == semanticKey.ToUpperInvariant());
    }

    [Fact]
    public async Task Create_WhenTheSemanticKeyRepeats_Returns409SemanticKeyDuplicate()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(client);
        var semanticKey = CatalogAdminApiFixture.UniqueCode("SPEC");
        using (var first = await PostDefinitionAsync(client, category.PublicId, semanticKey))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using var duplicate = await PostDefinitionAsync(client, category.PublicId, semanticKey);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("specification_semantic_key_duplicate", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_Returns409ConcurrencyConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(client);
        using var created = await PostDefinitionAsync(
            client, category.PublicId, CatalogAdminApiFixture.UniqueCode("SPEC"));
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = body.GetProperty("publicId").GetGuid();
        var staleRowVersion = body.GetProperty("rowVersion").GetString();

        using (var firstUpdate = await PutDefinitionAsync(client, publicId, "先改一次", staleRowVersion))
        {
            Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);
        }

        using var conflict = await PutDefinitionAsync(client, publicId, "再改一次", staleRowVersion);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disable_WhenTheDefinitionIsProtected_Returns409DefinitionReferenced()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        // A category code the fixed compatibility engine depends on: its required semantic keys
        // are protected by CompatibilityCatalogContract.
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(
            client, CompatibilityCatalogContract.Categories.Cpu);
        using var created = await PostDefinitionAsync(
            client, category.PublicId, CompatibilityCatalogContract.SemanticKeys.CpuGeneration);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isProtected").GetBoolean());

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/admin/specification-definitions/{body.GetProperty("publicId").GetGuid()}/actions/disable")
            {
                Content = JsonContent.Create(new { rowVersion = body.GetProperty("rowVersion").GetString() }),
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("specification_definition_referenced", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disable_WhenTheDefinitionIsOrdinary_Returns200AndMarksItInactive()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(client);
        using var created = await PostDefinitionAsync(
            client, category.PublicId, CatalogAdminApiFixture.UniqueCode("SPEC"));
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/admin/specification-definitions/{body.GetProperty("publicId").GetGuid()}/actions/disable")
            {
                Content = JsonContent.Create(new { rowVersion = body.GetProperty("rowVersion").GetString() }),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disabled = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(disabled.GetProperty("isActive").GetBoolean());
    }

    /// <summary>沒有刪除端點是刻意的：資料字典要求以停用代替刪除。</summary>
    [Fact]
    public async Task Delete_IsNotRouted()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/v1/admin/specification-definitions/{Guid.CreateVersion7()}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostDefinitionAsync(
        HttpClient client,
        Guid categoryPublicId,
        string semanticKey,
        string valueType = "Decimal",
        object[]? options = null) =>
        CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/specification-definitions")
            {
                Content = JsonContent.Create(new
                {
                    categoryPublicId,
                    semanticKey,
                    displayNameZhTw = "測試規格",
                    valueType,
                    unitCode = (string?)null,
                    isRequired = true,
                    allowsMultiple = false,
                    sortOrder = 0,
                    options = options ?? [],
                }),
            });

    private static Task<HttpResponseMessage> PutDefinitionAsync(
        HttpClient client,
        Guid publicId,
        string displayNameZhTw,
        string? rowVersion) =>
        CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/specification-definitions/{publicId}")
            {
                Content = JsonContent.Create(new
                {
                    displayNameZhTw,
                    isRequired = true,
                    sortOrder = 0,
                    options = Array.Empty<object>(),
                    rowVersion,
                }),
            });
}
