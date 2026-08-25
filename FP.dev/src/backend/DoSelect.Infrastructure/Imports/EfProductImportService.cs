using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.Imports;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// 商品匯入 Preview／Status／Rows／Errors. See ImportContracts.cs's IProductImportService doc
/// comment for why Confirm is not implemented (AuditLog/Outbox gap) and why per-resource
/// ownership scoping is currently a no-op under the present role mapping.
/// </summary>
public sealed class EfProductImportService : IProductImportService
{
    private const int MaxFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxTotalRows = 5_000;

    private readonly DoSelectDbContext _dbContext;

    public EfProductImportService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProductImportBatchDto> PreviewAsync(
        PreviewProductImportRequest request,
        string createdByAdminUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(createdByAdminUserId))
        {
            throw new ArgumentException("The value is required.", nameof(createdByAdminUserId));
        }

        var productsBytes = await ReadFileAsync(request.ProductsFile, "Products", cancellationToken);
        var skusBytes = await ReadFileAsync(request.SkusFile, "Skus", cancellationToken);
        var specificationsBytes = await ReadFileAsync(request.SpecificationsFile, "Specifications", cancellationToken);

        var productRows = ParseCsv(productsBytes, ProductRowParser.Parse, "Products");
        var skuRows = ParseCsv(skusBytes, SkuRowParser.Parse, "Skus");
        var specificationRows = ParseCsv(specificationsBytes, SpecificationRowParser.Parse, "Specifications");

