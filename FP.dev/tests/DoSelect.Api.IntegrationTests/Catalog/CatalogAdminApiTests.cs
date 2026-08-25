using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Catalog;
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

    [Fact]
    public async Task List_WithExtremePageNumber_ReturnsOkWithEmptyPage()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();

        // Same int-overflow guard as AdminProductsApiTests.List_WithExtremePageNumber_...,
        // applied identically across Brands/Categories/Tags — a page this far out is always
        // legally empty, not a 500.
        using var response = await client.GetAsync("/api/v1/admin/brands?pageNumber=2147483647&pageSize=100");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
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
        var defaultSku = Assert.Single(created.GetProperty("skus").EnumerateArray());
        Assert.True(defaultSku.GetProperty("isDefault").GetBoolean());

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
    public async Task Create_WhenDefaultSkuIsMissing_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/products")
            {
                Content = JsonContent.Create(new
                {
                    productCode = CatalogAdminApiFixture.UniqueCode("PROD"),
                    nameZhTw = "測試商品",
                    brandPublicId = brandId,
                    categoryPublicId = categoryId,
                    descriptionZhTw = (string?)null,
                    warrantyMonths = (int?)null,
                    tagPublicIds = Array.Empty<Guid>(),
                    status = "Draft",
                }),
            });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
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

    [Fact]
    public async Task List_WithUndefinedNumericStatus_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();

        // Enum.TryParse<ProductStatus>("999", ...) succeeds as a raw numeric conversion even
        // though no member has that value — without an Enum.IsDefined guard this used to reach
        // the query and could 500 rather than a stable validation error.
        using var response = await client.GetAsync("/api/v1/admin/products?statuses=999");
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
    }

    [Fact]
    public async Task Create_WhenStatusIsANumericAlias_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);

        // "1" parses to the defined member ProductStatus.Published, but only the formal status
        // name is a supported input — a numeric alias must still be rejected.
        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/products")
        {
            Content = JsonContent.Create(new
            {
                productCode = CatalogAdminApiFixture.UniqueCode("PROD"),
                nameZhTw = "測試商品",
                brandPublicId = brandId,
                categoryPublicId = categoryId,
                descriptionZhTw = (string?)null,
                warrantyMonths = (int?)null,
                tagPublicIds = Array.Empty<Guid>(),
                status = "1",
                defaultSku = new
                {
                    skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                    nameZhTw = "預設規格",
                    listPrice = 10_000m,
                    unitCost = 7_000m,
                    status = "Draft",
                    isDefault = true,
                    requiresPrepayment = false,
                    specifications = Array.Empty<object>(),
                },
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
    }

    [Fact]
    public async Task Create_WhenProductCodeExceedsMaxLength_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);

        // Product.ProductCode is nvarchar(64); an over-long value used to ride through to a
        // SQL Server truncation DbUpdateException (500) instead of a stable 400.
        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/products")
        {
            Content = JsonContent.Create(new
            {
                productCode = new string('A', 65),
                nameZhTw = "測試商品",
                brandPublicId = brandId,
                categoryPublicId = categoryId,
                descriptionZhTw = (string?)null,
                warrantyMonths = (int?)null,
                tagPublicIds = Array.Empty<Guid>(),
                status = "Draft",
                defaultSku = new
                {
                    skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                    nameZhTw = "預設規格",
                    listPrice = 10_000m,
                    unitCost = 7_000m,
                    status = "Draft",
                    isDefault = true,
                    requiresPrepayment = false,
                    specifications = Array.Empty<object>(),
                },
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
    }

    [Fact]
    public async Task List_WithExtremePageNumber_ReturnsOkWithEmptyPage()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();

        // (pageNumber - 1) * pageSize used to overflow int and could 500 for an extreme page
        // number; a page this far out is always legally empty.
        using var response = await client.GetAsync("/api/v1/admin/products?pageNumber=2147483647&pageSize=100");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
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
    public async Task Create_WhenSkuCodeExceedsMaxLength_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        // Sku.SkuCode is nvarchar(64); an over-long value used to ride through to a SQL Server
        // truncation DbUpdateException (500) instead of a stable 400.
        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/skus")
        {
            Content = JsonContent.Create(new
            {
                skuCode = new string('A', 65),
                nameZhTw = "標準版",
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
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
    }

    /// <summary>組長 PR #24 round 4 review, item 4: a negative price used to reach
    /// CatalogGuard.NonNegative and throw ArgumentOutOfRangeException, which
    /// GlobalExceptionHandler turns into an opaque 500 instead of a stable 400.</summary>
    [Fact]
    public async Task Create_WhenListPriceIsNegative_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/skus")
        {
            Content = JsonContent.Create(new
            {
                skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                nameZhTw = "標準版",
                listPrice = -1m,
                unitCost = 7_000m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
    }

    /// <summary>組長 PR #24 round 5 review, item 3: WeightKg's SQL column is decimal(10,3) —
    /// 0.001 is the true smallest valid value, not an arbitrarily larger "practical minimum"
    /// the [Range] bound might have invented.</summary>
    [Fact]
    public async Task Create_WhenWeightKgIsAtTheSmallestValidPrecision_Returns201()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/skus")
        {
            Content = JsonContent.Create(new
            {
                skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                nameZhTw = "標準版",
                listPrice = 10_000m,
                unitCost = 7_000m,
                weightKg = 0.001m,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
            }),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>組長 PR #24 round 5 review, item 3: the old double.MaxValue upper bound was
    /// nowhere near ListPrice's actual SQL column limit (decimal(18,2)) — an over-large value
    /// used to ride through to a truncation/overflow DbUpdateException (500).</summary>
    [Fact]
    public async Task Create_WhenListPriceExceedsTheSqlColumnPrecision_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/skus")
        {
            Content = JsonContent.Create(new
            {
                skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                nameZhTw = "標準版",
                // decimal(18,2) tops out at 9999999999999999.99 — one order of magnitude over.
                listPrice = 99_999_999_999_999_999.99m,
                unitCost = 7_000m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
    }

    /// <summary>Same rationale as the negative-price test — CatalogGuard.OptionalPositive
    /// throws for zero or negative dimensions.</summary>
    [Fact]
    public async Task Create_WhenWeightKgIsZero_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/skus")
        {
            Content = JsonContent.Create(new
            {
                skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                nameZhTw = "標準版",
                listPrice = 10_000m,
                unitCost = 7_000m,
                weightKg = 0m,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Draft",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
    }

    [Fact]
    public async Task Create_WhenPublishedAndSpecificationsAreEmptyButCategoryRequiresOne_Returns400WithMissingRequiredSpecification()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var (brandPublicId, categoryPublicId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(client);

        // No admin HTTP endpoint exists for specification definitions (managed via seed/
        // migration data, not this admin surface), so this one seed step reaches into the
        // DbContext directly — same precedent as Delete_WhenSkuHasInventoryBalance_... above.
        await using (var context = _fixture.CreateScopedContext())
        {
            var categoryId = await context.Categories
                .Where(candidate => candidate.PublicId == categoryPublicId)
                .Select(candidate => candidate.Id)
                .FirstAsync();
            context.SpecificationDefinitions.Add(new SpecificationDefinition(
                Guid.CreateVersion7(), categoryId, CatalogAdminApiFixture.UniqueCode("SPEC"), "必要規格",
                SpecificationValueType.Decimal, null, isRequired: true, isProtected: false, sortOrder: 0, DateTime.UtcNow));
            await context.SaveChangesAsync();
        }

        using var productResponse = await CatalogAdminApiSeeding.PostProductAsync(client, brandPublicId, categoryPublicId);
        productResponse.EnsureSuccessStatusCode();
        var productId = (await productResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("publicId").GetGuid();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/skus")
        {
            Content = JsonContent.Create(new
            {
                skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                nameZhTw = "標準版",
                listPrice = 10_000m,
                unitCost = 7_000m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "Published",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("sku_missing_required_specification", problemCode);
    }

    [Fact]
    public async Task Update_WhenUnsettingTheCurrentDefaultSku_Returns409WithSkuDefaultRequired()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var productResponse = await client.GetAsync($"/api/v1/admin/products/{productId}");
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>();
        var defaultSku = product.GetProperty("skus").EnumerateArray().Single(sku => sku.GetProperty("isDefault").GetBoolean());
        var publicId = defaultSku.GetProperty("publicId").GetGuid();
        var rowVersion = defaultSku.GetProperty("rowVersion");

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new
            {
                nameZhTw = "標準版",
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
        Assert.Equal("sku_default_required", problemCode);
    }

    [Fact]
    public async Task Create_WhenStatusIsANumericAlias_Returns400WithValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        // "0" parses to the defined member SkuStatus.Draft, but only the formal status name is
        // a supported input — a numeric alias must still be rejected.
        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/skus")
        {
            Content = JsonContent.Create(new
            {
                skuCode = CatalogAdminApiFixture.UniqueCode("SKU"),
                nameZhTw = "標準版",
                listPrice = 10_000m,
                unitCost = 7_000m,
                weightKg = (decimal?)null,
                lengthCm = (decimal?)null,
                widthCm = (decimal?)null,
                heightCm = (decimal?)null,
                status = "0",
                isDefault = false,
                requiresPrepayment = false,
                specifications = Array.Empty<object>(),
            }),
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", problemCode);
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
