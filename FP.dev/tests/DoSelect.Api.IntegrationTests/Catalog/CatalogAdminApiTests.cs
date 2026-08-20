using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Catalog;

/// <summary>
/// HTTP-layer coverage for the 5 admin Catalog controllers: routing, query-string/route
/// model binding, JSON request/response (de)serialization (including base64 round-tripping
/// of `byte[] RowVersion`), and ProblemDetails status-code mapping. The equivalent business
/// rules are already unit-tested in-process at DoSelect.Infrastructure.Tests
/// (CatalogAdminServiceTests) — this layer only proves the wiring on top of that, so
/// coverage here is representative, not exhaustive.
/// </summary>
[Collection(nameof(CatalogAdminApiCollection))]
public sealed class BrandsAdminApiTests
{
    private readonly HttpClient _client;

    public BrandsAdminApiTests(CatalogAdminApiFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task List_ReturnsOkWithPageResult()
    {
        await CatalogAdminApiSeeding.CreateBrandAsync(_client);

        using var response = await _client.GetAsync("/api/v1/admin/brands?pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("items").GetArrayLength() >= 0);
        Assert.Equal(5, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Create_WhenCodeIsNew_Returns201WithLocation()
    {
        using var response = await CatalogAdminApiSeeding.PostBrandAsync(_client, CatalogAdminApiFixture.UniqueCode("BRAND"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers.Location?.ToString()));
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_WhenCodeAlreadyExists_Returns409WithBrandCodeDuplicate()
    {
        var code = CatalogAdminApiFixture.UniqueCode("BRAND");
        using var first = await CatalogAdminApiSeeding.PostBrandAsync(_client, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostBrandAsync(_client, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("brand_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsOkWithNewRowVersion()
    {
        var brand = await CatalogAdminApiSeeding.CreateBrandAsync(_client);

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/brands/{brand.PublicId}", new
        {
            nameZhTw = "更新後名稱",
            description = (string?)null,
            websiteUrl = (string?)null,
            sortOrder = 1,
            isActive = true,
            rowVersion = brand.RowVersion,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
        Assert.NotEqual(brand.RowVersion, body.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        var brand = await CatalogAdminApiSeeding.CreateBrandAsync(_client);
        using var firstUpdate = await _client.PutAsJsonAsync($"/api/v1/admin/brands/{brand.PublicId}", new
        {
            nameZhTw = "第一次更新",
            description = (string?)null,
            websiteUrl = (string?)null,
            sortOrder = 0,
            isActive = true,
            rowVersion = brand.RowVersion,
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/brands/{brand.PublicId}", new
        {
            nameZhTw = "用舊版本更新",
            description = (string?)null,
            websiteUrl = (string?)null,
            sortOrder = 0,
            isActive = true,
            rowVersion = brand.RowVersion,
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
public sealed class CategoriesAdminApiTests
{
    private readonly HttpClient _client;

    public CategoriesAdminApiTests(CatalogAdminApiFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task List_ReturnsOkWithPageResult()
    {
        await CatalogAdminApiSeeding.CreateCategoryAsync(_client);

        using var response = await _client.GetAsync("/api/v1/admin/categories?pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Create_WhenCodeIsNew_Returns201WithLocation()
    {
        using var response = await CatalogAdminApiSeeding.PostCategoryAsync(_client, CatalogAdminApiFixture.UniqueCode("CAT"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers.Location?.ToString()));
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_WhenCodeAlreadyExists_Returns409WithCategoryCodeDuplicate()
    {
        var code = CatalogAdminApiFixture.UniqueCode("CAT");
        using var first = await CatalogAdminApiSeeding.PostCategoryAsync(_client, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostCategoryAsync(_client, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("category_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Create_WhenParentCategoryPublicIdIsUnknown_Returns400WithCategoryParentInvalid()
    {
        var code = CatalogAdminApiFixture.UniqueCode("CAT");
        using var response = await _client.PostAsJsonAsync("/api/v1/admin/categories", new
        {
            code,
            nameZhTw = "測試分類",
            slug = "slug-" + Guid.NewGuid().ToString("N")[..12],
            description = (string?)null,
            parentCategoryPublicId = Guid.NewGuid(),
            sortOrder = 0,
            isActive = true,
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("category_parent_invalid", problemCode);
    }

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsOkWithNewRowVersion()
    {
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(_client);

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/categories/{category.PublicId}", new
        {
            nameZhTw = "更新後名稱",
            slug = category.Slug,
            description = (string?)null,
            parentCategoryPublicId = (Guid?)null,
            sortOrder = 1,
            isActive = true,
            rowVersion = category.RowVersion,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        var category = await CatalogAdminApiSeeding.CreateCategoryAsync(_client);
        using var firstUpdate = await _client.PutAsJsonAsync($"/api/v1/admin/categories/{category.PublicId}", new
        {
            nameZhTw = "第一次更新",
            slug = category.Slug,
            description = (string?)null,
            parentCategoryPublicId = (Guid?)null,
            sortOrder = 0,
            isActive = true,
            rowVersion = category.RowVersion,
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/categories/{category.PublicId}", new
        {
            nameZhTw = "用舊版本更新",
            slug = category.Slug,
            description = (string?)null,
            parentCategoryPublicId = (Guid?)null,
            sortOrder = 0,
            isActive = true,
            rowVersion = category.RowVersion,
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
public sealed class TagsAdminApiTests
{
    private readonly HttpClient _client;

    public TagsAdminApiTests(CatalogAdminApiFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task List_ReturnsOkWithPageResult()
    {
        await CatalogAdminApiSeeding.PostTagAsync(_client, CatalogAdminApiFixture.UniqueCode("TAG"));

        using var response = await _client.GetAsync("/api/v1/admin/tags?pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Create_WhenCodeIsNew_Returns201WithLocation()
    {
        using var response = await CatalogAdminApiSeeding.PostTagAsync(_client, CatalogAdminApiFixture.UniqueCode("TAG"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers.Location?.ToString()));
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_WhenCodeAlreadyExists_Returns409WithTagCodeDuplicate()
    {
        var code = CatalogAdminApiFixture.UniqueCode("TAG");
        using var first = await CatalogAdminApiSeeding.PostTagAsync(_client, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostTagAsync(_client, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("tag_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsOkWithNewRowVersion()
    {
        using var created = await CatalogAdminApiSeeding.PostTagAsync(_client, CatalogAdminApiFixture.UniqueCode("TAG"));
        var tag = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = tag.GetProperty("publicId").GetGuid();
        var rowVersion = tag.GetProperty("rowVersion").GetString();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/tags/{publicId}", new
        {
            nameZhTw = "更新後名稱",
            sortOrder = 1,
            isActive = true,
            rowVersion,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var created = await CatalogAdminApiSeeding.PostTagAsync(_client, CatalogAdminApiFixture.UniqueCode("TAG"));
        var tag = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = tag.GetProperty("publicId").GetGuid();
        var rowVersion = tag.GetProperty("rowVersion").GetString();

        using var firstUpdate = await _client.PutAsJsonAsync($"/api/v1/admin/tags/{publicId}", new
        {
            nameZhTw = "第一次更新",
            sortOrder = 0,
            isActive = true,
            rowVersion,
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/tags/{publicId}", new
        {
            nameZhTw = "用舊版本更新",
            sortOrder = 0,
            isActive = true,
            rowVersion,
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
public sealed class AdminProductsApiTests
{
    private readonly HttpClient _client;

    public AdminProductsApiTests(CatalogAdminApiFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task List_WithStatusFilter_ReturnsOkWithPageResult()
    {
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(_client);
        await CatalogAdminApiSeeding.PostProductAsync(_client, brandId, categoryId);

        using var response = await _client.GetAsync("/api/v1/admin/products?statuses=Draft&pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task GetById_WhenNotFound_Returns404WithResourceNotFound()
    {
        using var response = await _client.GetAsync($"/api/v1/admin/products/{Guid.NewGuid()}");
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(404, status);
        Assert.Equal("resource_not_found", problemCode);
    }

    [Fact]
    public async Task Create_WhenValid_Returns201WithLocationPointingToGetById()
    {
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(_client);

        using var response = await CatalogAdminApiSeeding.PostProductAsync(_client, brandId, categoryId);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var followUp = await _client.GetAsync(response.Headers.Location);
        var fetched = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        Assert.Equal(publicId, fetched.GetProperty("publicId").GetGuid());
    }

    [Fact]
    public async Task Create_WhenBrandPublicIdIsUnknown_Returns400WithReferenceNotFound()
    {
        var (_, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(_client);

        using var response = await CatalogAdminApiSeeding.PostProductAsync(_client, Guid.NewGuid(), categoryId);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("reference_not_found", problemCode);
    }

    [Fact]
    public async Task Create_WhenProductCodeAlreadyExists_Returns409WithProductCodeDuplicate()
    {
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(_client);
        var code = CatalogAdminApiFixture.UniqueCode("PROD");
        using var first = await CatalogAdminApiSeeding.PostProductAsync(_client, brandId, categoryId, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostProductAsync(_client, brandId, categoryId, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("product_code_duplicate", problemCode);
    }

    [Fact]
    public async Task Update_WhenValid_ReturnsOkWithNewRowVersion()
    {
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(_client);
        using var created = await CatalogAdminApiSeeding.PostProductAsync(_client, brandId, categoryId);
        var product = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = product.GetProperty("publicId").GetGuid();
        var rowVersion = product.GetProperty("rowVersion").GetString();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/products/{publicId}", new
        {
            nameZhTw = "更新後名稱",
            brandPublicId = brandId,
            categoryPublicId = categoryId,
            descriptionZhTw = (string?)null,
            warrantyMonths = (int?)null,
            tagPublicIds = Array.Empty<Guid>(),
            status = "Draft",
            rowVersion,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        var (brandId, categoryId) = await CatalogAdminApiSeeding.CreateBrandAndCategoryAsync(_client);
        using var created = await CatalogAdminApiSeeding.PostProductAsync(_client, brandId, categoryId);
        var product = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = product.GetProperty("publicId").GetGuid();
        var rowVersion = product.GetProperty("rowVersion").GetString();

        using var firstUpdate = await _client.PutAsJsonAsync($"/api/v1/admin/products/{publicId}", new
        {
            nameZhTw = "第一次更新",
            brandPublicId = brandId,
            categoryPublicId = categoryId,
            descriptionZhTw = (string?)null,
            warrantyMonths = (int?)null,
            tagPublicIds = Array.Empty<Guid>(),
            status = "Draft",
            rowVersion,
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/products/{publicId}", new
        {
            nameZhTw = "用舊版本更新",
            brandPublicId = brandId,
            categoryPublicId = categoryId,
            descriptionZhTw = (string?)null,
            warrantyMonths = (int?)null,
            tagPublicIds = Array.Empty<Guid>(),
            status = "Draft",
            rowVersion,
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}

[Collection(nameof(CatalogAdminApiCollection))]
public sealed class AdminSkusApiTests
{
    private readonly CatalogAdminApiFixture _fixture;
    private readonly HttpClient _client;

    public AdminSkusApiTests(CatalogAdminApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
    }

    [Fact]
    public async Task Create_WhenValid_Returns201WithLocationPointingToGetById()
    {
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(_client);

        using var response = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = created.GetProperty("publicId").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var followUp = await _client.GetAsync(response.Headers.Location);
        var fetched = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        Assert.Equal(publicId, fetched.GetProperty("publicId").GetGuid());
    }

    [Fact]
    public async Task Create_WhenSkuCodeAlreadyExists_Returns409WithSkuCodeDuplicate()
    {
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(_client);
        var code = CatalogAdminApiFixture.UniqueCode("SKU");
        using var first = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId, code);
        first.EnsureSuccessStatusCode();

        using var response = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId, code);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("sku_code_duplicate", problemCode);
    }

    [Fact]
    public async Task GetById_WhenNotFound_Returns404WithResourceNotFound()
    {
        using var response = await _client.GetAsync($"/api/v1/admin/skus/{Guid.NewGuid()}");
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(404, status);
        Assert.Equal("resource_not_found", problemCode);
    }

    [Fact]
    public async Task Update_WhenValid_ReturnsOkWithNewRowVersion()
    {
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(_client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/skus/{publicId}", new
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
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("更新後名稱", body.GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Update_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(_client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var firstUpdate = await _client.PutAsJsonAsync($"/api/v1/admin/skus/{publicId}", new
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
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await _client.PutAsJsonAsync($"/api/v1/admin/skus/{publicId}", new
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
        });
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }

    [Fact]
    public async Task Delete_WhenDraftAndUnreferenced_Returns204()
    {
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(_client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new { rowVersion }),
        };
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenSkuHasInventoryBalance_Returns409WithSkuDeleteReferenced()
    {
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(_client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId);
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

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new { rowVersion }),
        };
        using var response = await _client.SendAsync(request);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("sku_delete_referenced", problemCode);
    }

    [Fact]
    public async Task Delete_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(_client);
        using var created = await CatalogAdminApiSeeding.PostSkuAsync(_client, productId);
        var sku = await created.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = sku.GetProperty("publicId").GetGuid();
        var rowVersion = sku.GetProperty("rowVersion").GetString();

        using var firstUpdate = await _client.PutAsJsonAsync($"/api/v1/admin/skus/{publicId}", new
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
        });
        firstUpdate.EnsureSuccessStatusCode();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/skus/{publicId}")
        {
            Content = JsonContent.Create(new { rowVersion }),
        };
        using var response = await _client.SendAsync(request);
        var (status, problemCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", problemCode);
    }
}
