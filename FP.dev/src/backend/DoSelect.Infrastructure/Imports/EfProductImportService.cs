using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.Imports;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// 商品匯入 Preview／Status／Rows／Errors／Confirm. See ImportContracts.cs's IProductImportService
/// doc comment for the Confirm contract and why per-resource ownership scoping is currently a
/// no-op under the present role mapping.
/// </summary>
public sealed class EfProductImportService : IProductImportService
{
    private const int MaxFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxTotalRows = 5_000;

    /// <summary>
    /// 組長 PR #74 review item 3: only the current template version (and, per spec, the previous
    /// one once a newer version ships) is accepted; anything else — including the 0 that model
    /// binding produces when the multipart field is missing — is rejected whole-batch with the
    /// current template information. v1 is the first template, so there is no previous version to
    /// convert yet; when v2 ships, v1 handling belongs here as an explicit conversion step.
    /// </summary>
    private const int CurrentTemplateVersion = 1;

    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;

    public EfProductImportService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
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

        if (request.TemplateVersion != CurrentTemplateVersion)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"Template version '{request.TemplateVersion}' is not supported. The current product-import template is version {CurrentTemplateVersion} (datasets: Products, Skus, Specifications).");
        }

        // 組長 PR #74 review item 4: the one-in-progress unique index counts Ready batches, but an
        // expired Ready batch only used to flip to Expired when someone called *its* Confirm — so
        // re-uploading kept failing with import_batch_in_progress. Close out any of this admin's
        // batches whose 24-hour window has passed before staging the new one.
        var now = DateTime.UtcNow;
        var staleBatches = await _dbContext.ImportBatches
            .Where(candidate => candidate.CreatedByAdminUserId == createdByAdminUserId &&
                candidate.ImportType == ImportType.Product &&
                candidate.ExpiresAtUtc <= now &&
                (candidate.Status == ImportBatchStatus.Uploaded ||
                 candidate.Status == ImportBatchStatus.Validating ||
                 candidate.Status == ImportBatchStatus.Ready ||
                 candidate.Status == ImportBatchStatus.Committing))
            .ToListAsync(cancellationToken);
        if (staleBatches.Count > 0)
        {
            foreach (var stale in staleBatches)
            {
                stale.ChangeStatus(ImportBatchStatus.Expired, now);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
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

        // 組長 PR #74 review item 5: invalid inputs used to be silently widened — an unknown
        // dataset queried everything, a bad cursor restarted at page one, an out-of-range pageSize
        // became 50 — so a caller could believe a filter was applied when it was not. Each is now
        // a stable validation error instead.
        if (query.PageSize is not (> 0 and <= 200))
        {
            throw DomainProblemException.Validation(
                $"pageSize must be between 1 and 200; got {query.PageSize}.");
        }

        var pageSize = query.PageSize;
        var fingerprint = OpaqueCursorCodec.ComputeFingerprint(
            batchPublicId.ToString(), query.Dataset, query.ErrorsOnly.ToString());

        var rowsQuery = _dbContext.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId == batch.Id);

        if (!string.IsNullOrWhiteSpace(query.Dataset))
        {
            if (!Enum.TryParse<ImportDataset>(query.Dataset, ignoreCase: true, out var dataset) ||
                !Enum.IsDefined(dataset))
            {
                throw DomainProblemException.Validation(
                    $"Unknown dataset '{query.Dataset}'. Valid values: Products, Skus, Specifications.");
            }

            rowsQuery = rowsQuery.Where(row => row.Dataset == dataset);
        }

        if (query.ErrorsOnly)
        {
            rowsQuery = rowsQuery.Where(row => row.ErrorCodes != null);
        }

        if (!string.IsNullOrWhiteSpace(query.Cursor) &&
            (!OpaqueCursorCodec.TryDecode<RowCursorPayload>(query.Cursor, fingerprint, out _)))
        {
            throw DomainProblemException.Validation(
                "The cursor is invalid or was issued under different filters. Restart from the first page.");
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
                UnwrapPayloadJson(row.NormalizedPayloadJson)))
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


    public async Task<ProductImportBatchDto> ConfirmAsync(
        Guid batchPublicId,
        string adminUserId,
        byte[] rowVersion,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        ArgumentNullException.ThrowIfNull(auditContext);
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new ArgumentException("The value is required.", nameof(adminUserId));
        }

        var batch = await _dbContext.ImportBatches
            .FirstOrDefaultAsync(
                candidate => candidate.PublicId == batchPublicId && candidate.ImportType == ImportType.Product,
                cancellationToken)
            ?? throw DomainProblemException.NotFound($"Product import batch '{batchPublicId}' was not found.");

        var now = DateTime.UtcNow;
        if (batch.Status == ImportBatchStatus.Committed)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ImportAlreadyCommitted,
                "This batch has already been committed. Committed batches cannot be re-sent.");
        }

        if (batch.Status == ImportBatchStatus.Expired || now >= batch.ExpiresAtUtc)
        {
            if (batch.Status != ImportBatchStatus.Expired)
            {
                batch.ChangeStatus(ImportBatchStatus.Expired, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            throw DomainProblemException.Gone(
                DomainErrorCodes.ImportBatchExpired,
                "The preview has expired (24 hours). Upload the files again to create a new batch.");
        }

        if (batch.Status != ImportBatchStatus.Ready)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ImportValidationFailed,
                $"Only a Ready batch can be confirmed; this batch is {batch.Status}. Fix the source files and create a new batch.");
        }

        // 組長 PR #74 review item 1: 規格要求「建立者且具 CatalogImport.Execute」— the role half is
        // the controller policy, the creator half is here. Another CatalogManager must not be able
        // to commit a preview they never created or reviewed.
        if (!string.Equals(batch.CreatedByAdminUserId, adminUserId, StringComparison.Ordinal))
        {
            throw DomainProblemException.Forbidden(
                "Only the administrator who created this preview batch can confirm it.");
        }

        // 組長 PR #74 review item 3: re-checked at confirm too — a batch staged under a template
        // this build no longer supports must not be applied with the current parsers.
        if (batch.TemplateVersion != CurrentTemplateVersion)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"This batch was staged with template version '{batch.TemplateVersion}'; the current product-import template is version {CurrentTemplateVersion}. Re-upload with the current template.");
        }

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);

        var storedRows = await _dbContext.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId == batch.Id)
            .OrderBy(row => row.Dataset).ThenBy(row => row.SourceRowNumber)
            .ToListAsync(cancellationToken);

        var (productRows, productStoredActions) = Rehydrate<ProductPayload>(storedRows, ImportDataset.Products);
        var (skuRows, skuStoredActions) = Rehydrate<SkuPayload>(storedRows, ImportDataset.Skus);
        var (specificationRows, specificationStoredActions) = Rehydrate<SpecificationPayload>(storedRows, ImportDataset.Specifications);

        _dbContext.Entry(batch).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;

        var committingSaved = false;

        // 組長 PR #74 round-2 review (P1): the Update rows' RowVersion preimages protect the
        // entities this batch *writes*, but the reference rows that decide validity — Brand /
        // Category / SpecificationDefinition / SpecificationOption IsActive, and "this key does
        // not exist yet" for Inserts — were only read under ReadCommitted, which releases the
        // shared lock as soon as each query completes. Serializable keeps shared/key-range locks
        // on every row and range the resolvers read until commit, so nothing another transaction
        // does between the resolver and SaveChanges can invalidate what was validated: a
        // deactivation of a referenced brand blocks until this transaction commits, and an insert
        // into a key range the resolvers probed for an Insert row blocks the same way (then dies
        // on the unique index itself, not us). The residual failure modes are a deadlock victim
        // (→ 409 concurrency_conflict, batch stays Ready, retryable) and a unique-index loss when
        // the other writer committed first (→ batch Failed + import_validation_failed, re-Preview)
        // — both mapped below instead of surfacing as generic 500s.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            batch.ChangeStatus(ImportBatchStatus.Committing, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            committingSaved = true;

            // 商品匯入確認 step 3, hardened per 組長 PR #74 review item 2: the re-validation runs
            // *inside* the confirm transaction, and every row's outcome must still match what the
            // admin previewed (a product code created meanwhile, a category deactivated, an
            // Insert↔Update flip). Action equality alone cannot see an Update whose underlying
            // entity was edited since Preview — that is closed below in ApplyAsync, where every
            // Update write carries the Preview-time RowVersion as its EF concurrency original
            // value, so even a modification racing this very transaction fails the write instead
            // of being overwritten.
            var productContexts = await ResolveProductRowsAsync(productRows, cancellationToken);
            var skuContexts = await ResolveSkuRowsAsync(skuRows, productRows, productContexts, cancellationToken);
            await ResolveSpecificationRowsAsync(specificationRows, skuRows, skuContexts, cancellationToken);

            EnsureUnchangedSincePreview(productRows, productStoredActions, ImportDataset.Products);
            EnsureUnchangedSincePreview(skuRows, skuStoredActions, ImportDataset.Skus);
            EnsureUnchangedSincePreview(specificationRows, specificationStoredActions, ImportDataset.Specifications);

            var summary = await ApplyAsync(productRows, productContexts, skuRows, skuContexts, specificationRows, now, cancellationToken);

            batch.Complete(JsonSerializer.Serialize(summary), now);
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.CatalogImportConfirm,
                AuditResourceTypes.ImportBatch,
                batch.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("status", ImportBatchStatus.Ready.ToString(), ImportBatchStatus.Committed.ToString()),
                    AuditFieldChange.Code("templateVersion", null, batch.TemplateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    AuditFieldChange.Code("rowCount", null, batch.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    AuditFieldChange.Code("newCount", null, batch.NewCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    AuditFieldChange.Code("updatedCount", null, batch.UpdatedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    AuditFieldChange.Code("unchangedCount", null, batch.UnchangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ],
                reason: "catalog_import_confirm",
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (!committingSaved)
            {
                throw DomainProblemException.Conflict(
                    "concurrency_conflict",
                    "The batch was modified by someone else. Reload and try again.");
            }

            // An apply-phase concurrency failure means a referenced entity's RowVersion no longer
            // matches its Preview-time preimage — someone changed the catalog after the preview
            // the admin approved (組長 PR #74 review item 2). Mark the batch Failed and demand a
            // fresh Preview; nothing was applied.
            await MarkFailedBestEffortAsync(batch.Id, cancellationToken);
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ImportValidationFailed,
                "The catalog changed after this preview was taken. Re-run Preview and confirm the new batch.");
        }
        catch (DbUpdateException exception) when (IsUniqueKeyViolation(exception))
        {
            // 組長 PR #74 round-2 review (P1): an Insert key that another request committed first.
            // Under Serializable this only happens when the competing writer got in BEFORE our
            // resolver read (after it, the range lock makes them wait behind us) — the catalog no
            // longer matches the preview, so the batch fails like any other post-preview drift.
            await transaction.RollbackAsync(cancellationToken);
            await MarkFailedBestEffortAsync(batch.Id, cancellationToken);
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ImportValidationFailed,
                "Another request created one of this batch's new keys first. Re-run Preview and confirm the new batch.");
        }
        catch (Exception exception) when (IsDeadlockVictim(exception))
        {
            // Serializable's price: two imports (or an import and a catalog write) can deadlock
            // and SQL Server kills one. Nothing was applied and the batch reverts to Ready with
            // the rollback — a straight retry is the correct client move, so this is the stable
            // 409, not a Failed batch and not a 500.
            await transaction.RollbackAsync(cancellationToken);
            throw DomainProblemException.Conflict(
                "concurrency_conflict",
                "Concurrent catalog activity interrupted the confirm. Retry the confirm.");
        }
        catch (Exception)
        {
            // 商品匯入確認 step 5: any row failure rolls the entire catalog write back — no partial
            // success. The batch itself is then marked Failed in a fresh save outside the rolled-back
            // transaction (spec: "Batch 記錄安全錯誤"; a Failed batch requires a corrected re-upload).
            await transaction.RollbackAsync(cancellationToken);
            await MarkFailedBestEffortAsync(batch.Id, cancellationToken);
            throw;
        }

        return ToDto(batch);
    }

    private static (IReadOnlyList<StagedImportRow<TPayload>> Rows, Dictionary<string, ImportRowAction> StoredActions)
        Rehydrate<TPayload>(IReadOnlyList<ImportRow> storedRows, ImportDataset dataset)
    {
        var rows = new List<StagedImportRow<TPayload>>();
        var actions = new Dictionary<string, ImportRowAction>(StringComparer.Ordinal);
        foreach (var stored in storedRows.Where(row => row.Dataset == dataset))
        {
            var envelope = JsonSerializer.Deserialize<RowEnvelope<TPayload>>(stored.NormalizedPayloadJson);
            if (envelope is null || envelope.Payload is null)
            {
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.ImportValidationFailed,
                    $"Stored row '{stored.ImportKey}' has an unreadable payload; re-run Preview.");
            }

            rows.Add(new StagedImportRow<TPayload>
            {
                SourceRowNumber = stored.SourceRowNumber,
                ImportKey = stored.ImportKey,
                // Confirm only ever rehydrates a Ready batch, which by definition has no error
                // rows and therefore no synthetic storage keys — the stored key IS the business key.
                OriginalKey = stored.ImportKey,
                Payload = envelope.Payload,
                RawFields = [],
                PreimageRowVersion = envelope.PreimageRowVersion,
            });
            actions[stored.ImportKey] = stored.Action;
        }

        return (rows, actions);
    }

    private static void EnsureUnchangedSincePreview<TPayload>(
        IReadOnlyList<StagedImportRow<TPayload>> rows,
        IReadOnlyDictionary<string, ImportRowAction> storedActions,
        ImportDataset dataset)
    {
        foreach (var row in rows)
        {
            var fresh = row.Errors.Count > 0 ? ImportRowAction.Error : row.Action;
            var stored = storedActions.TryGetValue(row.ImportKey, out var action) ? action : ImportRowAction.Error;
            if (fresh != stored)
            {
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.ImportValidationFailed,
                    $"The catalog changed since this preview: {dataset} row '{row.ImportKey}' would now be {fresh} but was previewed as {stored}. Re-run Preview and confirm the new batch.");
            }
        }
    }

    private sealed record ConfirmSummary(
        int ProductsInserted, int ProductsUpdated,
        int SkusInserted, int SkusUpdated,
        int SpecificationsInserted, int SpecificationsUpdated);

    private async Task<ConfirmSummary> ApplyAsync(
        IReadOnlyList<StagedImportRow<ProductPayload>> productRows,
        Dictionary<string, ProductRowContext> productContexts,
        IReadOnlyList<StagedImportRow<SkuPayload>> skuRows,
        Dictionary<string, SkuRowContext> skuContexts,
        IReadOnlyList<StagedImportRow<SpecificationPayload>> specificationRows,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var brandCodes = productRows.Select(row => row.Payload.BrandCode).Where(code => code is not null).Distinct().ToArray();
        var brandIds = await _dbContext.Brands.AsNoTracking()
            .Where(brand => brand.IsActive && brandCodes.Contains(brand.Code))
            .ToDictionaryAsync(brand => brand.Code, brand => brand.Id, cancellationToken);

        // ---- Products ----
        var productsInserted = 0;
        var productsUpdated = 0;
        var createdProductsByKey = new Dictionary<string, Product>(StringComparer.Ordinal);
        var updateProductIds = productRows
            .Where(row => row.Action == ImportRowAction.Update)
            .Select(row => productContexts[row.ImportKey].ExistingProductId!.Value)
            .ToArray();
        var trackedProducts = await _dbContext.Products
            .Where(product => updateProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        foreach (var row in productRows)
        {
            var payload = row.Payload;
            switch (row.Action)
            {
                case ImportRowAction.Insert:
                    {
                        var brandId = brandIds[payload.BrandCode!];
                        var categoryId = productContexts[row.ImportKey].CategoryId!.Value;
                        var product = new Product(Guid.CreateVersion7(), payload.ProductCode!, brandId, categoryId, payload.NameZhTw!, now);
                        product.UpdateDetails(brandId, categoryId, payload.NameZhTw!, payload.DescriptionZhTw, payload.WarrantyMonths, isFeatured: false, now);
                        product.ChangeStatus(Enum.Parse<ProductStatus>(payload.Status!, ignoreCase: true), now);
                        _dbContext.Products.Add(product);
                        createdProductsByKey[row.ImportKey] = product;
                        productsInserted++;
                        break;
                    }

                case ImportRowAction.Update:
                    {
                        var product = trackedProducts[productContexts[row.ImportKey].ExistingProductId!.Value];
                        // 組長 PR #74 review item 2: the write itself enforces "unchanged since
                        // Preview" — EF sends the Preview-time RowVersion as the concurrency check.
                        if (row.PreimageRowVersion is not null)
                        {
                            _dbContext.Entry(product).Property(candidate => candidate.RowVersion).OriginalValue = row.PreimageRowVersion;
                        }

                        var brandId = brandIds[payload.BrandCode!];
                        var categoryId = productContexts[row.ImportKey].CategoryId!.Value;
                        product.UpdateDetails(brandId, categoryId, payload.NameZhTw!, payload.DescriptionZhTw, payload.WarrantyMonths, product.IsFeatured, now);
                        product.ChangeStatus(Enum.Parse<ProductStatus>(payload.Status!, ignoreCase: true), now);
                        productsUpdated++;
                        break;
                    }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // ---- SKUs ----
        var skusInserted = 0;
        var skusUpdated = 0;
        var createdSkusByKey = new Dictionary<string, Sku>(StringComparer.Ordinal);
        var skuProductIds = new Dictionary<string, long>(StringComparer.Ordinal);
        var defaultAssignedProductIds = new HashSet<long>();
        var updateSkuIds = skuRows
            .Where(row => row.Action == ImportRowAction.Update)
            .Select(row => skuContexts[row.ImportKey].ExistingSkuId!.Value)
            .ToArray();
        var trackedSkus = await _dbContext.Skus
            .Where(sku => updateSkuIds.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);

        foreach (var row in skuRows)
        {
            var payload = row.Payload;
            var productId = skuContexts[row.ImportKey].ProductId
                ?? createdProductsByKey[payload.ProductKey].Id;
            skuProductIds[row.ImportKey] = productId;

            switch (row.Action)
            {
                case ImportRowAction.Insert:
                    {
                        // 匯入模板: 空白 sku_code 表示新增並由系統產碼 — a GUID-derived code keeps the
                        // system-assigned namespace collision-free without a counter roundtrip;
                        // UX_Skus_SkuCode still backstops it.
                        var code = payload.SkuCode ?? $"SKU-{Guid.CreateVersion7():N}";
                        var sku = new Sku(Guid.CreateVersion7(), code, productId, payload.NameZhTw!, payload.ListPrice!.Value, payload.UnitCost!.Value, now);
                        sku.UpdatePackageDimensions(payload.WeightKg, payload.LengthCm, payload.WidthCm, payload.HeightCm, now);
                        // The first imported SKU of a *newly imported* product becomes its default —
                        // the catalog rules require every product to keep exactly one default SKU, and
                        // a brand-new product has no other candidate. Existing products keep theirs.
                        var isDefault = createdProductsByKey.ContainsKey(payload.ProductKey) && defaultAssignedProductIds.Add(productId);
                        sku.UpdateCommercialDetails(payload.NameZhTw!, payload.ListPrice!.Value, payload.UnitCost!.Value, isDefault, payload.RequiresPrepayment ?? false, now);
                        sku.ChangeStatus(Enum.Parse<SkuStatus>(payload.Status!, ignoreCase: true), now);
                        _dbContext.Skus.Add(sku);
                        createdSkusByKey[row.ImportKey] = sku;
                        skusInserted++;
                        break;
                    }

                case ImportRowAction.Update:
                    {
                        var sku = trackedSkus[skuContexts[row.ImportKey].ExistingSkuId!.Value];
                        if (row.PreimageRowVersion is not null)
                        {
                            _dbContext.Entry(sku).Property(candidate => candidate.RowVersion).OriginalValue = row.PreimageRowVersion;
                        }

                        var previousUnitCost = sku.UnitCost;
                        sku.UpdatePackageDimensions(payload.WeightKg, payload.LengthCm, payload.WidthCm, payload.HeightCm, now);
                        sku.UpdateCommercialDetails(payload.NameZhTw!, payload.ListPrice!.Value, payload.UnitCost!.Value, sku.IsDefault, payload.RequiresPrepayment ?? false, now);
                        sku.ChangeStatus(Enum.Parse<SkuStatus>(payload.Status!, ignoreCase: true), now);

                        // Same semantics as EfSkuAdminService.UpdateAsync: a unit-cost change on a SKU
                        // that has an inventory balance must leave a zero-delta CostChange movement so
                        // the M-15 turnover report keeps a correct cost basis.
                        if (previousUnitCost != payload.UnitCost!.Value)
                        {
                            var valuationBalance = await _dbContext.InventoryBalances.AsNoTracking()
                                .SingleOrDefaultAsync(candidate => candidate.SkuId == sku.Id, cancellationToken);
                            if (valuationBalance is not null)
                            {
                                _dbContext.InventoryMovements.Add(new DoSelect.Domain.Inventory.InventoryMovement(
                                    Guid.CreateVersion7(),
                                    sku.Id,
                                    reservationId: null,
                                    movementType: "CostChange",
                                    onHandDelta: 0,
                                    reservedDelta: 0,
                                    beforeOnHand: valuationBalance.OnHandQuantity,
                                    afterOnHand: valuationBalance.OnHandQuantity,
                                    beforeReserved: valuationBalance.ReservedQuantity,
                                    afterReserved: valuationBalance.ReservedQuantity,
                                    unitCostSnapshot: payload.UnitCost!.Value,
                                    reasonCode: "sku_unit_cost_changed",
                                    referenceType: "Sku",
                                    referencePublicId: sku.PublicId,
                                    actorUserId: null,
                                    occurredAtUtc: now));
                            }
                        }

                        skusUpdated++;
                        break;
                    }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // ---- Specifications ----
        var specificationsInserted = 0;
        var specificationsUpdated = 0;
        var skuRowsByKey = skuRows.ToDictionary(row => row.ImportKey, StringComparer.Ordinal);

        var categoryIds = specificationRows
            .Select(row => ResolveSkuCategoryId(row, skuRowsByKey, skuContexts))
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();
        var semanticKeys = specificationRows.Select(row => row.Payload.SemanticKey).Where(key => key is not null).Distinct().ToArray();
        var definitions = await _dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(definition => definition.IsActive && categoryIds.Contains(definition.CategoryId) && semanticKeys.Contains(definition.SemanticKey))
            .ToListAsync(cancellationToken);
        var definitionsByKey = definitions.ToDictionary(definition => (definition.CategoryId, definition.SemanticKey));
        var optionCodes = specificationRows.Select(row => row.Payload.OptionCode).Where(code => code is not null).Distinct().ToArray();
        var definitionIds = definitions.Select(definition => definition.Id).ToArray();
        var optionIds = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(option => option.IsActive && definitionIds.Contains(option.SpecificationDefinitionId) && optionCodes.Contains(option.Code))
            .ToDictionaryAsync(option => (option.SpecificationDefinitionId, option.Code), option => option.Id, cancellationToken);

        var existingSkuIdsForSpecs = skuContexts.Values
            .Where(context => context.ExistingSkuId.HasValue)
            .Select(context => context.ExistingSkuId!.Value).Distinct().ToArray();
        var trackedValues = await _dbContext.SkuSpecificationValues
            .Where(value => existingSkuIdsForSpecs.Contains(value.SkuId))
            .ToListAsync(cancellationToken);

        var productIdsToTouch = new HashSet<long>();

        foreach (var row in specificationRows)
        {
            if (row.Action is not (ImportRowAction.Insert or ImportRowAction.Update))
            {
                continue;
            }

            var payload = row.Payload;
            var skuRow = skuRowsByKey[payload.SkuKey];
            var skuContext = skuContexts[skuRow.ImportKey];
            var skuId = skuContext.ExistingSkuId ?? createdSkusByKey[skuRow.ImportKey].Id;
            var categoryId = ResolveSkuCategoryId(row, skuRowsByKey, skuContexts)!.Value;
            var definition = definitionsByKey[(categoryId, payload.SemanticKey!)];

            long? optionId = null;
            if (definition.ValueType == SpecificationValueType.Option)
            {
                optionId = optionIds[(definition.Id, payload.OptionCode!)];
            }

            if (row.Action == ImportRowAction.Update)
            {
                // SkuSpecificationValue is immutable (no update method) — replace the row, the same
                // shape ReplaceSpecificationsAsync uses. Import rows are per-(sku, key) upserts;
                // values the file does not mention are deliberately left untouched.
                var existing = trackedValues.Single(value => value.SkuId == skuId && value.SpecificationDefinitionId == definition.Id);
                if (row.PreimageRowVersion is not null)
                {
                    _dbContext.Entry(existing).Property(candidate => candidate.RowVersion).OriginalValue = row.PreimageRowVersion;
                }

                _dbContext.SkuSpecificationValues.Remove(existing);
                specificationsUpdated++;
            }
            else
            {
                specificationsInserted++;
            }

            _dbContext.SkuSpecificationValues.Add(new SkuSpecificationValue(
                skuId,
                definition.Id,
                payload.StringValue,
                payload.DecimalValue,
                payload.BooleanValue,
                optionId,
                specificationSourceId: null,
                now));

            productIdsToTouch.Add(skuProductIds.TryGetValue(skuRow.ImportKey, out var mappedProductId)
                ? mappedProductId
                : skuContext.ProductId!.Value);
        }

        if (productIdsToTouch.Count > 0)
        {
            // Same rationale as EfSkuAdminService: a spec write must advance the owning product's
            // RowVersion so a racing category switch can't validate against stale spec values
            // (組長 PR #24 round 5, item 2).
            var createdIds = createdProductsByKey.Values.Select(product => product.Id).ToHashSet();
            var idsNeedingLoad = productIdsToTouch.Where(id => !createdIds.Contains(id) && !trackedProducts.ContainsKey(id)).ToArray();
            var loaded = await _dbContext.Products.Where(product => idsNeedingLoad.Contains(product.Id)).ToListAsync(cancellationToken);
            foreach (var product in loaded)
            {
                product.Touch(now);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ConfirmSummary(productsInserted, productsUpdated, skusInserted, skusUpdated, specificationsInserted, specificationsUpdated);
    }

    private async Task<AuditActor> ResolveActorAsync(string adminUserId, CancellationToken cancellationToken)
    {
        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.CatalogManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw DomainProblemException.Forbidden("The administrator no longer has permission to run catalog imports.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    private async Task MarkFailedBestEffortAsync(long batchId, CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.ChangeTracker.Clear();
            var batch = await _dbContext.ImportBatches
                .FirstOrDefaultAsync(candidate => candidate.Id == batchId, cancellationToken);
            if (batch is not null && batch.Status != ImportBatchStatus.Committed)
            {
                batch.ChangeStatus(ImportBatchStatus.Failed, DateTime.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            // Best effort only: the original apply failure is the error worth surfacing, and a
            // batch left Ready (rather than Failed) can still be retried or re-previewed safely.
        }
    }

    private sealed record RowCursorPayload(ImportDataset Dataset, int SourceRowNumber);

    /// <summary>ImportRow.NormalizedPayloadJson / RawJson 的長度上限（與 Domain 建構子一致）。</summary>
    private const int MaxRowJsonLength = 32 * 1024;

    /// <summary>
    /// A row whose payload does not fit: stored without any of the offending content, but still a
    /// real row carrying its error code (組長 PR #74 round-3, item 4). round-4 (P3)：信封仍保留
    /// 原始 business key（本身有 64 字元上限），否則同時是 duplicate 的超長列在錯誤 CSV 只剩合成
    /// 鍵可顯示，與「顯示管理員原始鍵」的契約不符。
    /// </summary>
    private static string BuildOversizedPayloadJson(string? originalKey) =>
        JsonSerializer.Serialize(new OversizedRowEnvelope(null, null, originalKey));

    /// <summary>The minimal envelope stored for an oversized row — same shape as
    /// <see cref="RowEnvelope{TPayload}"/> plus the preserved key.</summary>
    private sealed record OversizedRowEnvelope(object? Payload, byte[]? PreimageRowVersion, string? OriginalKey);

    /// <summary>What ImportRow.NormalizedPayloadJson actually stores: the normalized payload plus,
    /// for Update／NoChange rows, the referenced entity's Preview-time RowVersion (組長 PR #74
    /// review item 2). GetRowsAsync unwraps the envelope so API consumers still see the payload.</summary>
    private sealed record RowEnvelope<TPayload>(TPayload Payload, byte[]? PreimageRowVersion);

    private sealed record ExistingProductSnapshot(
        long Id, long BrandId, long CategoryId, string NameZhTw,
        string? DescriptionZhTw, int? WarrantyMonths, ProductStatus Status, byte[] RowVersion);

    private sealed record ProductRowContext(long? ExistingProductId, long? CategoryId);

    private sealed record ExistingSkuSnapshot(
        long Id, long ProductId, string NameZhTw, decimal ListPrice, decimal UnitCost,
        decimal? WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm,
        bool RequiresPrepayment, SkuStatus Status, byte[] RowVersion);

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
                p.Id, p.BrandId, p.CategoryId, p.NameZhTw, p.DescriptionZhTw, p.WarrantyMonths, p.Status, p.RowVersion))
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
                    // ??= — at Preview this is the first assignment; at Confirm the same resolver
                    // re-runs inside the transaction and must NOT stomp the Preview-time preimage
                    // with the current value, or the write-time concurrency check would trivially
                    // pass and overwrite interim edits (組長 PR #74 review item 2).
                    row.PreimageRowVersion ??= existing.RowVersion;
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
                s.WeightKg, s.LengthCm, s.WidthCm, s.HeightCm, s.RequiresPrepayment, s.Status, s.RowVersion))
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
                    row.PreimageRowVersion ??= existingSku.RowVersion;
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

            // Two definition shapes the single-value template cannot express — reject at Preview so
            // the admin learns it from the errors CSV, and again on the shared re-validation at
            // Confirm. AllowsMultiple definitions store SkuSpecificationOptionSelections, not a
            // single value row (EfSkuAdminService.ReplaceSpecificationsAsync); hard-rule
            // compatibility keys require a reviewed SpecificationSource the template has no column
            // for (same rule EfSkuAdminService.ResolveSpecificationSourceIdAsync enforces).
            if (definition.AllowsMultiple ||
                CompatibilityCatalogContract.HardRuleSemanticKeys.Contains(definition.SemanticKey))
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
                    row.PreimageRowVersion ??= existingValue.RowVersion;
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

            var normalizedPayloadJson = JsonSerializer.Serialize(
                new RowEnvelope<TPayload>(row.Payload, row.PreimageRowVersion));
            var rawJson = JsonSerializer.Serialize(row.RawFields);
            var errorCodes = row.Errors.Count > 0 ? string.Join(",", row.Errors.Distinct()) : null;

            // 組長 PR #74 round-3, item 4：ImportRow 的兩個 JSON 欄位各有 32 KB 上限，超過就從
            // 建構子丟 ArgumentOutOfRangeException——一個 40 KB 的無效欄位讓整批直接 500，管理員
            // 連錯誤檔都拿不到。改為建立實體「之前」量測序列化後的大小：超限的列不保存巨量內容
            // （RawJson 直接省略，payload 換成不含資料的最小信封），但仍然是一列帶錯誤碼的資料，
            // 批次照常成為 Invalid 讓管理員修檔重傳。
            if (normalizedPayloadJson.Length > MaxRowJsonLength)
            {
                normalizedPayloadJson = BuildOversizedPayloadJson(row.OriginalKey);
                if (errorCodes is null)
                {
                    errorCodes = DomainErrorCodes.ImportValidationFailed;
                    errorCount++;
                    switch (action)
                    {
                        case ImportRowAction.Insert: newCount--; break;
                        case ImportRowAction.Update: updatedCount--; break;
                        case ImportRowAction.NoChange: unchangedCount--; break;
                        default: errorCount--; break;
                    }

                    action = ImportRowAction.Error;
                }
            }

            if (rawJson.Length > MaxRowJsonLength)
            {
                rawJson = null;
            }

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

    private static string UnwrapPayloadJson(string normalizedPayloadJson)
    {
        using var document = JsonDocument.Parse(normalizedPayloadJson);
        return document.RootElement.TryGetProperty("Payload", out var payload)
            ? payload.GetRawText()
            : normalizedPayloadJson;
    }

    /// <summary>2601/2627: unique index / unique constraint violation.</summary>
    /// <summary>
    /// The business key the admin wrote in their own file. Normally identical to ImportRow.ImportKey;
    /// it differs only for rows stored under a synthetic duplicate key, and for those the original
    /// still lives in the stored payload (組長 PR #74 round-3, item 1). Falls back to the storage
    /// key when the payload was dropped for being oversized (item 4).
    /// </summary>
    private static string OriginalKeyOf(ImportRow row)
    {
        try
        {
            using var document = JsonDocument.Parse(row.NormalizedPayloadJson);

            // An oversized row kept only its key (round-4, P3).
            if (ReadString(document.RootElement, "OriginalKey") is { } preservedKey)
            {
                return preservedKey;
            }

            if (!document.RootElement.TryGetProperty("Payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                return row.ImportKey;
            }

            return row.Dataset switch
            {
                ImportDataset.Products => ReadString(payload, "ProductKey") ?? row.ImportKey,
                ImportDataset.Skus => ReadString(payload, "SkuKey") ?? row.ImportKey,
                ImportDataset.Specifications =>
                    ReadString(payload, "SkuKey") is { } skuKey
                        ? $"{skuKey}/{ReadString(payload, "SemanticKey") ?? string.Empty}"
                        : row.ImportKey,
                _ => row.ImportKey,
            };
        }
        catch (JsonException)
        {
            return row.ImportKey;
        }
    }

    private static string? ReadString(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsUniqueKeyViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqlException { Number: 2601 or 2627 };

    /// <summary>1205 anywhere in the chain: this transaction was chosen as a deadlock victim —
    /// it can surface from a resolver query directly or wrapped by SaveChanges.</summary>
    private static bool IsDeadlockVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
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
