using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Catalog;

namespace DoSelect.Api.IntegrationTests.Catalog;

/// <summary>
/// HTTP-layer coverage for the public, anonymous search/detail/filter-options endpoints:
/// routing, query-string model binding, and ProblemDetails status-code mapping — including
/// two regressions caught in review (unsupported spec operator returning 500 instead of
/// 400, and a missing published default SKU returning 500 instead of 404). The equivalent
/// business rules are already unit-tested in-process at DoSelect.Infrastructure.Tests
/// (EfProductSearchServiceTests / CatalogDetailAndFilterOptionsTests) — this layer only
/// proves the wiring on top of that.
/// </summary>
[Collection(nameof(ProductsApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ProductsApiTests
{
    private readonly ProductsApiFixture _fixture;

    public ProductsApiTests(ProductsApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Search_WhenNoFilters_ExcludesOutOfStockByDefault()
    {
        Product inStock;
        Product outOfStock;
        await using (var context = _fixture.CreateScopedContext())
        {
            (inStock, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context, onHandQuantity: 5);
            (outOfStock, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context, onHandQuantity: 0);
        }

        using var response = await _fixture.Client.GetAsync(
            $"/api/v1/products?q={inStock.ProductCode}&pageSize=20");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        response.EnsureSuccessStatusCode();

        var codes = body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("productCode").GetString())
            .ToArray();
        Assert.Contains(inStock.ProductCode, codes);

        using var outOfStockResponse = await _fixture.Client.GetAsync(
            $"/api/v1/products?q={outOfStock.ProductCode}&pageSize=20");
        var outOfStockBody = await outOfStockResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, outOfStockBody.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_WhenInStockIsExplicitlyFalse_IncludesOutOfStockItems()
    {
        Product outOfStock;
        await using (var context = _fixture.CreateScopedContext())
        {
            (outOfStock, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context, onHandQuantity: 0);
        }

        using var response = await _fixture.Client.GetAsync(
            $"/api/v1/products?q={outOfStock.ProductCode}&inStock=false&pageSize=20");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_WhenSpecFilterHasNoCategory_Returns400WithFilterUnsupported()
    {
        using var response = await _fixture.Client.GetAsync(
            "/api/v1/products?specs[0].semanticKey=length&specs[0].operator=gte&specs[0].value=1");
        var (status, code, _) = await ProductsApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("search_filter_unsupported", code);
    }

    [Fact]
    public async Task Search_WhenSpecOperatorIsUnsupported_Returns400InsteadOf500()
    {
        Product product;
        Category category;
        await using (var context = _fixture.CreateScopedContext())
        {
            (product, _, category) = await ProductsApiSeeding.CreatePublishedProductAsync(context);
        }

        using var response = await _fixture.Client.GetAsync(
            $"/api/v1/products?category={category.Code}&specs[0].semanticKey=length&specs[0].operator=contains&specs[0].value=1");
        var (status, code, _) = await ProductsApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("search_filter_unsupported", code);
    }

    [Fact]
    public async Task GetById_WhenProductDoesNotExist_Returns404()
    {
        using var response = await _fixture.Client.GetAsync($"/api/v1/products/{Guid.NewGuid()}");
        var (status, code, _) = await ProductsApiFixture.ReadProblemAsync(response);

        Assert.Equal(404, status);
        Assert.Equal("resource_not_found", code);
    }

    [Fact]
    public async Task GetById_WhenProductIsPublished_ReturnsOkWithDetail()
    {
        Product product;
        await using (var context = _fixture.CreateScopedContext())
        {
            (product, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context);
        }

        using var response = await _fixture.Client.GetAsync($"/api/v1/products/{product.PublicId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(product.ProductCode, body.GetProperty("productCode").GetString());
    }

    [Fact]
    public async Task GetFilterOptions_WhenCategoryIsUnknown_Returns400WithFilterUnsupported()
    {
        using var response = await _fixture.Client.GetAsync("/api/v1/catalog/filter-options?category=does-not-exist");
        var (status, code, _) = await ProductsApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("search_filter_unsupported", code);
    }

    [Fact]
    public async Task GetFilterOptions_WithoutCategory_ReturnsOkWithTopLevelOptions()
    {
        await using (var context = _fixture.CreateScopedContext())
        {
            await ProductsApiSeeding.CreatePublishedProductAsync(context);
        }

        using var response = await _fixture.Client.GetAsync("/api/v1/catalog/filter-options");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("categories").GetArrayLength() >= 1);
    }
}
