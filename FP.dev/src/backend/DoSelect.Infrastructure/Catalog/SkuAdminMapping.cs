using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

internal static class SkuAdminMapping
{
    public static async Task<SkuDto> ToDtoAsync(
        DoSelectDbContext dbContext,
        Sku sku,
        Product product,
        CancellationToken cancellationToken)
    {
        var values = await dbContext.SkuSpecificationValues.AsNoTracking()
            .Where(value => value.SkuId == sku.Id)
            .ToListAsync(cancellationToken);
        var definitionIds = values.Select(value => value.SpecificationDefinitionId).Distinct().ToArray();
        var definitions = await dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(definition => definitionIds.Contains(definition.Id))
            .ToDictionaryAsync(definition => definition.Id, cancellationToken);
        var optionIds = values.Where(value => value.OptionId.HasValue)
            .Select(value => value.OptionId!.Value)
            .Distinct()
            .ToArray();
        var options = await dbContext.SpecificationOptions.AsNoTracking()
            .Where(option => optionIds.Contains(option.Id))
            .ToDictionaryAsync(option => option.Id, cancellationToken);

        var specifications = values
            .Where(value => definitions.ContainsKey(value.SpecificationDefinitionId))
            .Select(value =>
            {
                var definition = definitions[value.SpecificationDefinitionId];
                var optionCode = value.OptionId.HasValue && options.TryGetValue(value.OptionId.Value, out var option)
                    ? option.Code
                    : null;
                return new SkuSpecValueDto(
                    definition.SemanticKey,
                    definition.DisplayNameZhTw,
                    definition.ValueType.ToString(),
                    value.StringValue,
                    value.DecimalValue,
                    value.BooleanValue,
                    optionCode);
            })
            .ToList();

        var balance = await dbContext.InventoryBalances.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.SkuId == sku.Id, cancellationToken);
        var inventory = balance is null
            ? null
            : new SkuInventorySummary(balance.OnHandQuantity, balance.ReservedQuantity, balance.AvailableQuantity);

        return new SkuDto(
            sku.PublicId,
            sku.SkuCode,
            new ProductRef(product.PublicId, product.ProductCode, product.NameZhTw),
            sku.NameZhTw,
            sku.ListPrice,
            sku.UnitCost,
            sku.WeightKg,
            sku.LengthCm,
            sku.WidthCm,
            sku.HeightCm,
            sku.Status.ToString(),
            sku.IsDefault,
            sku.RequiresPrepayment,
            specifications,
            inventory,
            sku.CreatedAtUtc,
            sku.UpdatedAtUtc,
            sku.RowVersion);
    }
}
