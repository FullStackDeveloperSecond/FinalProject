using DoSelect.Application.Catalog;

namespace DoSelect.Application.Tests.Catalog;

public sealed class ProductSearchQueryValidatorTests
{
    [Fact]
    public void NormalizeSort_WhenSortIsNull_ReturnsRelevance()
    {
        var result = ProductSearchQueryValidator.NormalizeSort(null);

        Assert.Equal(ProductSortOptions.Relevance, result);
    }

    [Theory]
    [InlineData("relevance", ProductSortOptions.Relevance)]
    [InlineData("PRICEASC", ProductSortOptions.PriceAsc)]
    [InlineData("priceDesc", ProductSortOptions.PriceDesc)]
    [InlineData("newest", ProductSortOptions.Newest)]
    public void NormalizeSort_WhenSortIsSupported_ReturnsCanonicalToken(string sort, string expected)
    {
        var result = ProductSearchQueryValidator.NormalizeSort(sort);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeSort_WhenSortIsUnsupported_ThrowsSortUnsupported()
    {
        var exception = Assert.Throws<CatalogSearchException>(
            () => ProductSearchQueryValidator.NormalizeSort("cheapest"));

        Assert.Equal(CatalogSearchException.ErrorCodes.SortUnsupported, exception.ErrorCode);
    }

    [Theory]
    [InlineData("eq", SpecFilterOperator.Eq)]
    [InlineData("GTE", SpecFilterOperator.Gte)]
    [InlineData("lte", SpecFilterOperator.Lte)]
    [InlineData("in", SpecFilterOperator.In)]
    public void ParseOperator_WhenOperatorIsSupported_ReturnsMatchingEnum(
        string token,
        SpecFilterOperator expected)
    {
        var result = ProductSearchQueryValidator.ParseOperator(token);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("between")]
    public void ParseOperator_WhenOperatorIsUnsupported_ThrowsFilterUnsupported(string? token)
    {
        var exception = Assert.Throws<CatalogSearchException>(
            () => ProductSearchQueryValidator.ParseOperator(token));

        Assert.Equal(CatalogSearchException.ErrorCodes.FilterUnsupported, exception.ErrorCode);
    }
}
