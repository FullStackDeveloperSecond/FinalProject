using System.Net.Http.Json;
using System.Text.Json;

namespace DoSelect.Api.IntegrationTests.Catalog;

internal readonly record struct SeededBrand(Guid PublicId, string Code, string? RowVersion);

internal readonly record struct SeededCategory(Guid PublicId, string Code, string Slug, string? RowVersion);

/// <summary>
/// Seeds catalog data purely through the admin HTTP API so <see cref="CatalogAdminApiTests"/>
/// stays a black-box HTTP test suite (the one exception is inventory balances, which have
/// no admin endpoint yet — see AdminSkusApiTests.Delete_WhenSkuHasInventoryBalance_...).
/// </summary>
internal static class CatalogAdminApiSeeding
{
    public static Task<HttpResponseMessage> PostBrandAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/v1/admin/brands", new
        {
            code,
            nameZhTw = "測試品牌",
            description = (string?)null,
            websiteUrl = (string?)null,
            sortOrder = 0,
            isActive = true,
        });

    public static async Task<SeededBrand> CreateBrandAsync(HttpClient client, string? code = null)
    {
        code ??= CatalogAdminApiFixture.UniqueCode("BRAND");
        using var response = await PostBrandAsync(client, code);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new SeededBrand(
            body.GetProperty("publicId").GetGuid(),
            body.GetProperty("code").GetString()!,
            body.GetProperty("rowVersion").GetString());
    }

    public static Task<HttpResponseMessage> PostCategoryAsync(HttpClient client, string code, string? slug = null) =>
        client.PostAsJsonAsync("/api/v1/admin/categories", new
        {
            code,
            nameZhTw = "測試分類",
            slug = slug ?? "slug-" + Guid.NewGuid().ToString("N")[..12],
            description = (string?)null,
            parentCategoryPublicId = (Guid?)null,
            sortOrder = 0,
            isActive = true,
        });

    public static async Task<SeededCategory> CreateCategoryAsync(HttpClient client, string? code = null)
    {
        code ??= CatalogAdminApiFixture.UniqueCode("CAT");
        var slug = "slug-" + Guid.NewGuid().ToString("N")[..12];
        using var response = await PostCategoryAsync(client, code, slug);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new SeededCategory(
            body.GetProperty("publicId").GetGuid(),
            body.GetProperty("code").GetString()!,
            body.GetProperty("slug").GetString()!,
            body.GetProperty("rowVersion").GetString());
    }

    public static Task<HttpResponseMessage> PostTagAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/v1/admin/tags", new
        {
            code,
            nameZhTw = "新品",
            sortOrder = 0,
            isActive = true,
        });

    public static async Task<(Guid BrandPublicId, Guid CategoryPublicId)> CreateBrandAndCategoryAsync(HttpClient client)
    {
        var brand = await CreateBrandAsync(client);
        var category = await CreateCategoryAsync(client);
        return (brand.PublicId, category.PublicId);
    }

    public static Task<HttpResponseMessage> PostProductAsync(
        HttpClient client, Guid brandPublicId, Guid categoryPublicId, string? code = null) =>
        client.PostAsJsonAsync("/api/v1/admin/products", new
        {
            productCode = code ?? CatalogAdminApiFixture.UniqueCode("PROD"),
            nameZhTw = "測試商品",
            brandPublicId,
            categoryPublicId,
            descriptionZhTw = (string?)null,
            warrantyMonths = (int?)null,
            tagPublicIds = Array.Empty<Guid>(),
            status = "Draft",
        });

    public static async Task<Guid> CreateProductWithCatalogAsync(HttpClient client)
    {
        var (brandId, categoryId) = await CreateBrandAndCategoryAsync(client);
        using var response = await PostProductAsync(client, brandId, categoryId);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("publicId").GetGuid();
    }

    public static Task<HttpResponseMessage> PostSkuAsync(HttpClient client, Guid productPublicId, string? code = null) =>
        client.PostAsJsonAsync($"/api/v1/admin/products/{productPublicId}/skus", new
        {
            skuCode = code ?? CatalogAdminApiFixture.UniqueCode("SKU"),
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
        });
}
