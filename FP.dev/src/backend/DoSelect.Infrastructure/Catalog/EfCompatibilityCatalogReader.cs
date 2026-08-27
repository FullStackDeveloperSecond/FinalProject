using DoSelect.Application.Builds;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfCompatibilityCatalogReader : ICompatibilityCatalogReader
{
    private readonly DoSelectDbContext _dbContext;

    public EfCompatibilityCatalogReader(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CompatibilityCatalogReadResult> ReadAsync(
        IReadOnlyCollection<CompatibilityItemReference> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > 20 ||
            items.Any(item => item.SkuPublicId == Guid.Empty || item.Quantity is < 1 or > 8))
        {
            throw new ArgumentException("Compatibility items must contain 1..20 valid SKU references.", nameof(items));
        }

        var requested = items.GroupBy(item => item.SkuPublicId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        if (requested.Values.Any(quantity => quantity > 8))
        {
            throw new ArgumentException("The combined quantity of a SKU cannot exceed 8.", nameof(items));
        }

        var requestedIds = requested.Keys.ToArray();
        var skuRows = await (
                from sku in _dbContext.Skus.AsNoTracking()
                join product in _dbContext.Products.AsNoTracking() on sku.ProductId equals product.Id
                join category in _dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
                where requestedIds.Contains(sku.PublicId) &&
                      sku.Status == SkuStatus.Published &&
                      product.Status == ProductStatus.Published &&
                      category.IsActive
                select new
                {
                    sku.Id,
                    sku.PublicId,
                    CategoryCode = category.Code,
                })
            .ToListAsync(cancellationToken);

        var foundPublicIds = skuRows.Select(row => row.PublicId).ToHashSet();
        var missing = requestedIds.Where(id => !foundPublicIds.Contains(id)).ToArray();
        if (skuRows.Count == 0)
        {
            return new CompatibilityCatalogReadResult([], missing);
        }

        var skuIds = skuRows.Select(row => row.Id).ToArray();
        var hardKeys = CompatibilityCatalogContract.HardRuleSemanticKeys.ToArray();
        var scalarValues = await (
                from value in _dbContext.SkuSpecificationValues.AsNoTracking()
                join definition in _dbContext.SpecificationDefinitions.AsNoTracking()
                    on value.SpecificationDefinitionId equals definition.Id
                where skuIds.Contains(value.SkuId) &&
                      value.SpecificationSourceId != null &&
                      definition.IsActive &&
                      hardKeys.Contains(definition.SemanticKey)
                select new
                {
                    value.SkuId,
                    definition.SemanticKey,
                    value.DecimalValue,
                    value.OptionId,
                })
            .ToListAsync(cancellationToken);
        var scalarOptionIds = scalarValues.Where(value => value.OptionId.HasValue)
            .Select(value => value.OptionId!.Value)
            .Distinct()
            .ToArray();
        var scalarOptionCodes = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(option => scalarOptionIds.Contains(option.Id) && option.IsActive)
            .ToDictionaryAsync(option => option.Id, option => option.Code, cancellationToken);

        var multiValues = await (
                from selection in _dbContext.SkuSpecificationOptionSelections.AsNoTracking()
                join option in _dbContext.SpecificationOptions.AsNoTracking()
                    on selection.SpecificationOptionId equals option.Id
                join definition in _dbContext.SpecificationDefinitions.AsNoTracking()
                    on option.SpecificationDefinitionId equals definition.Id
                where skuIds.Contains(selection.SkuId) &&
                      selection.SpecificationSourceId != null &&
                      option.IsActive &&
                      definition.IsActive &&
                      definition.AllowsMultiple &&
                      hardKeys.Contains(definition.SemanticKey)
                select new
                {
                    selection.SkuId,
                    definition.SemanticKey,
                    option.Code,
                })
            .ToListAsync(cancellationToken);

        var specificationsBySku = skuRows.ToDictionary(
            row => row.Id,
            _ => new Dictionary<string, CompatibilitySpecification>(StringComparer.Ordinal));
        foreach (var value in scalarValues)
        {
            if (value.DecimalValue.HasValue)
            {
                specificationsBySku[value.SkuId][value.SemanticKey] =
                    CompatibilitySpecification.FromDecimal(value.DecimalValue.Value);
            }
            else if (value.OptionId.HasValue &&
                     scalarOptionCodes.TryGetValue(value.OptionId.Value, out var optionCode))
            {
                specificationsBySku[value.SkuId][value.SemanticKey] =
                    CompatibilitySpecification.FromOption(optionCode);
            }
        }

        foreach (var group in multiValues.GroupBy(value => new { value.SkuId, value.SemanticKey }))
        {
            specificationsBySku[group.Key.SkuId][group.Key.SemanticKey] =
                CompatibilitySpecification.FromOptions(group.Select(value => value.Code));
        }

        var components = skuRows.Select(row => new CompatibilityComponent(
                row.PublicId,
                row.CategoryCode,
                requested[row.PublicId],
                specificationsBySku[row.Id]))
            .ToArray();
        return new CompatibilityCatalogReadResult(components, missing);
    }
}
