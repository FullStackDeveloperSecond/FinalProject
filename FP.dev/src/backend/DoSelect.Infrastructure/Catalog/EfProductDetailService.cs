using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfProductDetailService : IProductDetailService
{
    private const int MaxPurchasableQuantityCap = 99;

    private readonly DoSelectDbContext _dbContext;

    public EfProductDetailService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProductDetailDto?> GetByPublicIdAsync(
        Guid productPublicId,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.PublicId == productPublicId &&
                    candidate.Status == ProductStatus.Published,
                cancellationToken);
        if (product is null)
        {
            return null;
        }

        var skus = await _dbContext.Skus.AsNoTracking()
            .Where(sku => sku.ProductId == product.Id && sku.Status == SkuStatus.Published)
            .OrderByDescending(sku => sku.IsDefault)
            .ThenBy(sku => sku.SkuCode)
            .ToListAsync(cancellationToken);
        if (skus.Count == 0)
        {
            return null;
        }

        var defaultSku = skus.First(sku => sku.IsDefault);
        var skuIds = skus.Select(sku => sku.Id).ToArray();

        var brand = await _dbContext.Brands.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == product.BrandId, cancellationToken);
        var category = await _dbContext.Categories.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == product.CategoryId, cancellationToken);

        var balances = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var activeSalePrices = await _dbContext.SalePrices.AsNoTracking()
            .Where(salePrice =>
                skuIds.Contains(salePrice.SkuId) &&
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= nowUtc &&
                salePrice.EndsAtUtc > nowUtc)
            .ToDictionaryAsync(salePrice => salePrice.SkuId, cancellationToken);

        var specValues = await _dbContext.SkuSpecificationValues.AsNoTracking()
            .Where(value => skuIds.Contains(value.SkuId))
            .ToListAsync(cancellationToken);
        var definitionIds = specValues.Select(value => value.SpecificationDefinitionId).Distinct().ToArray();
        var definitions = await _dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(definition => definitionIds.Contains(definition.Id))
            .ToDictionaryAsync(definition => definition.Id, cancellationToken);

        var unitIds = definitions.Values
            .Where(definition => definition.MeasurementUnitId.HasValue)
            .Select(definition => definition.MeasurementUnitId!.Value)
            .Distinct()
            .ToArray();
        var units = await _dbContext.MeasurementUnits.AsNoTracking()
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);

        var optionIds = specValues
            .Where(value => value.OptionId.HasValue)
            .Select(value => value.OptionId!.Value)
            .Distinct()
            .ToArray();
        var options = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(option => optionIds.Contains(option.Id))
            .ToDictionaryAsync(option => option.Id, cancellationToken);

        var definitionLabels = await ResolveDefinitionLabelsAsync(definitionIds, locale, cancellationToken);
        var optionLabels = await ResolveOptionLabelsAsync(optionIds, locale, cancellationToken);

        var tags = await (
            from productTag in _dbContext.ProductTags.AsNoTracking()
            join tag in _dbContext.Tags.AsNoTracking() on productTag.TagId equals tag.Id
            where productTag.ProductId == product.Id && tag.IsActive
            orderby tag.SortOrder
            select tag).ToListAsync(cancellationToken);

        var productName = await ResolveNameAsync(
            _dbContext.ProductTranslations.AsNoTracking()
                .Where(t => t.ProductId == product.Id),
            product.NameZhTw,
            locale,
            cancellationToken);
        var description = await ResolveDescriptionAsync(product, locale, cancellationToken);
        var brandName = await ResolveNameAsync(
            _dbContext.BrandTranslations.AsNoTracking().Where(t => t.BrandId == brand.Id),
            brand.NameZhTw,
            locale,
            cancellationToken);
        var categoryName = await ResolveNameAsync(
            _dbContext.CategoryTranslations.AsNoTracking().Where(t => t.CategoryId == category.Id),
            category.NameZhTw,
            locale,
            cancellationToken);

        var skuDtos = new List<PublicSkuDto>(skus.Count);
        var groupValuesByDefinition = new Dictionary<long, List<SpecificationGroupValue>>();

        foreach (var sku in skus)
        {
            balances.TryGetValue(sku.Id, out var balance);
            activeSalePrices.TryGetValue(sku.Id, out var salePrice);
            var skuName = await ResolveNameAsync(
                _dbContext.SkuTranslations.AsNoTracking().Where(t => t.SkuId == sku.Id),
                sku.NameZhTw,
                locale,
                cancellationToken);

            var skuSpecifications = new List<SkuSpecificationSummary>();
            foreach (var value in specValues.Where(value => value.SkuId == sku.Id))
            {
                if (!definitions.TryGetValue(value.SpecificationDefinitionId, out var definition))
                {
                    continue;
                }

                var unit = definition.MeasurementUnitId.HasValue &&
                    units.TryGetValue(definition.MeasurementUnitId.Value, out var measurementUnit)
                        ? measurementUnit.Symbol
                        : null;
                var formattedValue = FormatSpecificationValue(value, definition, options);
                var label = definitionLabels.GetValueOrDefault(definition.Id, definition.DisplayNameZhTw);

                skuSpecifications.Add(new SkuSpecificationSummary(
                    definition.SemanticKey,
                    label,
                    unit,
                    formattedValue));

                if (!groupValuesByDefinition.TryGetValue(definition.Id, out var groupValues))
                {
                    groupValues = [];
                    groupValuesByDefinition[definition.Id] = groupValues;
                }

                groupValues.Add(new SpecificationGroupValue(sku.PublicId, formattedValue));
            }

            skuDtos.Add(new PublicSkuDto(
                sku.PublicId,
                sku.SkuCode,
                skuName,
                new ProductPrice(sku.ListPrice, salePrice?.Price, "TWD"),
                ResolveAvailability(balance),
                ResolveMaxPurchasableQuantity(balance),
                skuSpecifications,
                new SkuDimensionsSummary(sku.WeightKg, sku.LengthCm, sku.WidthCm, sku.HeightCm),
                sku.IsDefault));
        }

        var specificationGroups = groupValuesByDefinition
            .Select(entry =>
            {
                var definition = definitions[entry.Key];
                var unit = definition.MeasurementUnitId.HasValue &&
                    units.TryGetValue(definition.MeasurementUnitId.Value, out var measurementUnit)
                        ? measurementUnit.Symbol
                        : null;
                return new SpecificationGroupDto(
                    definition.SemanticKey,
                    definitionLabels.GetValueOrDefault(definition.Id, definition.DisplayNameZhTw),
                    unit,
                    entry.Value);
            })
            .OrderBy(group => group.SemanticKey, StringComparer.Ordinal)
            .ToList();

        balances.TryGetValue(defaultSku.Id, out var defaultBalance);
        activeSalePrices.TryGetValue(defaultSku.Id, out var defaultSalePrice);

        return new ProductDetailDto(
            product.PublicId,
            defaultSku.PublicId,
            product.ProductCode,
            defaultSku.SkuCode,
            productName,
            new ProductBrandRef(brand.Code, brandName),
            new ProductCategoryRef(category.Code, categoryName),
            new ProductPrice(defaultSku.ListPrice, defaultSalePrice?.Price, "TWD"),
            ResolveAvailability(defaultBalance),
            // Public image URLs depend on the shared file/image service (SH-06),
            // which is not available yet; deferred to a follow-up slice.
            null,
            product.IsFeatured ? ["featured"] : Array.Empty<string>(),
            description,
            tags.Select(tag => new TagRef(tag.Code, tag.NameZhTw)).ToList(),
            // Same SH-06 dependency as PrimaryImage above.
            [],
            skuDtos,
            specificationGroups,
            // Cross-checking against ShippingProviderProfile/PackageLimitVersion package
            // limits needs an established provider-code convention that does not exist
            // in the codebase yet; deferred to a follow-up M-11 slice rather than
            // guessing string literals.
            [],
            product.WarrantyMonths);
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
        IReadOnlyCollection<long> optionIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw || optionIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        return await _dbContext.SpecificationOptionTranslations.AsNoTracking()
            .Where(translation =>
                optionIds.Contains(translation.SpecificationOptionId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(
                translation => translation.SpecificationOptionId,
                translation => translation.DisplayName,
                cancellationToken);
    }

    private static async Task<string> ResolveNameAsync<TTranslation>(
        IQueryable<TTranslation> translations,
        string fallback,
        SupportedLocale locale,
        CancellationToken cancellationToken)
        where TTranslation : class
    {
        if (locale == SupportedLocale.ZhTw)
        {
            return fallback;
        }

        var name = translations switch
        {
            IQueryable<BrandTranslation> brand => await brand
                .Where(t => t.Locale == locale && t.TranslationStatus == TranslationStatus.Published)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(cancellationToken),
            IQueryable<CategoryTranslation> category => await category
                .Where(t => t.Locale == locale && t.TranslationStatus == TranslationStatus.Published)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(cancellationToken),
            IQueryable<ProductTranslation> product => await product
                .Where(t => t.Locale == locale && t.TranslationStatus == TranslationStatus.Published)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(cancellationToken),
            IQueryable<SkuTranslation> sku => await sku
                .Where(t => t.Locale == locale && t.TranslationStatus == TranslationStatus.Published)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(cancellationToken),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private async Task<string?> ResolveDescriptionAsync(
        Product product,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw)
        {
            return product.DescriptionZhTw;
        }

        var description = await _dbContext.ProductTranslations.AsNoTracking()
            .Where(t =>
                t.ProductId == product.Id &&
                t.Locale == locale &&
                t.TranslationStatus == TranslationStatus.Published)
            .Select(t => t.Description)
            .FirstOrDefaultAsync(cancellationToken);

        return description ?? product.DescriptionZhTw;
    }

    private static string FormatSpecificationValue(
        SkuSpecificationValue value,
        SpecificationDefinition definition,
        IReadOnlyDictionary<long, SpecificationOption> optionsById)
    {
        return definition.ValueType switch
        {
            SpecificationValueType.String => value.StringValue ?? string.Empty,
            SpecificationValueType.Decimal => value.DecimalValue?.ToString("0.###") ?? string.Empty,
            SpecificationValueType.Boolean => value.BooleanValue == true ? "true" : "false",
            SpecificationValueType.Option => value.OptionId.HasValue &&
                optionsById.TryGetValue(value.OptionId.Value, out var option)
                    ? option.DisplayNameZhTw
                    : string.Empty,
            _ => string.Empty,
        };
    }

    private static int ResolveMaxPurchasableQuantity(InventoryBalance? balance)
    {
        if (balance is null)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(balance.AvailableQuantity, MaxPurchasableQuantityCap));
    }

    private static string ResolveAvailability(InventoryBalance? balance)
    {
        if (balance is null || balance.AvailableQuantity <= 0)
        {
            return ProductAvailabilityCodes.OutOfStock;
        }

        return balance.AvailableQuantity <= balance.ReorderLevel
            ? ProductAvailabilityCodes.LowStock
            : ProductAvailabilityCodes.InStock;
    }
}
