using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Imports;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Imports;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// UC-ADM-INV-01 匯入（匯入暫存與庫存調整設計.md「庫存匯入確認」）。
///
/// 批次那一層完全沿用 <see cref="ImportBatchStaging"/>，這裡只處理庫存自己的規則：依目前 Balance
/// 算 Delta、Confirm 時檢查 Balance 是否在 Preview 之後被動過、套用時不得造成負 OnHand、負
/// Reserved、Reserved 大於 OnHand，或覆蓋 Active Reservation。
/// </summary>
public sealed class EfInventoryImportService : IInventoryImportService
{
    private const int MaxTotalRows = 5_000;
    private const int CurrentTemplateVersion = 1;

    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;

    public EfInventoryImportService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
    }

    public async Task<InventoryImportBatchDto> PreviewAsync(
        PreviewInventoryImportRequest request,
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
                $"Template version '{request.TemplateVersion}' is not supported. The current inventory-import template is version {CurrentTemplateVersion}.");
        }

        var now = DateTime.UtcNow;
        await ImportBatchStaging.ExpireStaleBatchesAsync(
            _dbContext, createdByAdminUserId, ImportType.InventoryAdjustment, now, cancellationToken);

        var bytes = await ImportBatchStaging.ReadFileAsync(
            request.AdjustmentsFile, "InventoryAdjustments", "an inventory import", cancellationToken);
        var rows = ImportBatchStaging.ParseCsv(
            bytes, InventoryAdjustmentRowParser.Parse, "InventoryAdjustments");

        if (rows.Count == 0)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportDatasetMissing,
                "The upload contains no data rows.");
        }

        if (rows.Count > MaxTotalRows)
        {
            throw DomainProblemException.Validation(
                $"An inventory import batch may contain at most {MaxTotalRows} rows; this upload has {rows.Count}.");
        }

        await ResolveRowsAsync(rows, cancellationToken);

        var batch = new ImportBatch(
            Guid.CreateVersion7(),
            ImportType.InventoryAdjustment,
            request.TemplateVersion,
            createdByAdminUserId,
            now.AddHours(24),
            Guid.CreateVersion7(),
            now);

        // 庫存只有第 1 組來源；第 2／3 組 Hash 與檔名為 Null（匯入暫存與庫存調整設計.md）。
        batch.SetSources(
            SHA256.HashData(bytes), request.AdjustmentsFile.OriginalFileName,
            null, null,
            null, null,
            now);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.ImportBatches.Add(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var counts = ImportBatchStaging.AddRows(
                _dbContext, batch.Id, ImportDataset.InventoryAdjustments, rows, default, now);
            batch.SetPreviewStatistics(
                rows.Count, counts.New, counts.Updated, counts.Unchanged, counts.Errors,
                normalizedContentVersion: 1, now);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOneInProgressBatchConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ImportBatchInProgress,
                "You already have an in-progress inventory import batch. Finish, expire, or otherwise close it before starting another.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return ToDto(batch);
    }

    /// <summary>
    /// 對照目前的 SKU 與 Balance，決定每一列的動作並記下 Preview 當時的 Balance RowVersion。
    ///
    /// 庫存匯入沒有「新增」——每一列都是對既有 SKU 的調整，所以動作只會是 Update（目標與現況不同）
    /// 或 NoChange（相同）。找不到 SKU 是列級錯誤。
    /// </summary>
    private async Task ResolveRowsAsync(
        IReadOnlyList<StagedImportRow<InventoryAdjustmentPayload>> rows,
        CancellationToken cancellationToken)
    {
        var codes = rows
            .Where(row => row.Errors.Count == 0 && !string.IsNullOrEmpty(row.Payload.SkuCode))
            .Select(row => row.Payload.SkuCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (codes.Length == 0)
        {
            return;
        }

        // SQL Server 的預設定序不分大小寫與全形半形，所以查詢照原樣送出去，比對結果再用同一套
        // 正規化形式對回來——應用層若用 Ordinal 比，會把資料庫認定相同的兩個代碼當成不同。
        var skus = await _dbContext.Skus.AsNoTracking()
            .Where(sku => codes.Contains(sku.SkuCode))
            .Select(sku => new { sku.Id, sku.SkuCode })
            .ToListAsync(cancellationToken);
        var skuIdsByCanonicalCode = skus
            .GroupBy(sku => ImportStorageKeyAllocator.Canonicalize(sku.SkuCode), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

        var skuIds = skuIdsByCanonicalCode.Values.ToArray();
        var balancesBySkuId = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

        foreach (var row in rows)
        {
            if (row.Errors.Count > 0)
            {
                continue;
            }

            var canonical = ImportStorageKeyAllocator.Canonicalize(row.Payload.SkuCode);
            if (!skuIdsByCanonicalCode.TryGetValue(canonical, out var skuId))
            {
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
                continue;
            }

            if (!balancesBySkuId.TryGetValue(skuId, out var balance))
            {
                // 有 SKU 卻沒有 Balance 列：這批不負責建立庫存列，那是入庫流程的事。
                row.AddError(DomainErrorCodes.ImportLookupNotFound);
                continue;
            }

            var target = row.Payload.TargetOnHand!.Value;

            // Preview 當時的 Before／Reserved 存進暫存列：預覽畫面要顯示 Before／Delta／After，而這些
            // 數字只在此刻成立。錯誤列（低於已保留）也記，管理員才看得出為什麼被擋。
            row.Payload.BeforeOnHand = balance.OnHandQuantity;
            row.Payload.ReservedQuantity = balance.ReservedQuantity;

            // 「不允許造成負 Reserved、Reserved 大於 OnHand，或覆蓋 Active Reservation」——把 OnHand
            // 調到低於已保留的數量，等於把別人已經下單佔住的貨憑空變不見。這是列級錯誤，在 Preview
            // 就要讓管理員看到，而不是等 Confirm 整批炸掉。
            if (target < balance.ReservedQuantity)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
                continue;
            }

            row.Action = target == balance.OnHandQuantity
                ? ImportRowAction.NoChange
                : ImportRowAction.Update;

            // Confirm 會拿這個 RowVersion 當樂觀鎖的原始值：盤點差異是對著某一個時點的庫存算出來
            // 的，底下的數字換了那份差異就不再成立（「Commit 檢查 Preview 時的 Balance RowVersion；
            // 任一 SKU 已變動時整批拒絕並要求重新 Preview」）。
            row.PreimageRowVersion = balance.RowVersion;
        }
    }

    public async Task<InventoryImportBatchDto?> GetAsync(Guid batchPublicId, CancellationToken cancellationToken)
    {
        var batch = await FindBatchAsync(batchPublicId, tracking: false, cancellationToken);
        return batch is null ? null : ToDto(batch);
    }

    public async Task<CursorPage<InventoryImportRowDto>> GetRowsAsync(
        Guid batchPublicId,
        ImportRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.PageSize is < 1 or > 200)
        {
            throw DomainProblemException.Validation(
                $"pageSize must be between 1 and 200; got {query.PageSize}.");
        }

        var batch = await FindBatchAsync(batchPublicId, tracking: false, cancellationToken)
            ?? throw DomainProblemException.NotFound("The import batch was not found.");

        return await ImportBatchStaging.GetRowsAsync(
            _dbContext,
            batch.Id,
            batchPublicId,
            query,
            [ImportDataset.InventoryAdjustments],
            ToRowDto,
            cancellationToken);
    }

    /// <summary>
    /// 暫存列 → 明確型別的預覽列。超過 32 KB 而只剩最小信封的列沒有 payload，那就只給得出鍵與錯誤碼。
    /// </summary>
    private static InventoryImportRowDto ToRowDto(ImportRow row)
    {
        var payload = Deserialize(row)?.Payload;
        var target = payload?.TargetOnHand;
        var before = payload?.BeforeOnHand;
        return new InventoryImportRowDto(
            row.SourceRowNumber,
            payload?.SkuCode is { Length: > 0 } code ? code : ImportBatchStaging.OriginalKeyOf(row),
            row.Action.ToString(),
            ImportBatchStaging.SplitErrorCodes(row.ErrorCodes),
            before,
            payload?.ReservedQuantity,
            target,
            before is not null && target is not null ? target - before : null,
            payload?.ReasonCode,
            payload?.Note);
    }

    public async Task<byte[]?> GetErrorsCsvAsync(Guid batchPublicId, CancellationToken cancellationToken)
    {
        var batch = await FindBatchAsync(batchPublicId, tracking: false, cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var errorRows = await _dbContext.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId == batch.Id && row.ErrorCodes != null)
            .OrderBy(row => row.SourceRowNumber)
            .ToListAsync(cancellationToken);

        return ImportBatchStaging.BuildErrorsCsv(errorRows);
    }

    public async Task<InventoryImportBatchDto> ConfirmAsync(
        Guid batchPublicId,
        string adminUserId,
        byte[] rowVersion,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        ArgumentNullException.ThrowIfNull(auditContext);

        var batch = await FindBatchAsync(batchPublicId, tracking: true, cancellationToken)
            ?? throw DomainProblemException.NotFound("The import batch was not found.");

        if (batch.Status == ImportBatchStatus.Committed)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ImportAlreadyCommitted,
                "This inventory import batch has already been committed.");
        }

        var now = DateTime.UtcNow;
        if (batch.ExpiresAtUtc <= now)
        {
            batch.ChangeStatus(ImportBatchStatus.Expired, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw DomainProblemException.Gone(
                DomainErrorCodes.ImportBatchExpired,
                "This inventory import batch expired; upload the file again.");
        }

        if (batch.Status != ImportBatchStatus.Ready)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.InventoryImportValidationFailed,
                $"An inventory import batch can only be committed from Ready; this one is {batch.Status}.");
        }

        _dbContext.Entry(batch).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);
        var storedRows = await _dbContext.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId == batch.Id)
            .OrderBy(row => row.SourceRowNumber)
            .ToListAsync(cancellationToken);

        // Preview 已經把錯誤列標出來了；Ready 的批次不該還有錯誤列，但真的有就整批拒絕而不是
        // 「跳過那幾列」——部分成功正是這份規格明令禁止的。
        if (storedRows.Any(row => row.ErrorCodes != null))
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.InventoryImportValidationFailed,
                "This batch still has rows with errors. Fix the file and upload it again.");
        }

        var adjustments = storedRows
            .Select(row => (Row: row, Envelope: Deserialize(row)))
            .ToArray();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var applied = await ApplyAsync(batch, adjustments, adminUserId, now, cancellationToken);

            // Complete 一次做完狀態、摘要與 ConfirmedAtUtc——與商品匯入走同一個領域方法。
            batch.Complete(JsonSerializer.Serialize(new { appliedRowCount = applied }), now);

            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.InventoryImportConfirm,
                AuditResourceTypes.ImportBatch,
                batch.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("status", ImportBatchStatus.Ready.ToString(), ImportBatchStatus.Committed.ToString()),
                    AuditFieldChange.Changed("inventoryBalances"),
                ],
                reason: "inventory_import_confirm",
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
            _dbContext.ChangeTracker.Clear();

            // Balance 或 Batch 的 RowVersion 對不上：盤點結果是對著 Preview 當時的庫存算的，
            // 底下的數字已經被別人改過，這份差異就不再成立。整批拒絕並要求重新 Preview。
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The stock levels changed after this preview was taken. Upload the file again to re-preview against the current stock.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return ToDto(batch);
    }

    /// <summary>
    /// 套用整批調整。每一列產生一筆 Adjustment InventoryMovement 並更新 Balance，全部在呼叫端的
    /// 同一個交易內——任一列失敗整批回滾。
    /// </summary>
    private async Task<int> ApplyAsync(
        ImportBatch batch,
        IReadOnlyList<(ImportRow Row, ImportBatchStaging.RowEnvelope<InventoryAdjustmentPayload>? Envelope)> adjustments,
        string adminUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var codes = adjustments
            .Where(entry => entry.Envelope is not null)
            .Select(entry => entry.Envelope!.Payload.SkuCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var skus = await _dbContext.Skus.AsNoTracking()
            .Where(sku => codes.Contains(sku.SkuCode))
            .Select(sku => new { sku.Id, sku.SkuCode, sku.UnitCost })
            .ToListAsync(cancellationToken);
        var skusByCanonicalCode = skus.ToDictionary(
            sku => ImportStorageKeyAllocator.Canonicalize(sku.SkuCode), StringComparer.Ordinal);

        var skuIds = skus.Select(sku => sku.Id).ToArray();
        var balances = await _dbContext.InventoryBalances
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

        var applied = 0;
        foreach (var (row, envelope) in adjustments)
        {
            if (envelope is null)
            {
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.InventoryImportValidationFailed,
                    $"Row {row.SourceRowNumber} could not be read back from the preview. Upload the file again.");
            }

            var payload = envelope.Payload;
            var canonical = ImportStorageKeyAllocator.Canonicalize(payload.SkuCode);
            if (!skusByCanonicalCode.TryGetValue(canonical, out var sku) ||
                !balances.TryGetValue(sku.Id, out var balance))
            {
                // Preview 之後 SKU 或 Balance 被刪掉了。
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.InventoryImportValidationFailed,
                    $"SKU '{payload.SkuCode}' no longer has a stock record. Upload the file again to re-preview.");
            }

            // 這一行就是「Commit 檢查 Preview 時的 Balance RowVersion」：對不上的話 SaveChanges
            // 會丟 DbUpdateConcurrencyException，整批回滾。
            _dbContext.Entry(balance).Property(candidate => candidate.RowVersion).OriginalValue =
                envelope.PreimageRowVersion ?? balance.RowVersion;

            var target = payload.TargetOnHand!.Value;
            var beforeOnHand = balance.OnHandQuantity;
            var beforeReserved = balance.ReservedQuantity;

            // 目標低於已保留數量會讓 Reserved 大於 OnHand。Preview 已經擋過一次，但 Reserved 在
            // Preview 之後可能又變多了，所以套用前再擋一次——這是最後一道，過了就寫進資料庫了。
            if (target < beforeReserved)
            {
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.InventoryImportValidationFailed,
                    $"SKU '{payload.SkuCode}' now has {beforeReserved} reserved, more than the target on-hand of {target}. Upload the file again to re-preview.");
            }

            // 組長 PR #89 item 1：NoChange 列也要真的寫 Balance。只設 OriginalValue 不會產生 UPDATE，
            // SQL 端的 RowVersion 條件根本不會送出去——Preview 之後被別人改到剛好等於目標值的 SKU 就
            // 靜靜通過了。ApplyQuantities 會更新 UpdatedAtUtc，EF 才會送出帶 RowVersion 條件的 UPDATE。
            // 同一個理由，每一列（含 Delta 為 0 的）都留一筆 Adjustment Movement：「所有列都保存
            // Before、Delta、After、Reason、Actor、Batch PublicId 及時間」。
            balance.ApplyQuantities(target, beforeReserved, now);

            _dbContext.InventoryMovements.Add(new InventoryMovement(
                Guid.CreateVersion7(),
                sku.Id,
                reservationId: null,
                InventoryMovementTypes.Adjustment,
                onHandDelta: target - beforeOnHand,
                reservedDelta: 0,
                beforeOnHand: beforeOnHand,
                afterOnHand: target,
                beforeReserved: beforeReserved,
                afterReserved: beforeReserved,
                unitCostSnapshot: sku.UnitCost,
                reasonCode: payload.ReasonCode!,
                referenceType: "ImportBatch",
                referencePublicId: batch.PublicId,
                actorUserId: adminUserId,
                occurredAtUtc: now,
                adjustmentNote: payload.Note));
            applied++;
        }

        return applied;
    }

    private static ImportBatchStaging.RowEnvelope<InventoryAdjustmentPayload>? Deserialize(ImportRow row)
    {
        try
        {
            return JsonSerializer.Deserialize<ImportBatchStaging.RowEnvelope<InventoryAdjustmentPayload>>(
                row.NormalizedPayloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ImportBatch?> FindBatchAsync(
        Guid batchPublicId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking ? _dbContext.ImportBatches : _dbContext.ImportBatches.AsNoTracking();
        return await query.FirstOrDefaultAsync(
            candidate => candidate.PublicId == batchPublicId &&
                candidate.ImportType == ImportType.InventoryAdjustment,
            cancellationToken);
    }

    /// <summary>Same shape as EfProductImportService.ResolveActorAsync.</summary>
    private async Task<AuditActor> ResolveActorAsync(string adminUserId, CancellationToken cancellationToken)
    {
        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.Validation("The administrator identity is invalid.");

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.InventoryManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw DomainProblemException.Validation(
                "The administrator is not allowed to commit inventory imports.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    /// <summary>與 EfProductImportService 相同：靠唯一索引的名稱辨識，不猜錯誤號碼。</summary>
    private static bool IsOneInProgressBatchConflict(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains(
            "UX_ImportBatches_CreatedByAdminUserId_ImportType", StringComparison.Ordinal) == true;

    private static InventoryImportBatchDto ToDto(ImportBatch batch) => new(
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
