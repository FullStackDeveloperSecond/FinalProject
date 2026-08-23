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
    private const int MaxPageSize = 200;

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

        var existingOpenSkuIds = (await _dbContext.InventoryReconciliationCases.AsNoTracking()
            .Where(reconciliationCase => reconciliationCase.Status == InventoryReconciliationStatus.Open &&
                skuIds.Contains(reconciliationCase.SkuId))
            .Select(reconciliationCase => reconciliationCase.SkuId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var newCases = new List<InventoryReconciliationCase>();
        foreach (var balance in balances)
        {
            if (existingOpenSkuIds.Contains(balance.SkuId))
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
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<InventoryReconciliationStatus>(query.Status, ignoreCase: true, out var status))
        {
            cases = cases.Where(reconciliationCase => reconciliationCase.Status == status);
        }

        var totalCount = await cases.CountAsync(cancellationToken);
        var page = await cases
            .OrderByDescending(reconciliationCase => reconciliationCase.DetectedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Join(_dbContext.Skus.AsNoTracking(), reconciliationCase => reconciliationCase.SkuId, sku => sku.Id,
                (reconciliationCase, sku) => new { reconciliationCase, sku })
            .ToListAsync(cancellationToken);

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
            row.reconciliationCase.AcknowledgedBy,
            row.reconciliationCase.ResolvedByAdminUserId,
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
        _dbContext.Entry(reconciliationCase).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        try
        {
            if (request.Dismissed)
            {
                reconciliationCase.Resolve(adminUserId, null, request.Reason, dismissed: true, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var balance = await _dbContext.InventoryBalances
                .FirstAsync(candidate => candidate.SkuId == reconciliationCase.SkuId, cancellationToken);
            var beforeOnHand = balance.OnHandQuantity;
            var beforeReserved = balance.ReservedQuantity;
            balance.ApplyQuantities(reconciliationCase.ActualOnHand, reconciliationCase.ActualReserved, now);

            var movementType = reconciliationCase.ActualOnHand >= beforeOnHand
                ? InventoryMovementTypes.ManualIncrease
                : InventoryMovementTypes.ManualDecrease;
            var movement = new InventoryMovement(
                Guid.CreateVersion7(),
                reconciliationCase.SkuId,
                reservationId: null,
                movementType,
                onHandDelta: reconciliationCase.ActualOnHand - beforeOnHand,
                reservedDelta: reconciliationCase.ActualReserved - beforeReserved,
                beforeOnHand,
                reconciliationCase.ActualOnHand,
                beforeReserved,
                reconciliationCase.ActualReserved,
                reasonCode: "reconciliation_correction",
                referenceType: "InventoryReconciliationCase",
                casePublicId,
                adminUserId,
                now);
            _dbContext.InventoryMovements.Add(movement);
            await _dbContext.SaveChangesAsync(cancellationToken);

            reconciliationCase.Resolve(adminUserId, movement.Id, request.Reason, dismissed: false, now);
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
