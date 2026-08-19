using System.Text;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfCatalogFilterOptionsService : ICatalogFilterOptionsService
{
    private readonly DoSelectDbContext _dbContext;

    public EfCatalogFilterOptionsService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatalogFilterOptionsDto> GetAsync(
        CatalogFilterOptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Category? category = null;
        if (!string.IsNullOrWhiteSpace(query.CategoryCode))
        {
            var normalized = NormalizeCode(query.CategoryCode);
            category = await _dbContext.Categories.AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.Code == normalized && candidate.IsActive,
                    cancellationToken);
            if (category is null)
            {
                throw new CatalogSearchException(
                    CatalogSearchException.ErrorCodes.FilterUnsupported,
                    $"The category '{query.CategoryCode}' is not recognized.");
            }
        }

        var categories = await GetCategoriesAsync(category, query.Locale, cancellationToken);
        var brands = await GetBrandsAsync(category?.Id, query.Locale, cancellationToken);
        var priceRange = await GetPriceRangeAsync(category?.Id, cancellationToken);
        var specificationFilters = category is null
            ? []
            : await GetSpecificationFiltersAsync(category.Id, query.Locale, cancellationToken);

        return new CatalogFilterOptionsDto(
            categories,
            brands,
            priceRange,
            specificationFilters,
            ProductSortOptions.All.ToList());
    }

    private async Task<IReadOnlyList<CategoryFilterOption>> GetCategoriesAsync(
        Category? selectedCategory,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        var candidates = selectedCategory is null
            ? await _dbContext.Categories.AsNoTracking()
                .Where(category => category.IsActive && category.ParentCategoryId == null)
                .OrderBy(category => category.SortOrder)
                .ToListAsync(cancellationToken)
            : await _dbContext.Categories.AsNoTracking()
                .Where(category => category.IsActive && category.ParentCategoryId == selectedCategory.Id)
                .OrderBy(category => category.SortOrder)
                .ToListAsync(cancellationToken);

        var names = await ResolveCategoryNamesAsync(
            candidates.Select(category => category.Id),
            locale,
            cancellationToken);

        return candidates
            .Select(category => new CategoryFilterOption(
                category.Code,
                names.GetValueOrDefault(category.Id, category.NameZhTw),
                category.PublicId))
            .ToList();
    }

    private async Task<IReadOnlyList<BrandFilterOption>> GetBrandsAsync(
        long? categoryId,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        var brandIdsWithPublishedProducts = _dbContext.Products.AsNoTracking()
            .Where(product =>
                product.Status == ProductStatus.Published &&
                (categoryId == null || product.CategoryId == categoryId))
            .Select(product => product.BrandId)
            .Distinct();

        var candidates = await _dbContext.Brands.AsNoTracking()
            .Where(brand => brand.IsActive && brandIdsWithPublishedProducts.Contains(brand.Id))
            .OrderBy(brand => brand.SortOrder)
            .ToListAsync(cancellationToken);

        var names = await ResolveBrandNamesAsync(
            candidates.Select(brand => brand.Id),
            locale,
            cancellationToken);

        return candidates
            .Select(brand => new BrandFilterOption(
                brand.Code,
                names.GetValueOrDefault(brand.Id, brand.NameZhTw),
                brand.PublicId))
            .ToList();
    }

    private async Task<PriceRangeDto?> GetPriceRangeAsync(long? categoryId, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var activeSalePrices = _dbContext.SalePrices.AsNoTracking()
            .Where(salePrice =>
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= nowUtc &&
                salePrice.EndsAtUtc > nowUtc);

        var effectivePrices = await (
            from product in _dbContext.Products.AsNoTracking()
            where product.Status == ProductStatus.Published &&
                (categoryId == null || product.CategoryId == categoryId)
            join sku in _dbContext.Skus.AsNoTracking()
                    .Where(candidate => candidate.IsDefault && candidate.Status == SkuStatus.Published)
                on product.Id equals sku.ProductId
            join salePrice in activeSalePrices on sku.Id equals salePrice.SkuId into saleGroup
            from salePrice in saleGroup.DefaultIfEmpty()
            select salePrice != null ? salePrice.Price : sku.ListPrice)
            .ToListAsync(cancellationToken);

        if (effectivePrices.Count == 0)
        {
            return null;
        }

        return new PriceRangeDto(effectivePrices.Min(), effectivePrices.Max());
    }

    private async Task<IReadOnlyList<SpecificationFilterOptionDto>> GetSpecificationFiltersAsync(
        long categoryId,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(definition => definition.CategoryId == categoryId && definition.IsActive)
            .OrderBy(definition => definition.SortOrder)
            .ToListAsync(cancellationToken);
        if (definitions.Count == 0)
        {
            return [];
        }

        var definitionIds = definitions.Select(definition => definition.Id).ToArray();
        var definitionLabels = await ResolveDefinitionLabelsAsync(definitionIds, locale, cancellationToken);

        var unitIds = definitions
            .Where(definition => definition.MeasurementUnitId.HasValue)
            .Select(definition => definition.MeasurementUnitId!.Value)
            .Distinct()
            .ToArray();
        var units = await _dbContext.MeasurementUnits.AsNoTracking()
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);

        var optionDefinitionIds = definitions
            .Where(definition => definition.ValueType == SpecificationValueType.Option)
            .Select(definition => definition.Id)
            .ToArray();
        var options = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(option => optionDefinitionIds.Contains(option.SpecificationDefinitionId) && option.IsActive)
            .OrderBy(option => option.SortOrder)
            .ToListAsync(cancellationToken);
        var optionLabels = await ResolveOptionLabelsAsync(
            options.Select(option => option.Id),
            locale,
            cancellationToken);

        return definitions
            .Select(definition =>
            {
                var unit = definition.MeasurementUnitId.HasValue &&
                    units.TryGetValue(definition.MeasurementUnitId.Value, out var measurementUnit)
                        ? measurementUnit.Symbol
                        : null;
                var definitionOptions = definition.ValueType == SpecificationValueType.Option
                    ? options
                        .Where(option => option.SpecificationDefinitionId == definition.Id)
                        .Select(option => new SpecificationOptionRef(
                            option.Code,
                            optionLabels.GetValueOrDefault(option.Id, option.DisplayNameZhTw)))
                        .ToList()
                    : null;

                return new SpecificationFilterOptionDto(
                    definition.SemanticKey,
                    definitionLabels.GetValueOrDefault(definition.Id, definition.DisplayNameZhTw),
                    definition.ValueType.ToString(),
                    unit,
                    ResolveSupportedOperators(definition.ValueType),
                    definitionOptions);
            })
            .ToList();
    }

    private static IReadOnlyList<string> ResolveSupportedOperators(SpecificationValueType valueType) => valueType switch
    {
        SpecificationValueType.Decimal =>
        [
            SpecFilterOperatorCodes.Eq,
            SpecFilterOperatorCodes.Gte,
            SpecFilterOperatorCodes.Lte,
        ],
        SpecificationValueType.String or SpecificationValueType.Option =>
        [
            SpecFilterOperatorCodes.Eq,
            SpecFilterOperatorCodes.In,
        ],
        _ => [SpecFilterOperatorCodes.Eq],
    };

    private async Task<Dictionary<long, string>> ResolveCategoryNamesAsync(
        IEnumerable<long> categoryIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw)
        {
            return new Dictionary<long, string>();
        }

        var ids = categoryIds.Distinct().ToArray();
        return await _dbContext.CategoryTranslations.AsNoTracking()
            .Where(translation =>
                ids.Contains(translation.CategoryId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(translation => translation.CategoryId, translation => translation.Name, cancellationToken);
    }

    private async Task<Dictionary<long, string>> ResolveBrandNamesAsync(
        IEnumerable<long> brandIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw)
        {
            return new Dictionary<long, string>();
        }

        var ids = brandIds.Distinct().ToArray();
        return await _dbContext.BrandTranslations.AsNoTracking()
            .Where(translation =>
                ids.Contains(translation.BrandId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(translation => translation.BrandId, translation => translation.Name, cancellationToken);
    }

    private async Task<Dictionary<long, string>> ResolveDefinitionLabelsAsync(
        IReadOnlyCollection<long> definitionIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw || definitionIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        return await _dbContext.SpecificationDefinitionTranslations.AsNoTracking()
            .Where(translation =>
                definitionIds.Contains(translation.SpecificationDefinitionId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(
                translation => translation.SpecificationDefinitionId,
                translation => translation.DisplayName,
                cancellationToken);
    }

    private async Task<Dictionary<long, string>> ResolveOptionLabelsAsync(
        IEnumerable<long> optionIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        var ids = optionIds.Distinct().ToArray();
        if (locale == SupportedLocale.ZhTw || ids.Length == 0)
        {
            return new Dictionary<long, string>();
        }

        return await _dbContext.SpecificationOptionTranslations.AsNoTracking()
            .Where(translation =>
                ids.Contains(translation.SpecificationOptionId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(
                translation => translation.SpecificationOptionId,
                translation => translation.DisplayName,
                cancellationToken);
    }

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
