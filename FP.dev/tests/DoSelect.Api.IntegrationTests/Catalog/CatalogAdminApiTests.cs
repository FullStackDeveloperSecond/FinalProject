using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Catalog;

/// <summary>
/// HTTP-layer coverage for the 5 admin Catalog controllers: routing, query-string/route
/// model binding, JSON request/response (de)serialization (including base64 round-tripping
/// of `byte[] RowVersion`), authorization, and ProblemDetails status-code mapping. The
/// equivalent business rules are already unit-tested in-process at
/// DoSelect.Infrastructure.Tests (CatalogAdminServiceTests) — this layer only proves the
/// wiring on top of that, so coverage here is representative, not exhaustive. Every test
/// signs in as an admin with the CatalogManager role via
/// <see cref="CatalogAdminApiFixture.CreateAuthenticatedAdminClientAsync"/> — see
/// <see cref="CatalogAdminAuthorizationTests"/> for the 401/403/role-matrix coverage itself.
/// [Trait("Category", "RequiresSqlServer")] SQL Server Provider-backed
/// (see AssemblyInfo/ci.yml — this suite is excluded from required Linux CI per DEV-07).
/// </summary>
[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class BrandsAdminApiTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public BrandsAdminApiTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task List_ReturnsOkWithPageResult()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        await CatalogAdminApiSeeding.CreateBrandAsync(client);

        using var response = await client.GetAsync("/api/v1/admin/brands?pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("items").GetArrayLength() >= 0);
        Assert.Equal(5, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Create_WhenCodeIsNew_Returns201WithLocation()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await CatalogAdminApiSeeding.PostBrandAsync(client, CatalogAdminApiFixture.UniqueCode("BRAND"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers.Location?.ToString()));
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_WhenCodeAlreadyExists_Returns409WithBrandCodeDuplicate()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var code = CatalogAdminApiFixture.UniqueCode("BRAND");
        using var first = await CatalogAdminApiSeeding.PostBrandAsync(client, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostBrandAsync(client, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("brand_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsOkWithNewRowVersion()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var brand = await CatalogAdminApiSeeding.CreateBrandAsync(client);

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/brands/{brand.PublicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "更新後名稱",
                description = (string?)null,
                websiteUrl = (string?)null,
                sortOrder = 1,
                isActive = true,
                rowVersion = brand.RowVersion,
            }),
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
        Assert.NotEqual(brand.RowVersion, body.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var brand = await CatalogAdminApiSeeding.CreateBrandAsync(client);
        using var firstUpdate = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/brands/{brand.PublicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "第一次更新",
                description = (string?)null,
                websiteUrl = (string?)null,
                sortOrder = 0,
                isActive = true,
                rowVersion = brand.RowVersion,
            }),
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/brands/{brand.PublicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "用舊版本更新",
                description = (string?)null,
                websiteUrl = (string?)null,
                sortOrder = 0,
                isActive = true,
                rowVersion = brand.RowVersion,
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CategoriesAdminApiTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public CategoriesAdminApiTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task List_ReturnsOkWithPageResult()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        await CatalogAdminApiSeeding.CreateCategoryAsync(client);

        using var response = await client.GetAsync("/api/v1/admin/categories?pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Create_WhenCodeIsNew_Returns201WithLocation()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await CatalogAdminApiSeeding.PostCategoryAsync(client, CatalogAdminApiFixture.UniqueCode("CAT"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers.Location?.ToString()));
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_WhenCodeAlreadyExists_Returns409WithCategoryCodeDuplicate()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var code = CatalogAdminApiFixture.UniqueCode("CAT");
        using var first = await CatalogAdminApiSeeding.PostCategoryAsync(client, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostCategoryAsync(client, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("category_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Create_WhenParentCategoryPublicIdIsUnknown_Returns400WithCategoryParentInvalid()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var code = CatalogAdminApiFixture.UniqueCode("CAT");
        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/categories")
        {
            Content = JsonContent.Create(new
            {
                code,
                nameZhTw = "測試分類",
                slug = "slug-" + Guid.NewGuid().ToString("N")[..12],
                description = (string?)null,
                parentCategoryPublicId = Guid.NewGuid(),
                sortOrder = 0,
                isActive = true,
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("category_parent_invalid", problemCode);
    }

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsOkWithNewRowVersion()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(client);

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/categories/{category.PublicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "更新後名稱",
                slug = category.Slug,
                description = (string?)null,
                parentCategoryPublicId = (Guid?)null,
                sortOrder = 1,
                isActive = true,
                rowVersion = category.RowVersion,
            }),
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(client);
        using var firstUpdate = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/categories/{category.PublicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "第一次更新",
                slug = category.Slug,
                description = (string?)null,
                parentCategoryPublicId = (Guid?)null,
                sortOrder = 0,
                isActive = true,
                rowVersion = category.RowVersion,
            }),
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/categories/{category.PublicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "用舊版本更新",
                slug = category.Slug,
                description = (string?)null,
                parentCategoryPublicId = (Guid?)null,
                sortOrder = 0,
                isActive = true,
                rowVersion = category.RowVersion,
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class TagsAdminApiTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public TagsAdminApiTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task List_ReturnsOkWithPageResult()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        await CatalogAdminApiSeeding.PostTagAsync(client, CatalogAdminApiFixture.UniqueCode("TAG"));

        using var response = await client.GetAsync("/api/v1/admin/tags?pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Create_WhenCodeIsNew_Returns201WithLocation()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await CatalogAdminApiSeeding.PostTagAsync(client, CatalogAdminApiFixture.UniqueCode("TAG"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers.Location?.ToString()));
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_WhenCodeAlreadyExists_Returns409WithTagCodeDuplicate()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var code = CatalogAdminApiFixture.UniqueCode("TAG");
        using var first = await CatalogAdminApiSeeding.PostTagAsync(client, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostTagAsync(client, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("tag_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsOkWithNewRowVersion()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var created = await CatalogAdminApiSeeding.PostTagAsync(client, CatalogAdminApiFixture.UniqueCode("TAG"));
        var tag = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = tag.GetProperty("publicId").GetGuid();
        var rowVersion = tag.GetProperty("rowVersion").GetString();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/tags/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "更新後名稱",
                sortOrder = 1,
                isActive = true,
                rowVersion,
            }),
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var created = await CatalogAdminApiSeeding.PostTagAsync(client, CatalogAdminApiFixture.UniqueCode("TAG"));
        var tag = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = tag.GetProperty("publicId").GetGuid();
        var rowVersion = tag.GetProperty("rowVersion").GetString();

        using var firstUpdate = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/tags/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "第一次更新",
                sortOrder = 0,
                isActive = true,
                rowVersion,
            }),
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/tags/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "用舊版本更新",
                sortOrder = 0,
                isActive = true,
                rowVersion,
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AdminProductsApiTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public AdminProductsApiTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task List_WithStatusFilter_ReturnsOkWithPageResult()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);
        await CatalogAdminApiSeeding.PostProductAsync(client, brandId, categoryId);

        using var response = await client.GetAsync("/api/v1/admin/products?statuses=Draft&pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task GetById_WhenNotFound_Returns404WithResourceNotFound()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await client.GetAsync($"/api/v1/admin/products/{Guid.NewGuid()}");
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(404, status);
        Assert.Equal("resource_not_found", problemCode);
    }

    [Fact]
    public async Task Create_WhenValid_Returns201WithLocationPointingToGetById()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);

        using var response = await CatalogAdminApiSeeding.PostProductAsync(client, brandId, categoryId);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var followUp = await client.GetAsync(response.Headers.Location);
        var fetched = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        Assert.Equal(publicId, fetched.GetProperty("publicId").GetGuid());
    }

    [Fact]
    public async Task Create_WhenBrandPublicIdIsUnknown_Returns400WithReferenceNotFound()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (_, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);

        using var response = await CatalogAdminApiSeeding.PostProductAsync(client, Guid.NewGuid(), categoryId);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("reference_not_found", problemCode);
    }

    [Fact]
    public async Task Create_WhenProductCodeAlreadyExists_Returns409WithProductCodeDuplicate()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);
        var code = CatalogAdminApiFixture.UniqueCode("PROD");
        using var first = await CatalogAdminApiSeeding.PostProductAsync(client, brandId, categoryId, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostProductAsync(client, brandId, categoryId, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("product_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Update_WhenValid_ReturnsOkWithNewRowVersion()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);
        using var created = await CatalogAdminApiSeeding.PostProductAsync(client, brandId, categoryId);
        var product = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = product.GetProperty("publicId").GetGuid();
        var rowVersion = product.GetProperty("rowVersion").GetString();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/products/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "更新後名稱",
                brandPublicId = brandId,
                categoryPublicId = categoryId,
                descriptionZhTw = (string?)null,
                warrantyMonths = (int?)null,
                tagPublicIds = Array.Empty<Guid>(),
                status = "Draft",
                rowVersion,
            }),
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);
        using var created = await CatalogAdminApiSeeding.PostProductAsync(client, brandId, categoryId);
        var product = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = product.GetProperty("publicId").GetGuid();
        var rowVersion = product.GetProperty("rowVersion").GetString();

        using var firstUpdate = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/products/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "第一次更新",
                brandPublicId = brandId,
                categoryPublicId = categoryId,
                descriptionZhTw = (string?)null,
                warrantyMonths = (int?)null,
                tagPublicIds = Array.Empty<Guid>(),
                status = "Draft",
                rowVersion,
            }),
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/products/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "用舊版本更新",
                brandPublicId = brandId,
                categoryPublicId = categoryId,
                descriptionZhTw = (string?)null,
                warrantyMonths = (int?)null,
                tagPublicIds = Array.Empty<Guid>(),
                status = "Draft",
                rowVersion,
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AdminSkusApiTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public AdminSkusApiTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_WhenValid_Returns201WithLocationPointingToGetById()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var response = await CatalogAdminApiSeeding.PostSkuAsync(client, productId);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var followUp = await client.GetAsync(response.Headers.Location);
        var fetched = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        Assert.Equal(publicId, fetched.GetProperty("publicId").GetGuid());
    }

    [Fact]
    public async Task Create_WhenSkuCodeAlreadyExists_Returns409WithSkuCodeDuplicate()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        var code = CatalogAdminApiFixture.UniqueCode("SKU");
        using var first = await CatalogAdminApiSeeding.PostSkuAsync(client, productId, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostSkuAsync(client, productId, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("sku_code_duplicate", problemCode);
    }

    [Fact]
    public async Task GetById_WhenNotFound_Returns404WithResourceNotFound()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await client.GetAsync($"/api/v1/admin/skus/{Guid.NewGuid()}");
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(404, status);
        Assert.Equal("resource_not_found", problemCode);
    }

    [Fact]
    public async Task Update_WhenValid_ReturnsOkWithNewRowVersion()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "更新後名稱",
                listPrice = 11_000m,
                unitCost = 7_500m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
                rowVersion,
            }),
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var firstUpdate = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "第一次更新",
                listPrice = 10_000m,
                unitCost = 7_000m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
                rowVersion,
            }),
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "用舊版本更新",
                listPrice = 10_000m,
                unitCost = 7_000m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
                rowVersion,
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }

    [Fact]
    public async Task Delete_WhenDraftAndUnreferenced_Returns204()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new { rowVersion }),
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenSkuHasInventoryBalance_Returns409WithSkuDeleteReferenced()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        // No admin HTTP endpoint exists yet for inventory balances (separate M-10 work
        // package), so this one seed step reaches into the DbContext directly — the
        // assertion below still only goes through HTTP.
        await using (var context = _fixture.CreateScopedContext())
        {
            var skuId = await context.Skus
                .Where(candidate => candidate.PublicId == publicId)
                .Select(candidate => candidate.Id)
                .FirstAsync();
            context.InventoryBalances.Add(new InventoryBalance(Guid.CreateVersion7(), skuId, 5, 1, DateTime.UtcNow));
            await context.SaveChangesAsync();
        }

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new { rowVersion }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("sku_delete_referenced", problemCode);
    }

    [Fact]
    public async Task Delete_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var firstUpdate = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "改一次讓 RowVersion 過期",
                listPrice = 10_000m,
                unitCost = 7_000m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
                rowVersion,
            }),
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new { rowVersion }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

