using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Inventory;

/// <summary>
/// Naming convention for <see cref="InventoryReconciliationCase"/> (not defined anywhere in the
/// docs — flagged in the PR): Expected* is what <see cref="InventoryBalance"/> currently states;
/// Actual* is freshly recomputed from the InventoryMovement ledger (庫存規則.md: Movement is the
/// auditable source, Balance is only a derived value). Resolve corrects Balance to match Actual*.
/// </summary>
public sealed class EfInventoryReconciliationService : IInventoryReconciliationService
{
    // The API contract caps PageSize at 100 (AdminInventoryController's [Range(1,100)]); this is a
    // defense-in-depth match for callers of the service directly, not the primary enforcement point.
    private const int MaxPageSize = 100;

    private readonly DoSelectDbContext _dbContext;

    public EfInventoryReconciliationService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> DetectDiscrepanciesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var balances = await _dbContext.InventoryBalances.AsNoTracking().ToListAsync(cancellationToken);
        if (balances.Count == 0)
        {
            return 0;
        }

        var skuIds = balances.Select(balance => balance.SkuId).ToArray();

        var actualReservedBySkuId = await _dbContext.InventoryReservations.AsNoTracking()
            .Where(reservation => skuIds.Contains(reservation.SkuId) &&
                reservation.Status == InventoryReservationStatus.Active)
            .GroupBy(reservation => reservation.SkuId)
            .Select(group => new { SkuId = group.Key, Total = group.Sum(reservation => reservation.Quantity) })
            .ToDictionaryAsync(row => row.SkuId, row => row.Total, cancellationToken);

        var actualOnHandBySkuId = await _dbContext.InventoryMovements.AsNoTracking()
            .Where(movement => skuIds.Contains(movement.SkuId))
            .GroupBy(movement => movement.SkuId)
            .Select(group => new { SkuId = group.Key, Total = group.Sum(movement => movement.OnHandDelta) })
            .ToDictionaryAsync(row => row.SkuId, row => row.Total, cancellationToken);

        // An Acknowledged case is still unresolved (only Resolved／Dismissed close it) — excluding
        // only Open let the next sweep open a second case for the same still-broken SKU.
        var existingUnresolvedSkuIds = (await _dbContext.InventoryReconciliationCases.AsNoTracking()
            .Where(reconciliationCase =>
                (reconciliationCase.Status == InventoryReconciliationStatus.Open ||
                    reconciliationCase.Status == InventoryReconciliationStatus.Acknowledged) &&
                skuIds.Contains(reconciliationCase.SkuId))
            .Select(reconciliationCase => reconciliationCase.SkuId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var newCases = new List<InventoryReconciliationCase>();
        foreach (var balance in balances)
        {
            if (existingUnresolvedSkuIds.Contains(balance.SkuId))
            {
                continue;
            }

            // Movement ledger can't represent negative physical stock; a negative sum only means
            // the ledger is incomplete for this SKU (e.g. seeded Balance with no StockIn trail) —
            // clamp to 0 so it still surfaces as a flagged case instead of violating the DB CHECK
            // constraint on the new case's non-negative columns.
            var actualReserved = Math.Max(0, actualReservedBySkuId.GetValueOrDefault(balance.SkuId, 0));
            var actualOnHand = Math.Max(0, actualOnHandBySkuId.GetValueOrDefault(balance.SkuId, 0));
            if (actualReserved == balance.ReservedQuantity && actualOnHand == balance.OnHandQuantity)
            {
                continue;
            }

            newCases.Add(new InventoryReconciliationCase(
                Guid.CreateVersion7(),
                balance.SkuId,
                expectedOnHand: balance.OnHandQuantity,
                actualOnHand: actualOnHand,
                expectedReserved: balance.ReservedQuantity,
                actualReserved: actualReserved,
                detectedAtUtc: now,
                createdAtUtc: now));
        }

        if (newCases.Count == 0)
        {
            return 0;
        }

        _dbContext.InventoryReconciliationCases.AddRange(newCases);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return newCases.Count;
    }

    public async Task<PageResult<InventoryReconciliationCaseDto>> ListCasesAsync(
        InventoryReconciliationCaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var cases = _dbContext.InventoryReconciliationCases.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<InventoryReconciliationStatus>(query.Status, ignoreCase: true, out var status) ||
                !Enum.IsDefined(status))
            {
                throw new InventoryWriteException(
                    InventoryWriteException.ErrorCodes.ValidationFailed,
                    $"Unsupported status '{query.Status}'.");
            }

            cases = cases.Where(reconciliationCase => reconciliationCase.Status == status);
        }

