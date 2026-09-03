using DoSelect.Application.Returns;
using DoSelect.Domain.Returns;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Returns;

public sealed class ReturnStore : IReturnStore
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const string ReturnNumberUniqueIndexName = "UX_ReturnRequests_ReturnNumber";

    /// <summary>SQL Server's deadlock-victim error number.</summary>
    private const int DeadlockVictimErrorNumber = 1205;

    /// <summary>
    /// Attempts for the whole locked create transaction. Ascending-Id lock ordering (see
    /// <see cref="CreateWithItemsOnceAsync"/>) makes a deadlock between two of *this* code path's
    /// own lock acquisitions essentially impossible, but SQL Server can still pick either side as
    /// a victim under contention from elsewhere — the entire attempt (lock, re-sum, insert) must
    /// rerun on a deadlock, not just the failed statement, since the sum it read is now stale.
    /// </summary>
    private const int MaximumLockRetryAttempts = 3;

    private readonly DoSelectDbContext _dbContext;

    public ReturnStore(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ReturnNumberExistsAsync(string returnNumber, CancellationToken cancellationToken) =>
        _dbContext.ReturnRequests.AnyAsync(r => r.ReturnNumber == returnNumber, cancellationToken);

    public async Task<ReturnCreationResult> CreateWithItemsAsync(
        ReturnRequest request,
        IReadOnlyList<ReturnItemQuantityBudget> quantityBudgets,
        Func<long, IReadOnlyList<ReturnItem>> itemsFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumLockRetryAttempts; attempt++)
        {
            try
            {
                return await CreateWithItemsOnceAsync(request, quantityBudgets, itemsFactory, cancellationToken);
            }
            catch (Exception ex) when (attempt < MaximumLockRetryAttempts && IsDeadlockVictim(ex))
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new ReturnsWriteException(
            ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
            "Unable to create the return due to repeated concurrency conflicts. Please try again.");
    }

    private async Task<ReturnCreationResult> CreateWithItemsOnceAsync(
        ReturnRequest request,
        IReadOnlyList<ReturnItemQuantityBudget> quantityBudgets,
        Func<long, IReadOnlyList<ReturnItem>> itemsFactory,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Closes the check-then-insert race: SumActiveRequestedQuantityAsync used to run
            // outside any transaction, so two concurrent creates for the same OrderItem could
            // both read "0 already requested" and both pass. Locking each distinct OrderItem's
            // own PK row (not a Serializable range-scan over ReturnItems, which has no
            // OrderItemId-leading index and would range-lock unrelated items too — see the
            // implementation report) means only creates that actually target the SAME
            // OrderItemId ever block each other; different items proceed independently.
            foreach (var orderItemId in quantityBudgets.Select(b => b.OrderItemId).Distinct().OrderBy(id => id))
            {
                await _dbContext.Database.SqlQuery<int>(
                    $"SELECT TOP (1) 1 AS Value FROM dbo.OrderItems WITH (UPDLOCK, HOLDLOCK) WHERE Id = {orderItemId}")
                    .ToListAsync(cancellationToken);
            }

            foreach (var budget in quantityBudgets)
            {
                var alreadyRequested = await SumActiveRequestedQuantityAsync(budget.OrderItemId, cancellationToken);
                var remaining = budget.MaximumReturnableQuantity - alreadyRequested;
                if (budget.RequestedQuantity > remaining)
                {
                    throw new ReturnQuantityConflictException(budget.OrderItemId);
                }
            }

            await _dbContext.ReturnRequests.AddAsync(request, cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsIndexViolation(ex, ReturnNumberUniqueIndexName))
            {
                _dbContext.Entry(request).State = EntityState.Detached;
                throw new ReturnNumberCollisionException(request.ReturnNumber, ex);
            }

            var items = itemsFactory(request.Id);
            await _dbContext.ReturnItems.AddRangeAsync(items, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new ReturnCreationResult(request.Id, request, items);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsDeadlockVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictimErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    public Task<ReturnRequest?> FindOwnedAsync(
        Guid returnPublicId, string? memberUserId, long? guestOrderId, CancellationToken cancellationToken)
    {
        var query = _dbContext.ReturnRequests.Where(r => r.PublicId == returnPublicId);
        query = memberUserId is not null
            ? query.Where(r => r.RequesterUserId == memberUserId)
            : query.Join(
                _dbContext.Orders.Where(o => o.Id == guestOrderId),
                r => r.OrderId,
                o => o.Id,
                (r, _) => r);

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public Task<ReturnRequest?> FindByPublicIdAsync(Guid returnPublicId, CancellationToken cancellationToken) =>
        _dbContext.ReturnRequests.SingleOrDefaultAsync(r => r.PublicId == returnPublicId, cancellationToken);

    public async Task<IReadOnlyList<ReturnItem>> ListItemsAsync(long returnRequestId, CancellationToken cancellationToken) =>
        await _dbContext.ReturnItems
            .Where(i => i.ReturnRequestId == returnRequestId)
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnItemDto>> ListItemSummariesAsync(long returnRequestId, CancellationToken cancellationToken) =>
        await _dbContext.ReturnItems
            .Where(i => i.ReturnRequestId == returnRequestId)
            .Join(
                _dbContext.OrderItems,
                i => i.OrderItemId,
                oi => oi.Id,
                (i, oi) => new { Item = i, OrderItem = oi })
            .OrderBy(pair => pair.Item.PublicId)
            .Select(pair => new ReturnItemDto(
                pair.Item.PublicId,
                pair.OrderItem.PublicId,
                pair.OrderItem.SkuCodeSnapshot,
                pair.OrderItem.ProductNameSnapshot,
                pair.Item.Description,
                pair.Item.Quantity,
                pair.Item.InspectionStatus,
                pair.Item.RestockDisposition))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnAttachmentDto>> ListCleanAttachmentSummariesAsync(
        long returnRequestId, CancellationToken cancellationToken) =>
        await _dbContext.ReturnAttachments
            .Where(a => a.ReturnRequestId == returnRequestId && a.DeletedAtUtc == null && a.ScanStatus == DoSelect.Domain.Support.PrivateAttachmentScanStatus.Clean)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new ReturnAttachmentDto(a.PublicId, a.OriginalFileName, a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveAttachmentsAsync(long returnRequestId, CancellationToken cancellationToken) =>
        _dbContext.ReturnAttachments.CountAsync(
            a => a.ReturnRequestId == returnRequestId && a.DeletedAtUtc == null,
            cancellationToken);

    public async Task<bool> TryAddAttachmentAsync(
        ReturnAttachment attachment, int maxActiveAttachments, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumLockRetryAttempts; attempt++)
        {
            try
            {
                return await TryAddAttachmentOnceAsync(attachment, maxActiveAttachments, cancellationToken);
            }
            catch (Exception ex) when (attempt < MaximumLockRetryAttempts && IsDeadlockVictim(ex))
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new ReturnsWriteException(
            ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
            "Unable to add the attachment due to repeated concurrency conflicts. Please try again.");
    }

    private async Task<bool> TryAddAttachmentOnceAsync(
        ReturnAttachment attachment, int maxActiveAttachments, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Same shape as CreateWithItemsOnceAsync's OrderItem lock: lock the parent row so
            // only uploads targeting the SAME return case ever block each other, then re-count
            // under that lock immediately before inserting.
            await _dbContext.Database.SqlQuery<int>(
                $"SELECT TOP (1) 1 AS Value FROM dbo.ReturnRequests WITH (UPDLOCK, HOLDLOCK) WHERE Id = {attachment.ReturnRequestId}")
                .ToListAsync(cancellationToken);

            var activeCount = await _dbContext.ReturnAttachments.CountAsync(
                a => a.ReturnRequestId == attachment.ReturnRequestId && a.DeletedAtUtc == null,
                cancellationToken);

            if (activeCount >= maxActiveAttachments)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await _dbContext.ReturnAttachments.AddAsync(attachment, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<ReturnAttachmentAccess?> FindAttachmentAccessAsync(Guid attachmentPublicId, CancellationToken cancellationToken) =>
        _dbContext.ReturnAttachments
            .Where(a => a.PublicId == attachmentPublicId && a.DeletedAtUtc == null &&
                        a.ScanStatus == DoSelect.Domain.Support.PrivateAttachmentScanStatus.Clean)
            .Join(
                _dbContext.ReturnRequests,
                a => a.ReturnRequestId,
                r => r.Id,
                (a, r) => new ReturnAttachmentAccess(
                    r.Id, r.RequesterUserId, r.OrderId, a.StorageKey, a.OriginalFileName, a.MimeType))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> SumActiveRequestedQuantityAsync(long orderItemId, CancellationToken cancellationToken) =>
        await _dbContext.ReturnItems
            .Where(i => i.OrderItemId == orderItemId)
            .Join(
                _dbContext.ReturnRequests.Where(r =>
                    r.Status != ReturnRequestStatus.Rejected && r.Status != ReturnRequestStatus.Cancelled),
                i => i.ReturnRequestId,
                r => r.Id,
                (i, r) => i.Quantity)
            .SumAsync(cancellationToken);

    public async Task<(IReadOnlyList<AdminReturnSummaryDto> Items, int TotalCount)> ListForAdminAsync(
        AdminReturnQuery query, CancellationToken cancellationToken)
    {
        var filtered = _dbContext.ReturnRequests.AsQueryable();
        if (query.Statuses is { Count: > 0 } statuses)
        {
            filtered = filtered.Where(r => statuses.Contains(r.Status));
        }

        if (query.ReasonCodes is { Count: > 0 } reasonCodes)
        {
            filtered = filtered.Where(r => reasonCodes.Contains(r.ReasonCode));
        }

        if (query.From is { } from)
        {
            filtered = filtered.Where(r => r.RequestedAtUtc >= from);
        }

        if (query.To is { } to)
        {
            filtered = filtered.Where(r => r.RequestedAtUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            filtered = filtered.Where(r => r.ReturnNumber.Contains(q));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var joined = filtered
            .Join(
                _dbContext.Orders,
                r => r.OrderId,
                o => o.Id,
                (r, o) => new { Return = r, o.PublicId, o.OrderNumber })
            .OrderByDescending(x => x.Return.UpdatedAtUtc)
            .ThenByDescending(x => x.Return.PublicId)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize);

        var rows = await joined
            .Select(x => new
            {
                x.Return.PublicId,
                x.Return.ReturnNumber,
                OrderPublicId = x.PublicId,
                x.OrderNumber,
                x.Return.Status,
                x.Return.Priority,
                x.Return.RequestedAtUtc,
                x.Return.ReturnShipmentDueAtUtc,
                x.Return.RowVersion,
                ItemCount = _dbContext.ReturnItems.Count(i => i.ReturnRequestId == x.Return.Id),
            })
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var items = rows
            .Select(row => new AdminReturnSummaryDto(
                row.PublicId,
                row.ReturnNumber,
                row.OrderPublicId,
                row.OrderNumber,
                row.Status,
                row.Priority,
                row.ItemCount,
                row.RequestedAtUtc,
                row.ReturnShipmentDueAtUtc,
                row.Status == ReturnRequestStatus.AwaitingShipment && row.ReturnShipmentDueAtUtc is { } due && due <= nowUtc.AddDays(2),
                row.RowVersion))
            .ToList();

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<ReturnHistoryEntryDto>> ListHistoryAsync(long returnRequestId, CancellationToken cancellationToken) =>
        await _dbContext.ReturnStatusHistories
            .Where(h => h.ReturnRequestId == returnRequestId)
            .OrderBy(h => h.OccurredAtUtc)
            .ThenBy(h => h.Id)
            .Select(h => new ReturnHistoryEntryDto(h.FromStatus, h.ToStatus, h.ReasonCode, h.Note, h.OccurredAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnInspectionDto>> ListInspectionsAsync(long returnRequestId, CancellationToken cancellationToken) =>
        await _dbContext.ReturnInspections
            .Join(
                _dbContext.ReturnItems.Where(i => i.ReturnRequestId == returnRequestId),
                insp => insp.ReturnItemId,
                i => i.Id,
                (insp, i) => new { Inspection = insp, ReturnItemPublicId = i.PublicId })
            .OrderBy(row => row.Inspection.InspectedAtUtc)
            .ThenBy(row => row.Inspection.Id)
            .Select(row => new ReturnInspectionDto(
                row.ReturnItemPublicId,
                row.Inspection.Result,
                row.Inspection.ConditionCode,
                row.Inspection.Note,
                row.Inspection.InspectedAtUtc))
            .ToListAsync(cancellationToken);

    public Task<ReturnShipment?> FindShipmentAsync(long returnRequestId, CancellationToken cancellationToken) =>
        _dbContext.ReturnShipments.SingleOrDefaultAsync(s => s.ReturnRequestId == returnRequestId, cancellationToken);

    public async Task<IReadOnlyList<ReturnShipmentEvent>> ListShipmentEventsAsync(long returnShipmentId, CancellationToken cancellationToken) =>
        await _dbContext.ReturnShipmentEvents
            .Where(e => e.ReturnShipmentId == returnShipmentId)
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);


    public async Task SaveTransitionAsync(
        ReturnRequest request,
        IReadOnlyList<ReturnItem>? itemsToUpdate,
        IReadOnlyList<ReturnInspection>? inspectionsToAdd,
        IReadOnlyList<ReturnStatusHistory> historiesToAdd,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (_dbContext.Entry(request).State == EntityState.Detached)
            {
                _dbContext.ReturnRequests.Attach(request);
            }

            _dbContext.Entry(request).State = EntityState.Modified;
            _dbContext.Entry(request).Property(r => r.RowVersion).OriginalValue = expectedRowVersion;

            if (itemsToUpdate is { Count: > 0 })
            {
                foreach (var item in itemsToUpdate)
                {
                    if (_dbContext.Entry(item).State == EntityState.Detached)
                    {
                        _dbContext.ReturnItems.Attach(item);
                    }

                    _dbContext.Entry(item).State = EntityState.Modified;
                }
            }

            if (inspectionsToAdd is { Count: > 0 })
            {
                await _dbContext.ReturnInspections.AddRangeAsync(inspectionsToAdd, cancellationToken);
            }

            if (historiesToAdd.Count > 0)
            {
                await _dbContext.ReturnStatusHistories.AddRangeAsync(historiesToAdd, cancellationToken);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
                    "The return was modified by another request. Reload and try again.");
            }
            catch (DbUpdateException ex) when (IsIndexViolation(ex, "UX_Refunds_IdempotencyKey"))
            {
                // 這個轉移把退貨推進 AwaitingRefund，退款由 IReturnRefundCreationPort 一起
                // 暫存。金鑰由退貨對外識別推導，因此兩個並行的核准會撞同一把 —— 撞到的
                // 那一邊整筆交易回滾（退貨狀態不會單獨落地），拿到的是狀態衝突而不是 500。
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                    "This return already has a refund awaiting review.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ReturnShipment> CreateShipmentAsync(
        ReturnShipment shipment, long returnRequestId, byte[] expectedReturnRowVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // No ReturnRequest field actually changes here — Modified (not Unchanged) is
            // required anyway so EF emits an UPDATE whose WHERE clause enforces the
            // RowVersion concurrency check; Unchanged entities are skipped by SaveChanges
            // entirely and the check would silently never fire.
            var request = await _dbContext.ReturnRequests.SingleAsync(r => r.Id == returnRequestId, cancellationToken);
            _dbContext.Entry(request).State = EntityState.Modified;
            _dbContext.Entry(request).Property(r => r.RowVersion).OriginalValue = expectedReturnRowVersion;

            await _dbContext.ReturnShipments.AddAsync(shipment, cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
                    "The return was modified by another request. Reload and try again.");
            }
            catch (DbUpdateException ex) when (
                IsIndexViolation(ex, "UX_ReturnShipments_ShipmentNumber") ||
                IsIndexViolation(ex, "UX_ReturnShipments_ReturnRequestId"))
            {
                throw new ReturnsWriteException(
                    ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                    "This return already has an active shipment.");
            }

            await transaction.CommitAsync(cancellationToken);
            return shipment;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AppendShipmentEventResult> AppendShipmentEventAsync(
        ReturnShipmentEvent shipmentEvent,
        Func<ReturnShipment, ReturnRequest, IReadOnlyList<ReturnStatusHistory>> applyToLatestState,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // The service already read these aggregates for routing/authorization. Clear those
            // tracked snapshots before taking the write lock so the query below materializes the
            // latest database values instead of returning EF's stale identity-map instance.
            _dbContext.ChangeTracker.Clear();

            var shipment = await _dbContext.ReturnShipments
                .FromSqlInterpolated($"SELECT * FROM [ReturnShipments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {shipmentEvent.ReturnShipmentId}")
                .SingleAsync(cancellationToken);

            // All events for one shipment now serialize behind the parent-row update lock. The
            // duplicate check is intentionally inside that lock, closing the pre-check race.
            var duplicate = await _dbContext.ReturnShipmentEvents.AnyAsync(
                e => e.Source == shipmentEvent.Source && e.ExternalEventId == shipmentEvent.ExternalEventId,
                cancellationToken);
            if (duplicate)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AppendShipmentEventResult(shipment, WasDuplicate: true);
            }

            var request = await _dbContext.ReturnRequests
                .SingleAsync(r => r.Id == shipment.ReturnRequestId, cancellationToken);
            var histories = applyToLatestState(shipment, request);

            await _dbContext.ReturnShipmentEvents.AddAsync(shipmentEvent, cancellationToken);
            if (histories.Count > 0)
            {
                await _dbContext.ReturnStatusHistories.AddRangeAsync(histories, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AppendShipmentEventResult(shipment, WasDuplicate: false);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
                "The shipment was modified by another request. Reload and try again.");
        }
        catch (DbUpdateException ex) when (IsIndexViolation(ex, "UX_ReturnShipmentEvents_Source_ExternalEventId"))
        {
            // Defensive fallback for the global unique key if the same carrier event was ever
            // routed concurrently against different shipment rows. It remains an idempotent
            // success and never leaks a provider exception as HTTP 500.
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            var shipment = await _dbContext.ReturnShipments
                .AsNoTracking()
                .SingleAsync(s => s.Id == shipmentEvent.ReturnShipmentId, cancellationToken);
            return new AppendShipmentEventResult(shipment, WasDuplicate: true);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<Guid>> CancelOverdueAwaitingShipmentAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var overdueIds = await _dbContext.ReturnRequests
            .Where(r => r.Status == ReturnRequestStatus.AwaitingShipment &&
                        r.ReturnShipmentDueAtUtc != null && r.ReturnShipmentDueAtUtc <= nowUtc)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var cancelledPublicIds = new List<Guid>();
        foreach (var id in overdueIds)
        {
            cancelledPublicIds.AddRange(await TryCancelOneAsync(id, nowUtc, cancellationToken));
        }

        return cancelledPublicIds;
    }

    private async Task<IReadOnlyList<Guid>> TryCancelOneAsync(long returnRequestId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var request = await _dbContext.ReturnRequests.SingleOrDefaultAsync(r => r.Id == returnRequestId, cancellationToken);
            if (request is null || request.Status != ReturnRequestStatus.AwaitingShipment ||
                request.ReturnShipmentDueAtUtc is null || request.ReturnShipmentDueAtUtc > nowUtc)
            {
                // Already handled by a previous (possibly concurrent) run — idempotent no-op.
                await transaction.RollbackAsync(cancellationToken);
                return [];
            }

            var fromStatus = request.Status;
            request.Transition(ReturnRequestStatus.Cancelled, nowUtc);
            await _dbContext.ReturnStatusHistories.AddAsync(
                new ReturnStatusHistory(
                    request.Id, fromStatus, ReturnRequestStatus.Cancelled,
                    "shipment-deadline-expired", "Automatically cancelled: shipment deadline passed.",
                    actorUserId: null, nowUtc),
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return [request.PublicId];
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsIndexViolation(DbUpdateException ex, string indexName) =>
        ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sqlException &&
        sqlException.Message.Contains(indexName, StringComparison.Ordinal);
}
