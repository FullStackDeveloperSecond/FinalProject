using System.Text;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfSkuAdminService : ISkuAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfSkuAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SkuDto> CreateAsync(
        Guid productPublicId,
        CreateSkuRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(candidate => candidate.PublicId == productPublicId, cancellationToken);
        if (product is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ReferenceNotFound,
                $"Product '{productPublicId}' was not found.");
        }

        var normalizedSkuCode = NormalizeCode(request.SkuCode);
        var codeExists = await _dbContext.Skus.AsNoTracking()
            .AnyAsync(candidate => candidate.SkuCode == normalizedSkuCode, cancellationToken);
        if (codeExists)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SkuCodeDuplicate,
                $"SKU code '{request.SkuCode}' already exists.");
        }

        var status = ParseStatus(request.Status);
        var now = DateTime.UtcNow;

        if (request.IsDefault)
        {
            await ClearExistingDefaultAsync(product.Id, excludeSkuId: null, now, cancellationToken);
        }

        var sku = new Sku(
            Guid.CreateVersion7(),
            request.SkuCode,
            product.Id,
            request.NameZhTw,
            request.ListPrice,
            request.UnitCost,
            now);
        sku.UpdatePackageDimensions(request.WeightKg, request.LengthCm, request.WidthCm, request.HeightCm, now);
        sku.UpdateCommercialDetails(
            request.NameZhTw,
            request.ListPrice,
            request.UnitCost,
            request.IsDefault,
            request.RequiresPrepayment,
            now);
        sku.ChangeStatus(status, now);

        _dbContext.Skus.Add(sku);

        // ReplaceSpecificationsAsync needs sku.Id, only assigned once the first
        // SaveChangesAsync inserts the row — can't collapse into one call. Wrapping both in
        // a transaction means an invalid specification (or the ClearExistingDefaultAsync
        // side effect above, already pending in the same change tracker) doesn't leave a
        // half-created SKU or a wrongly-cleared previous default behind: any failure rolls
        // back the whole thing.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            await ReplaceSpecificationsAsync(sku.Id, product.CategoryId, request.Specifications, now, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await SkuAdminMapping.ToDtoAsync(_dbContext, sku, product, cancellationToken);
    }

    public async Task<SkuDto?> GetByPublicIdAsync(Guid skuPublicId, CancellationToken cancellationToken)
    {
        var sku = await _dbContext.Skus.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == skuPublicId, cancellationToken);
        if (sku is null)
        {
            return null;
        }

        var product = await _dbContext.Products.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == sku.ProductId, cancellationToken);

        return await SkuAdminMapping.ToDtoAsync(_dbContext, sku, product, cancellationToken);
    }

    public async Task<SkuDto> UpdateAsync(
        Guid skuPublicId,
        UpdateSkuRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sku = await _dbContext.Skus
            .FirstOrDefaultAsync(candidate => candidate.PublicId == skuPublicId, cancellationToken);
        if (sku is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"SKU '{skuPublicId}' was not found.");
        }

        var product = await _dbContext.Products.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == sku.ProductId, cancellationToken);

        var status = ParseStatus(request.Status);
        var now = DateTime.UtcNow;

        if (request.IsDefault && !sku.IsDefault)
        {
            await ClearExistingDefaultAsync(sku.ProductId, sku.Id, now, cancellationToken);
        }

        _dbContext.Entry(sku).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        sku.UpdatePackageDimensions(request.WeightKg, request.LengthCm, request.WidthCm, request.HeightCm, now);
        sku.UpdateCommercialDetails(
            request.NameZhTw,
            request.ListPrice,
            request.UnitCost,
            request.IsDefault,
            request.RequiresPrepayment,
            now);
        sku.ChangeStatus(status, now);

        await ReplaceSpecificationsAsync(sku.Id, product.CategoryId, request.Specifications, now, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The SKU was updated by someone else. Reload and try again.");
        }

        return await SkuAdminMapping.ToDtoAsync(_dbContext, sku, product, cancellationToken);
    }

    public async Task DeleteAsync(Guid skuPublicId, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var sku = await _dbContext.Skus
            .FirstOrDefaultAsync(candidate => candidate.PublicId == skuPublicId, cancellationToken);
        if (sku is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"SKU '{skuPublicId}' was not found.");
        }

        // Every table below has an OnDelete(DeleteBehavior.Restrict) foreign key to Skus —
        // this check exists specifically so a real reference surfaces as a stable 409
        // instead of the SqlException that Restrict would otherwise throw (and that
        // GlobalExceptionHandler would map to an opaque 500).
        var isReferenced = sku.Status != SkuStatus.Draft ||
            await _dbContext.InventoryBalances.AsNoTracking().AnyAsync(b => b.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.InventoryReservations.AsNoTracking().AnyAsync(r => r.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.InventoryReconciliationCases.AsNoTracking().AnyAsync(c => c.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.CartItems.AsNoTracking().AnyAsync(c => c.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.BuildListItems.AsNoTracking().AnyAsync(b => b.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.OrderItems.AsNoTracking().AnyAsync(o => o.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.ProductImages.AsNoTracking().AnyAsync(i => i.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.SkuTranslations.AsNoTracking().AnyAsync(t => t.SkuId == sku.Id, cancellationToken) ||
            await _dbContext.SalePrices.AsNoTracking().AnyAsync(p => p.SkuId == sku.Id, cancellationToken);
        if (isReferenced)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SkuDeleteReferenced,
                "Only a never-referenced draft SKU can be permanently deleted.");
        }

        _dbContext.Entry(sku).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;

        var specValues = await _dbContext.SkuSpecificationValues
            .Where(value => value.SkuId == sku.Id)
            .ToListAsync(cancellationToken);
        _dbContext.SkuSpecificationValues.RemoveRange(specValues);
        _dbContext.Skus.Remove(sku);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The SKU was updated by someone else. Reload and try again.");
        }
    }

    private async Task ClearExistingDefaultAsync(
        long productId,
        long? excludeSkuId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var currentDefault = await _dbContext.Skus
            .FirstOrDefaultAsync(
                candidate => candidate.ProductId == productId &&
                    candidate.IsDefault &&
                    candidate.Id != (excludeSkuId ?? -1),
                cancellationToken);

        currentDefault?.UpdateCommercialDetails(
            currentDefault.NameZhTw,
            currentDefault.ListPrice,
            currentDefault.UnitCost,
            isDefault: false,
            currentDefault.RequiresPrepayment,
            now);
    }

    private async Task ReplaceSpecificationsAsync(
        long skuId,
        long categoryId,
        IReadOnlyList<SpecValueInput> specifications,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SkuSpecificationValues
            .Where(value => value.SkuId == skuId)
            .ToListAsync(cancellationToken);
        _dbContext.SkuSpecificationValues.RemoveRange(existing);

        if (specifications.Count == 0)
        {
            return;
        }

        if (specifications.Count > 100)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "A SKU accepts at most 100 specification values.");
        }

        var semanticKeys = specifications.Select(input => NormalizeCode(input.SemanticKey)).Distinct().ToArray();
        var definitions = await _dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(definition =>
                definition.CategoryId == categoryId &&
                semanticKeys.Contains(definition.SemanticKey) &&
                definition.IsActive)
            .ToDictionaryAsync(definition => definition.SemanticKey, cancellationToken);

        foreach (var input in specifications)
        {
            var key = NormalizeCode(input.SemanticKey);
            if (!definitions.TryGetValue(key, out var definition))
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.SpecificationInvalid,
                    $"Specification '{input.SemanticKey}' is not defined for this product's category.");
            }

            if (!Enum.TryParse<SpecificationValueType>(input.ValueType, ignoreCase: true, out var valueType) ||
                valueType != definition.ValueType)
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.SpecificationInvalid,
                    $"Specification '{input.SemanticKey}' expects value type '{definition.ValueType}'.");
            }

            string? stringValue = null;
            decimal? decimalValue = null;
            bool? booleanValue = null;
            long? optionId = null;

            switch (definition.ValueType)
            {
                case SpecificationValueType.String:
                    stringValue = RequireValue(input.StringValue, input.SemanticKey);
                    break;
                case SpecificationValueType.Decimal:
                    decimalValue = RequireValue(input.DecimalValue, input.SemanticKey);
                    break;
                case SpecificationValueType.Boolean:
                    booleanValue = RequireValue(input.BooleanValue, input.SemanticKey);
                    break;
                case SpecificationValueType.Option:
                    optionId = await ResolveOptionIdAsync(definition.Id, input.OptionCode, cancellationToken);
                    break;
            }

            _dbContext.SkuSpecificationValues.Add(new SkuSpecificationValue(
                skuId,
                definition.Id,
                stringValue,
                decimalValue,
                booleanValue,
                optionId,
                null,
                now));
        }
    }

    private async Task<long> ResolveOptionIdAsync(
        long definitionId,
        string? code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationInvalid,
                "A specification option code is required.");
        }

        var normalized = NormalizeCode(code);
        var optionId = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(option =>
                option.SpecificationDefinitionId == definitionId &&
                option.Code == normalized &&
                option.IsActive)
            .Select(option => (long?)option.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (optionId is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationInvalid,
                $"Specification option '{code}' is not recognized.");
        }

        return optionId.Value;
    }

    private static T RequireValue<T>(T? value, string semanticKey)
        where T : struct
    {
        if (value is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationInvalid,
                $"A value is required for specification '{semanticKey}'.");
        }

        return value.Value;
    }

    private static string RequireValue(string? value, string semanticKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationInvalid,
                $"A value is required for specification '{semanticKey}'.");
        }

        return value;
    }

    private static SkuStatus ParseStatus(string status)
    {
        if (!AdminCatalogQueryValidator.TryParseDefinedEnumName<SkuStatus>(status, out var parsed))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"The SKU status '{status}' is not supported.");
        }

        return parsed;
    }

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