        var totalCount = await cases.CountAsync(cancellationToken);
        // Same int-overflow guard as EfProductAdminService/EfInventoryAdminQueryService: (pageNumber
        // - 1) * pageSize can overflow int for a large-but-Range-valid pageNumber.
        var skip = (long)(pageNumber - 1) * pageSize;
        var page = skip > int.MaxValue
            ? []
            : await cases
                .OrderByDescending(reconciliationCase => reconciliationCase.DetectedAtUtc)
                // A single DetectDiscrepanciesAsync sweep stamps every case it opens with the same
                // DetectedAtUtc — without a tiebreaker, paging through same-timestamp cases can skip
                // or repeat rows (組長 PR #36 review).
                .ThenByDescending(reconciliationCase => reconciliationCase.Id)
                .Skip((int)skip)
                .Take(pageSize)
                .Join(_dbContext.Skus.AsNoTracking(), reconciliationCase => reconciliationCase.SkuId, sku => sku.Id,
                    (reconciliationCase, sku) => new { reconciliationCase, sku })
                .ToListAsync(cancellationToken);

        var adminUserIds = page
            .SelectMany(row => new[] { row.reconciliationCase.AcknowledgedBy, row.reconciliationCase.ResolvedByAdminUserId })
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToArray();
        var adminUsersById = adminUserIds.Length == 0
            ? new Dictionary<string, (Guid PublicId, string? Email)>()
            : await _dbContext.Users.AsNoTracking()
                .Where(user => adminUserIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => (user.PublicId, user.Email), cancellationToken);

        var movementIds = page
            .Where(row => row.reconciliationCase.ResolutionMovementId is not null)
            .Select(row => row.reconciliationCase.ResolutionMovementId!.Value)
            .Distinct()
            .ToArray();
        var movementPublicIdsById = movementIds.Length == 0
            ? new Dictionary<long, Guid>()
            : await _dbContext.InventoryMovements.AsNoTracking()
                .Where(movement => movementIds.Contains(movement.Id))
                .ToDictionaryAsync(movement => movement.Id, movement => movement.PublicId, cancellationToken);

        var dtos = page.Select(row => new InventoryReconciliationCaseDto(
            row.reconciliationCase.PublicId,
            new InventorySkuSummaryDto(row.sku.PublicId, row.sku.SkuCode, row.sku.NameZhTw),
            row.reconciliationCase.Status.ToString(),
            row.reconciliationCase.ExpectedOnHand,
            row.reconciliationCase.ActualOnHand,
            row.reconciliationCase.ExpectedReserved,
            row.reconciliationCase.ActualReserved,
            row.reconciliationCase.DetectedAtUtc,
            ToActorSummary(row.reconciliationCase.AcknowledgedBy, adminUsersById),
            ToActorSummary(row.reconciliationCase.ResolvedByAdminUserId, adminUsersById),
            row.reconciliationCase.ResolutionMovementId is long movementId
                ? movementPublicIdsById.GetValueOrDefault(movementId)
                : null,
            row.reconciliationCase.ResolutionReason,
            row.reconciliationCase.ResolvedAtUtc,
            row.reconciliationCase.RowVersion))
            .ToList();