        var totalRowCount = productRows.Count + skuRows.Count + specificationRows.Count;
        if (totalRowCount == 0)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportDatasetMissing,
                "The upload contains no data rows across the three datasets.");
        }

        if (totalRowCount > MaxTotalRows)
        {
            throw DomainProblemException.Validation(
                $"A product import batch may contain at most {MaxTotalRows} rows across all three datasets combined; this upload has {totalRowCount}.");
        }

        var productContexts = await ResolveProductRowsAsync(productRows, cancellationToken);
        var skuContexts = await ResolveSkuRowsAsync(skuRows, productRows, productContexts, cancellationToken);
        await ResolveSpecificationRowsAsync(specificationRows, skuRows, skuContexts, cancellationToken);

        var now = DateTime.UtcNow;
        var batch = new ImportBatch(
            Guid.CreateVersion7(),
            ImportType.Product,
            request.TemplateVersion,
            createdByAdminUserId,
            now.AddHours(24),
            Guid.CreateVersion7(),
            now);
        batch.SetSources(
            SHA256.HashData(productsBytes), request.ProductsFile.OriginalFileName,
            SHA256.HashData(skusBytes), request.SkusFile.OriginalFileName,
            SHA256.HashData(specificationsBytes), request.SpecificationsFile.OriginalFileName,
            now);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.ImportBatches.Add(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var newCount = 0;
            var updatedCount = 0;
            var unchangedCount = 0;
            var errorCount = 0;

            (newCount, updatedCount, unchangedCount, errorCount) = AddRows(
                batch.Id, ImportDataset.Products, productRows, newCount, updatedCount, unchangedCount, errorCount, now);
            (newCount, updatedCount, unchangedCount, errorCount) = AddRows(
                batch.Id, ImportDataset.Skus, skuRows, newCount, updatedCount, unchangedCount, errorCount, now);
            (newCount, updatedCount, unchangedCount, errorCount) = AddRows(
                batch.Id, ImportDataset.Specifications, specificationRows, newCount, updatedCount, unchangedCount, errorCount, now);

            batch.SetPreviewStatistics(totalRowCount, newCount, updatedCount, unchangedCount, errorCount, normalizedContentVersion: 1, now);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOneInProgressBatchConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ImportBatchInProgress,
                "You already have an in-progress product import batch. Finish, expire, or otherwise close it before starting another.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return ToDto(batch);
    }

    public async Task<ProductImportBatchDto?> GetAsync(Guid batchPublicId, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == batchPublicId && candidate.ImportType == ImportType.Product, cancellationToken);
        return batch is null ? null : ToDto(batch);
    }

    public async Task<CursorPage<ImportRowDto>> GetRowsAsync(
        Guid batchPublicId,
        ImportRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var batch = await _dbContext.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == batchPublicId && candidate.ImportType == ImportType.Product, cancellationToken);
        if (batch is null)
        {
            throw DomainProblemException.NotFound($"Product import batch '{batchPublicId}' was not found.");
        }

        var pageSize = query.PageSize is > 0 and <= 200 ? query.PageSize : 50;
        var fingerprint = OpaqueCursorCodec.ComputeFingerprint(
            batchPublicId.ToString(), query.Dataset, query.ErrorsOnly.ToString());

        var rowsQuery = _dbContext.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId == batch.Id);

        if (!string.IsNullOrWhiteSpace(query.Dataset) &&
            Enum.TryParse<ImportDataset>(query.Dataset, ignoreCase: true, out var dataset) &&
            Enum.IsDefined(dataset))
        {
            rowsQuery = rowsQuery.Where(row => row.Dataset == dataset);
        }

        if (query.ErrorsOnly)
        {
            rowsQuery = rowsQuery.Where(row => row.ErrorCodes != null);
        }

        if (OpaqueCursorCodec.TryDecode<RowCursorPayload>(query.Cursor, fingerprint, out var after) && after is not null)
        {
            var afterDataset = after.Dataset;
            var afterSourceRowNumber = after.SourceRowNumber;
            rowsQuery = rowsQuery.Where(row =>
                row.Dataset > afterDataset ||
                (row.Dataset == afterDataset && row.SourceRowNumber > afterSourceRowNumber));
        }

        var page = await rowsQuery
            .OrderBy(row => row.Dataset).ThenBy(row => row.SourceRowNumber)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize)
            .Select(row => new ImportRowDto(
                row.Dataset.ToString(),
                row.SourceRowNumber,
                row.ImportKey,
                row.Action.ToString(),
                string.IsNullOrEmpty(row.ErrorCodes) ? [] : row.ErrorCodes.Split(',', StringSplitOptions.RemoveEmptyEntries),
                row.NormalizedPayloadJson))
            .ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = page[pageSize - 1];
            nextCursor = OpaqueCursorCodec.Encode(new RowCursorPayload(last.Dataset, last.SourceRowNumber), fingerprint);
        }

        return new CursorPage<ImportRowDto>(items, nextCursor, hasMore);
    }

    public async Task<byte[]?> GetErrorsCsvAsync(Guid batchPublicId, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == batchPublicId && candidate.ImportType == ImportType.Product, cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var errorRows = await _dbContext.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId == batch.Id && row.ErrorCodes != null)
            .OrderBy(row => row.Dataset).ThenBy(row => row.SourceRowNumber)
            .ToListAsync(cancellationToken);

        var header = new[] { "dataset", "source_row_number", "import_key", "error_codes" };
        var rows = errorRows.Select(row => (IReadOnlyList<string>)new[]
        {
            row.Dataset.ToString(),
            row.SourceRowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.ImportKey,
            row.ErrorCodes ?? string.Empty,
        });

        return DelimitedTextWriter.Write(header, rows);
    }

    private sealed record RowCursorPayload(ImportDataset Dataset, int SourceRowNumber);

    private sealed record ExistingProductSnapshot(
        long Id, long BrandId, long CategoryId, string NameZhTw,
        string? DescriptionZhTw, int? WarrantyMonths, ProductStatus Status);

    private sealed record ProductRowContext(long? ExistingProductId, long? CategoryId);

    private sealed record ExistingSkuSnapshot(
        long Id, long ProductId, string NameZhTw, decimal ListPrice, decimal UnitCost,
        decimal? WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm,
        bool RequiresPrepayment, SkuStatus Status);

    private sealed record SkuRowContext(long? ExistingSkuId, long? ProductId, long? CategoryId);

    private async Task<Dictionary<string, ProductRowContext>> ResolveProductRowsAsync(
        IReadOnlyList<StagedImportRow<ProductPayload>> rows,
        CancellationToken cancellationToken)
    {
        var brandCodes = rows.Select(r => r.Payload.BrandCode).Where(c => c is not null).Distinct().ToArray();
        var categoryCodes = rows.Select(r => r.Payload.CategoryCode).Where(c => c is not null).Distinct().ToArray();
        var productCodes = rows.Select(r => r.Payload.ProductCode).Where(c => c is not null).Distinct().ToArray();

        var brands = await _dbContext.Brands.AsNoTracking()
            .Where(b => b.IsActive && brandCodes.Contains(b.Code))
            .ToDictionaryAsync(b => b.Code, b => b.Id, cancellationToken);
        var categories = await _dbContext.Categories.AsNoTracking()
            .Where(c => c.IsActive && categoryCodes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, c => c.Id, cancellationToken);
        var existingProducts = await _dbContext.Products.AsNoTracking()
            .Where(p => productCodes.Contains(p.ProductCode))
            .Select(p => new ExistingProductSnapshot(
                p.Id, p.BrandId, p.CategoryId, p.NameZhTw, p.DescriptionZhTw, p.WarrantyMonths, p.Status))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var existingProductsByCode = await _dbContext.Products.AsNoTracking()
            .Where(p => productCodes.Contains(p.ProductCode))
            .ToDictionaryAsync(p => p.ProductCode, p => p.Id, cancellationToken);

        var contexts = new Dictionary<string, ProductRowContext>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            long? categoryId = null;
            if (row.Payload.CategoryCode is not null)
            {
                if (categories.TryGetValue(row.Payload.CategoryCode, out var resolvedCategoryId))
                {
                    categoryId = resolvedCategoryId;
                }
                else
                {
                    row.AddError(DomainErrorCodes.ImportLookupNotFound);
                }
            }

            if (row.Payload.BrandCode is not null && !brands.ContainsKey(row.Payload.BrandCode))
            {
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
            }

            long? existingProductId = null;
            if (row.Payload.ProductCode is not null)
            {
                if (existingProductsByCode.TryGetValue(row.Payload.ProductCode, out var foundId) &&
                    existingProducts.TryGetValue(foundId, out var existing))
                {
                    existingProductId = existing.Id;
                    var brandId = row.Payload.BrandCode is not null && brands.TryGetValue(row.Payload.BrandCode, out var b) ? b : (long?)null;
                    var isUnchanged = brandId == existing.BrandId &&
                        categoryId == existing.CategoryId &&
                        string.Equals(row.Payload.NameZhTw, existing.NameZhTw, StringComparison.Ordinal) &&
                        string.Equals(row.Payload.DescriptionZhTw, existing.DescriptionZhTw, StringComparison.Ordinal) &&
                        row.Payload.WarrantyMonths == existing.WarrantyMonths &&
                        string.Equals(row.Payload.Status, existing.Status.ToString(), StringComparison.OrdinalIgnoreCase);
                    row.Action = isUnchanged ? ImportRowAction.NoChange : ImportRowAction.Update;
                }
                else
                {
                    row.Action = ImportRowAction.Insert;
                }
            }

            contexts[row.ImportKey] = new ProductRowContext(existingProductId, categoryId);
        }

        return contexts;
    }

    private async Task<Dictionary<string, SkuRowContext>> ResolveSkuRowsAsync(
        IReadOnlyList<StagedImportRow<SkuPayload>> rows,
        IReadOnlyList<StagedImportRow<ProductPayload>> productRows,
        Dictionary<string, ProductRowContext> productContexts,
        CancellationToken cancellationToken)
    {
        var productRowsByKey = productRows.ToDictionary(r => r.ImportKey, StringComparer.Ordinal);
        var productKeysNeedingDbLookup = rows
            .Select(r => r.Payload.ProductKey)
            .Where(key => key.Length > 0 && !productRowsByKey.ContainsKey(key))
            .Distinct()
            .ToArray();
        var existingProductsByCode = await _dbContext.Products.AsNoTracking()
            .Where(p => productKeysNeedingDbLookup.Contains(p.ProductCode))
            .Select(p => new { p.ProductCode, p.Id, p.CategoryId })
            .ToDictionaryAsync(p => p.ProductCode, cancellationToken);

        var skuCodes = rows.Select(r => r.Payload.SkuCode).Where(c => c is not null).Distinct().ToArray();
        var existingSkus = await _dbContext.Skus.AsNoTracking()
            .Where(s => skuCodes.Contains(s.SkuCode))
            .Select(s => new ExistingSkuSnapshot(
                s.Id, s.ProductId, s.NameZhTw, s.ListPrice, s.UnitCost,
                s.WeightKg, s.LengthCm, s.WidthCm, s.HeightCm, s.RequiresPrepayment, s.Status))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var existingSkusByCode = await _dbContext.Skus.AsNoTracking()
            .Where(s => skuCodes.Contains(s.SkuCode))
            .ToDictionaryAsync(s => s.SkuCode, s => s.Id, cancellationToken);

        var contexts = new Dictionary<string, SkuRowContext>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            long? resolvedExistingProductId = null;
            long? resolvedCategoryId = null;

            if (productRowsByKey.TryGetValue(row.Payload.ProductKey, out var productRow))
            {
                // References a Product row in this same batch — that product may itself be a
                // new Insert (no DB id yet) or an Update of an existing one; either way its
                // category is whatever ResolveProductRowsAsync already worked out.
                if (productContexts.TryGetValue(productRow.ImportKey, out var productContext))
                {
                    resolvedExistingProductId = productContext.ExistingProductId;
                    resolvedCategoryId = productContext.CategoryId;
                }

                if (productRow.Errors.Count > 0)
                {
                    // The referenced product row itself is invalid — this SKU can't reliably
                    // resolve either, but keep whatever category we did find so specification
                    // validation downstream can still make progress.
                    row.AddError(DomainErrorCodes.ImportLookupNotFound);
                }
            }
            else if (row.Payload.ProductKey.Length > 0 &&
                existingProductsByCode.TryGetValue(row.Payload.ProductKey, out var existingProduct))
            {
                resolvedExistingProductId = existingProduct.Id;
                resolvedCategoryId = existingProduct.CategoryId;
            }
            else
            {
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
            }

            long? existingSkuId = null;
            if (row.Payload.SkuCode is not null)
            {
                if (existingSkusByCode.TryGetValue(row.Payload.SkuCode, out var foundSkuId) &&
                    existingSkus.TryGetValue(foundSkuId, out var existingSku))
                {
                    // Sku.ProductId has no update method in the domain (SKU codes never move
                    // between products) — an existing code resolving to a different product
                    // than this row references is a hard conflict, not a diffable update.
                    if (resolvedExistingProductId is null || existingSku.ProductId != resolvedExistingProductId)
                    {
                        row.AddError(DomainErrorCodes.ImportValidationFailed);
                    }

                    existingSkuId = existingSku.Id;
                    var isUnchanged = row.Payload.NameZhTw == existingSku.NameZhTw &&
                        row.Payload.ListPrice == existingSku.ListPrice &&
                        row.Payload.UnitCost == existingSku.UnitCost &&
                        row.Payload.WeightKg == existingSku.WeightKg &&
                        row.Payload.LengthCm == existingSku.LengthCm &&
                        row.Payload.WidthCm == existingSku.WidthCm &&
                        row.Payload.HeightCm == existingSku.HeightCm &&
                        row.Payload.RequiresPrepayment == existingSku.RequiresPrepayment &&
                        string.Equals(row.Payload.Status, existingSku.Status.ToString(), StringComparison.OrdinalIgnoreCase);
                    row.Action = isUnchanged ? ImportRowAction.NoChange : ImportRowAction.Update;
                }
                else
                {
                    row.Action = ImportRowAction.Insert;
                }
            }
            else
            {
                // Blank sku_code: always a new SKU, system-assigns the code at Confirm time.
                row.Action = ImportRowAction.Insert;
            }

            contexts[row.ImportKey] = new SkuRowContext(existingSkuId, resolvedExistingProductId, resolvedCategoryId);
        }

        return contexts;
    }

    private async Task ResolveSpecificationRowsAsync(
        IReadOnlyList<StagedImportRow<SpecificationPayload>> rows,
        IReadOnlyList<StagedImportRow<SkuPayload>> skuRows,
        Dictionary<string, SkuRowContext> skuContexts,
        CancellationToken cancellationToken)
    {
        var skuRowsByKey = skuRows.ToDictionary(r => r.ImportKey, StringComparer.Ordinal);

        var categoryIds = rows
            .Select(r => ResolveSkuCategoryId(r, skuRowsByKey, skuContexts))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var semanticKeys = rows.Select(r => r.Payload.SemanticKey).Where(k => k is not null).Distinct().ToArray();
        var definitions = await _dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(d => d.IsActive && categoryIds.Contains(d.CategoryId) && semanticKeys.Contains(d.SemanticKey))
            .ToListAsync(cancellationToken);
        var definitionsByKey = definitions.ToDictionary(d => (d.CategoryId, d.SemanticKey));

        var optionCodes = rows.Select(r => r.Payload.OptionCode).Where(c => c is not null).Distinct().ToArray();
        var definitionIds = definitions.Select(d => d.Id).ToArray();
        var options = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(o => o.IsActive && definitionIds.Contains(o.SpecificationDefinitionId) && optionCodes.Contains(o.Code))
            .ToDictionaryAsync(o => (o.SpecificationDefinitionId, o.Code), o => o.Id, cancellationToken);

        var existingSkuIds = skuContexts.Values.Where(c => c.ExistingSkuId.HasValue).Select(c => c.ExistingSkuId!.Value).Distinct().ToArray();
        var existingValues = await _dbContext.SkuSpecificationValues.AsNoTracking()
            .Where(v => existingSkuIds.Contains(v.SkuId))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (!skuRowsByKey.TryGetValue(row.Payload.SkuKey, out var skuRow))
            {
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
                continue;
            }

            if (skuRow.Errors.Count > 0)
            {
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
            }

            var categoryId = ResolveSkuCategoryId(row, skuRowsByKey, skuContexts);
            if (categoryId is null || row.Payload.SemanticKey is null)
            {
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
                continue;
            }

            if (!definitionsByKey.TryGetValue((categoryId.Value, row.Payload.SemanticKey), out var definition))
            {
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
                continue;
            }

            if (!Enum.TryParse<SpecificationValueType>(row.Payload.ValueType, ignoreCase: true, out var valueType) ||
                valueType != definition.ValueType)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
                continue;
            }

            long? optionId = null;
            if (valueType == SpecificationValueType.Option)
            {
                if (row.Payload.OptionCode is null || !options.TryGetValue((definition.Id, row.Payload.OptionCode), out var foundOptionId))
                {
                    row.AddError(DomainErrorCodes.ImportLookupNotFound);
                    continue;
                }

                optionId = foundOptionId;
            }

            skuContexts.TryGetValue(skuRow.ImportKey, out var skuContext);
            if (skuContext?.ExistingSkuId is long existingSkuId)
            {
                var existingValue = existingValues.FirstOrDefault(v => v.SkuId == existingSkuId && v.SpecificationDefinitionId == definition.Id);
                if (existingValue is null)
                {
                    row.Action = ImportRowAction.Insert;
                }
                else
                {
                    var isUnchanged = valueType switch
                    {
                        SpecificationValueType.String => existingValue.StringValue == row.Payload.StringValue,
                        SpecificationValueType.Decimal => existingValue.DecimalValue == row.Payload.DecimalValue,
                        SpecificationValueType.Boolean => existingValue.BooleanValue == row.Payload.BooleanValue,
                        SpecificationValueType.Option => existingValue.OptionId == optionId,
                        _ => false,
                    };
                    row.Action = isUnchanged ? ImportRowAction.NoChange : ImportRowAction.Update;
                }
            }
            else
            {
                row.Action = ImportRowAction.Insert;
            }
        }
    }

    private static long? ResolveSkuCategoryId(
        StagedImportRow<SpecificationPayload> row,
        Dictionary<string, StagedImportRow<SkuPayload>> skuRowsByKey,
        Dictionary<string, SkuRowContext> skuContexts)
    {
        if (!skuRowsByKey.TryGetValue(row.Payload.SkuKey, out var skuRow))
        {
            return null;
        }

        return skuContexts.TryGetValue(skuRow.ImportKey, out var context) ? context.CategoryId : null;
    }

    private (int New, int Updated, int Unchanged, int Errors) AddRows<TPayload>(
        long batchId,
        ImportDataset dataset,
        IReadOnlyList<StagedImportRow<TPayload>> rows,
        int newCount,
        int updatedCount,
        int unchangedCount,
        int errorCount,
        DateTime now)
    {
        foreach (var row in rows)
        {
            var action = row.Errors.Count > 0 ? ImportRowAction.Error : row.Action;
            switch (action)
            {
                case ImportRowAction.Insert: newCount++; break;
                case ImportRowAction.Update: updatedCount++; break;
                case ImportRowAction.NoChange: unchangedCount++; break;
                default: errorCount++; break;
            }

            var normalizedPayloadJson = JsonSerializer.Serialize(row.Payload);
            var rawJson = JsonSerializer.Serialize(row.RawFields);
            var errorCodes = row.Errors.Count > 0 ? string.Join(",", row.Errors.Distinct()) : null;
            var rowHash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPayloadJson));

            _dbContext.ImportRows.Add(new ImportRow(
                batchId,
                dataset,
                row.SourceRowNumber,
                row.ImportKey,
                action,
                normalizedPayloadJson,
                errorCodes,
                rowHash,
                rawJson,
                now));
        }

        return (newCount, updatedCount, unchangedCount, errorCount);
    }

    private static IReadOnlyList<StagedImportRow<TPayload>> ParseCsv<TPayload>(
        byte[] content,
        Func<IReadOnlyList<string[]>, IReadOnlyList<StagedImportRow<TPayload>>> parse,
        string datasetLabel)
    {
        IReadOnlyList<string[]> rows;
        try
        {
            rows = DelimitedTextReader.Parse(content);
        }
        catch (FormatException exception)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"The {datasetLabel} file is not valid CSV: {exception.Message}");
        }

        try
        {
            return parse(rows);
        }
        catch (ImportBatchParseException exception)
        {
            throw DomainProblemException.BadRequest(exception.ErrorCode, exception.Message);
        }
    }

    private static async Task<byte[]> ReadFileAsync(
        IncomingImportFile file,
        string datasetLabel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.HasFile)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportDatasetMissing,
                $"The {datasetLabel} file is required for a product import.");
        }

        if (file.DeclaredLength is > MaxFileSizeBytes)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"The {datasetLabel} file exceeds the 10 MB limit.");
        }

        await using var stream = file.OpenRead();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxFileSizeBytes)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"The {datasetLabel} file exceeds the 10 MB limit.");
        }

        return buffer.ToArray();
    }

    private static bool IsOneInProgressBatchConflict(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains(
            "UX_ImportBatches_CreatedByAdminUserId_ImportType", StringComparison.Ordinal) == true;

    private static ProductImportBatchDto ToDto(ImportBatch batch) => new(
        batch.PublicId,
        batch.ImportType.ToString(),
        batch.TemplateVersion,
        batch.Status.ToString(),
        batch.CreatedByAdminUserId,
        batch.CreatedAtUtc,
        batch.ExpiresAtUtc,
        batch.RowCount,
        batch.NewCount,
        batch.UpdatedCount,
        batch.UnchangedCount,
        batch.ErrorCount,
        batch.ConfirmedAtUtc,
        batch.RowVersion);
}
