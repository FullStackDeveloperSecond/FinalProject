using DoSelect.Domain.Members;

namespace DoSelect.Application.Catalog;

public sealed record CatalogFilterOptionsQuery(string? CategoryCode, SupportedLocale Locale);

public sealed record CategoryFilterOption(string Code, string Name, Guid PublicId);

public sealed record BrandFilterOption(string Code, string Name, Guid PublicId);

public sealed record PriceRangeDto(decimal Min, decimal Max);

public sealed record SpecificationOptionRef(string Code, string Label);

public sealed record SpecificationFilterOptionDto(
    string SemanticKey,
    string Label,
    string ValueType,
    string? Unit,
    IReadOnlyList<string> Operators,
    IReadOnlyList<SpecificationOptionRef>? Options);

public sealed record CatalogFilterOptionsDto(
    IReadOnlyList<CategoryFilterOption> Categories,
    IReadOnlyList<BrandFilterOption> Brands,
    PriceRangeDto? PriceRange,
    IReadOnlyList<SpecificationFilterOptionDto> SpecificationFilters,
    IReadOnlyList<string> SortOptions);

public interface ICatalogFilterOptionsService
{
    Task<CatalogFilterOptionsDto> GetAsync(
        CatalogFilterOptionsQuery query,
        CancellationToken cancellationToken);
}