        return new PageResult<InventoryReconciliationCaseDto>(dtos, pageNumber, pageSize, totalCount);
    }

    public async Task AcknowledgeAsync(
        Guid casePublicId, string adminUserId, byte[] expectedRowVersion, DateTime now, CancellationToken cancellationToken)
    {
        var reconciliationCase = await FindByPublicIdAsync(casePublicId, cancellationToken);
        _dbContext.Entry(reconciliationCase).Property(candidate => candidate.RowVersion).OriginalValue = expectedRowVersion;

        try
        {
            reconciliationCase.Acknowledge(adminUserId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ConcurrencyConflict,
                "The reconciliation case was changed by someone else.");
        }
    }

    public async Task ResolveAsync(
        Guid casePublicId,
        string adminUserId,
        ResolveReconciliationCaseRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Dismissed && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed,
                "A reason is required when dismissing a reconciliation case.");
        }

        var reconciliationCase = await FindByPublicIdAsync(casePublicId, cancellationToken);

        // Resolve needs the new Movement's identity Id before InventoryReconciliationCase.Resolve
        // can reference it, so this can't collapse into one SaveChangesAsync — wrap both saves in
        // one transaction so a RowVersion conflict on the *second* save (the case itself) rolls
        // back the Balance correction and Movement insert from the first, instead of leaving the
        // case stuck Open/Acknowledged with the stock already silently corrected underneath it.
        //
        // The RowVersion.OriginalValue override is set right before each branch's SaveChangesAsync
        // rather than once up front: an EF Core Unchanged entity's manually-poked OriginalValue can
        // get reconciled away by DetectChanges/AcceptAllChanges during an *earlier*, unrelated
        // SaveChangesAsync call on the same DbContext (the Movement/Balance save below) — silently
        // defeating the whole concurrency check. Setting it immediately before the save that
        // actually updates this entity avoids that.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (request.Dismissed)
            {
                _dbContext.Entry(reconciliationCase).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;
                reconciliationCase.Resolve(adminUserId, null, request.Reason, dismissed: true, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var balance = await _dbContext.InventoryBalances
                    .FirstAsync(candidate => candidate.SkuId == reconciliationCase.SkuId, cancellationToken);

                // The case's Expected*/Actual* were captured at DetectDiscrepanciesAsync time. A
                // legitimate StockIn/Ship/Reserve between detection and this Resolve call moves the
                // live Balance and/or ledger sum away from that snapshot — applying the stale
                // Actual* anyway would silently erase the later change (組長 PR #36 round-4 review).
                // Re-verify both, inside this same transaction, immediately before writing.
                if (balance.OnHandQuantity != reconciliationCase.ExpectedOnHand ||
                    balance.ReservedQuantity != reconciliationCase.ExpectedReserved)
                {
                    throw new InventoryWriteException(
                        InventoryWriteException.ErrorCodes.ConcurrencyConflict,
                        "The inventory balance changed since this case was detected. Re-detect before resolving.");
                }

                var (actualOnHand, actualReserved) = await RecomputeActualAsync(reconciliationCase.SkuId, cancellationToken);
                if (actualOnHand != reconciliationCase.ActualOnHand || actualReserved != reconciliationCase.ActualReserved)
                {
                    throw new InventoryWriteException(
                        InventoryWriteException.ErrorCodes.ConcurrencyConflict,
                        "The inventory ledger changed since this case was detected. Re-detect before resolving.");
                }

                // A ledger that can't produce a legal Balance (Reserved > OnHand) is exactly what
                // reconciliation exists to surface — InventoryBalance.ApplyQuantities would throw an
                // unmapped ArgumentOutOfRangeException for this, so guard it explicitly rather than
                // letting a 500 leak through and partially corrupt state (組長 PR #36 round-4 review).
                if (actualReserved > actualOnHand)
                {
                    throw new InventoryWriteException(
                        InventoryWriteException.ErrorCodes.ConcurrencyConflict,
                        "The recomputed ledger is internally inconsistent (Reserved exceeds OnHand) and cannot be applied to Balance. The case is left open for manual investigation.");
                }

                balance.ApplyQuantities(actualOnHand, actualReserved, now);

                // dev@e41ef51 (#66) made InventoryMovement.UnitCostSnapshot required. This movement
                // is a zero-delta marker, so the cost carries no valuation weight here — but the
                // column is non-nullable at construction, and Sku.UnitCost is the same authoritative
                // source the other writers use, so record the SKU's current cost rather than a
                // fabricated zero that would skew the M-15 turnover report's cost basis.
                var unitCostSnapshot = await _dbContext.Skus
                    .Where(sku => sku.Id == reconciliationCase.SkuId)
                    .Select(sku => sku.UnitCost)
                    .SingleAsync(cancellationToken);

                // Movement／Reservation is the ledger source of truth (庫存規則.md) — actualOnHand／
                // actualReserved were just recomputed FROM that same ledger above, so this correction
                // must record a zero-delta marker (before == after == Actual*) rather than a delta
                // that would change the ledger's own sum. Otherwise the next DetectDiscrepanciesAsync
                // run recomputes Actual* including this very correction and immediately reopens a new
                // case for the same SKU. The stale Balance value being corrected stays on the
                // ReconciliationCase's own Expected*/Actual* columns, not on this movement. Uses
                // Adjustment (組長's ruling), not ManualIncrease／ManualDecrease — those imply a real
                // quantity change happened and would mislead admin screens／future reports into
                // thinking stock was actually adjusted up or down when the ledger sum never moved.
                var movement = new InventoryMovement(
                    Guid.CreateVersion7(),
                    reconciliationCase.SkuId,
                    reservationId: null,
                    InventoryMovementTypes.Adjustment,
                    onHandDelta: 0,
                    reservedDelta: 0,
                    beforeOnHand: actualOnHand,
                    afterOnHand: actualOnHand,
                    beforeReserved: actualReserved,
                    afterReserved: actualReserved,
                    unitCostSnapshot: unitCostSnapshot,
                    reasonCode: "reconciliation_correction",
                    referenceType: "InventoryReconciliationCase",
                    casePublicId,
                    adminUserId,
                    now);
                _dbContext.InventoryMovements.Add(movement);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _dbContext.Entry(reconciliationCase).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;
                reconciliationCase.Resolve(adminUserId, movement.Id, request.Reason, dismissed: false, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ConcurrencyConflict,
                "The reconciliation case was changed by someone else.");
        }
    }

    /// <summary>Same recomputation as DetectDiscrepanciesAsync's batch query, scoped to one SKU for ResolveAsync's re-check.</summary>
    private async Task<(int ActualOnHand, int ActualReserved)> RecomputeActualAsync(
        long skuId, CancellationToken cancellationToken)
    {
        var actualReserved = await _dbContext.InventoryReservations.AsNoTracking()
            .Where(reservation => reservation.SkuId == skuId && reservation.Status == InventoryReservationStatus.Active)
            .SumAsync(reservation => (int?)reservation.Quantity, cancellationToken) ?? 0;
        var actualOnHand = await _dbContext.InventoryMovements.AsNoTracking()
            .Where(movement => movement.SkuId == skuId)
            .SumAsync(movement => (int?)movement.OnHandDelta, cancellationToken) ?? 0;

        // See the matching clamp in DetectDiscrepanciesAsync: an incomplete ledger can sum negative,
        // which isn't a real physical quantity.
        return (Math.Max(0, actualOnHand), Math.Max(0, actualReserved));
    }

    private static InventoryActorSummaryDto? ToActorSummary(
        string? adminUserId, IReadOnlyDictionary<string, (Guid PublicId, string? Email)> usersById) =>
        adminUserId is not null && usersById.TryGetValue(adminUserId, out var user)
            ? InventoryActorSummaryDto.FromIdentity(user.PublicId, user.Email)
            : null;

    private async Task<InventoryReconciliationCase> FindByPublicIdAsync(
        Guid casePublicId, CancellationToken cancellationToken)
    {
        var reconciliationCase = await _dbContext.InventoryReconciliationCases
            .FirstOrDefaultAsync(candidate => candidate.PublicId == casePublicId, cancellationToken);
        if (reconciliationCase is null)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ResourceNotFound, "The reconciliation case was not found.");
        }

        return reconciliationCase;
    }
}