/// <summary>
/// Per Terry-PR12修正書.md P1: the 5 admin Catalog controllers must reject anonymous and
/// under-privileged callers, and accept both roles the CatalogManager policy actually grants
/// (CatalogManager itself, and SuperAdmin per the policy matrix in
/// SecurityServiceCollectionExtensions.ConfigurePolicies). Representative across the 5
/// controllers, not exhaustive per endpoint — the [Authorize] attribute is identical on all 5,
/// so the wiring risk is "did I forget it on one controller", not "does it behave differently
/// per controller".
/// </summary>
[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CatalogAdminAuthorizationTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public CatalogAdminAuthorizationTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("/api/v1/admin/brands")]
    [InlineData("/api/v1/admin/categories")]
    [InlineData("/api/v1/admin/tags")]
    [InlineData("/api/v1/admin/products")]
    public async Task Anonymous_List_Returns401(string path)
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_GetSkuById_Returns401()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync($"/api/v1/admin/skus/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_CreateBrand_Returns401AndDoesNotCreateResource()
    {
        using var client = _fixture.CreateClient();
        var code = CatalogAdminApiFixture.UniqueCode("BRAND");

        using var response = await client.PostAsJsonAsync("/api/v1/admin/brands", new
        {
            code,
            nameZhTw = "匿名嘗試建立",
            description = (string?)null,
            websiteUrl = (string?)null,
            sortOrder = 0,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // No side effect: a real admin can still take this exact code afterward.
        using var adminClient = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var followUp = await CatalogAdminApiSeeding.PostBrandAsync(adminClient, code);
        Assert.Equal(HttpStatusCode.Created, followUp.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/brands")]
    [InlineData("/api/v1/admin/categories")]
    [InlineData("/api/v1/admin/tags")]
    [InlineData("/api/v1/admin/products")]
    public async Task SignedInWithoutCatalogManagerRole_List_Returns403(string path)
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignedInWithoutCatalogManagerRole_CreateBrand_Returns403AndDoesNotCreateResource()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var code = CatalogAdminApiFixture.UniqueCode("BRAND");

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/brands")
        {
            Content = JsonContent.Create(new
            {
                code,
                nameZhTw = "無權限嘗試建立",
                description = (string?)null,
                websiteUrl = (string?)null,
                sortOrder = 0,
                isActive = true,
            }),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var adminClient = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var followUp = await CatalogAdminApiSeeding.PostBrandAsync(adminClient, code);
        Assert.Equal(HttpStatusCode.Created, followUp.StatusCode);
    }

    [Fact]
    public async Task CatalogManagerRole_CanListAndCreateBrand()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);

        using var listResponse = await client.GetAsync("/api/v1/admin/brands");
        using var createResponse = await CatalogAdminApiSeeding.PostBrandAsync(client, CatalogAdminApiFixture.UniqueCode("BRAND"));

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    }

    [Fact]
    public async Task SuperAdminRole_CanListAndCreateBrand()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.SuperAdmin);

        using var listResponse = await client.GetAsync("/api/v1/admin/brands");
        using var createResponse = await CatalogAdminApiSeeding.PostBrandAsync(client, CatalogAdminApiFixture.UniqueCode("BRAND"));

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    }
}
