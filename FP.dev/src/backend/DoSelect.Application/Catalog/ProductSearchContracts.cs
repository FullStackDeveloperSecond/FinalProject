using DoSelect.Domain.Members;

namespace DoSelect.Application.Catalog;

public enum SpecFilterOperator
{
    Eq,
    Gte,
    Lte,
    In,
}

public sealed record SpecFilter(string SemanticKey, SpecFilterOperator Operator, string? Value, IReadOnlyList<string>? Values);

public sealed record ProductSearchQuery(
    string? Keyword,
    string? CategoryCode,
    string? BrandCode,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? InStock,
    IReadOnlyList<SpecFilter> Specs,
    string? Sort,
    int PageNumber,
    int PageSize,
    SupportedLocale Locale);

public sealed record ProductBrandRef(string Code, string Name);

public sealed record ProductCategoryRef(string Code, string Name);

public sealed record ProductPrice(decimal List, decimal? Sale, string Currency);

public sealed record ProductImageSummary(string Url, string Alt, int Width, int Height);

public sealed record ProductCardDto(
    Guid ProductPublicId,
    Guid DefaultSkuPublicId,
    string ProductCode,
    string SkuCode,
    string Name,
    ProductBrandRef Brand,
    ProductCategoryRef Category,
    ProductPrice Price,
    string Availability,
    ProductImageSummary? PrimaryImage,
    IReadOnlyList<string> Badges);

public static class ProductAvailabilityCodes
{
    public const string InStock = "inStock";
    public const string LowStock = "lowStock";
    public const string OutOfStock = "outOfStock";
}

public static class ProductSortOptions
{
    public const string Relevance = "relevance";
    public const string PriceAsc = "priceAsc";
    public const string PriceDesc = "priceDesc";
    public const string Newest = "newest";

    public static readonly IReadOnlyCollection<string> All =
    [
        Relevance,
        PriceAsc,
        PriceDesc,
        Newest,
    ];
}

public static class SpecFilterOperatorCodes
{
    public const string Eq = "eq";
    public const string Gte = "gte";
    public const string Lte = "lte";
    public const string In = "in";
}
