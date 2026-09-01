using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Inventory;

/// <summary>
/// See <see cref="IInventoryReservationService"/>: never opens its own transaction — the caller
/// (Checkout's future use case, an admin action, or the timeout sweep job) owns the ambient
/// transaction, and every write here just participates in it via the shared <see cref="DoSelectDbContext"/>.
/// </summary>
public sealed class EfInventoryReservationService : IInventoryReservationService
{
    private const int MaxConcurrencyRetries = 3;
    private const string OrderReferenceType = "Order";

    private readonly DoSelectDbContext _dbContext;

    public EfInventoryReservationService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ReserveAsync(
        long orderId,
        IReadOnlyList<ReservationLine> lines,
        DateTime? expiresAtUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one reservation line is required.", nameof(lines));
        }

        // This method writes Balance/Reservation and the InventoryMovement audit trail as two
        // separate SaveChangesAsync calls (see the loop below) — only atomic if the caller already
        // owns an ambient transaction, per the documented contract. Fail fast instead of silently
        // reserving stock with a window where a Movement could go missing (組長 PR #36 round-4
        // review, item 4).
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ReserveAsync)} must run inside an ambient transaction owned by the caller.");
        }

        // Merge duplicate SKU lines before validating stock: checking each line against the same
        // starting AvailableQuantity, then summing afterwards, can pass validation for e.g. two
        // lines of 3 units each against a balance of 5 (3 <= 5 and 3 <= 5 both pass individually)
        // and only fail later inside the domain guard when the merged total (6) actually gets
        // applied (組長 PR #36 round-4 review, item 4).
        lines = lines
            .GroupBy(line => line.SkuPublicId)
            .Select(group => new ReservationLine(group.Key, group.Sum(line => line.Quantity)))
            .ToArray();

        var skuPublicIds = lines.Select(line => line.SkuPublicId).Distinct().ToArray();
        var skusByPublicId = await _dbContext.Skus
            .Where(sku => skuPublicIds.Contains(sku.PublicId))
            .ToDictionaryAsync(sku => sku.PublicId, cancellationToken);
        foreach (var skuPublicId in skuPublicIds)
        {
            if (!skusByPublicId.ContainsKey(skuPublicId))
            {
                throw new InventoryWriteException(
                    InventoryWriteException.ErrorCodes.ResourceNotFound,
                    $"SKU '{skuPublicId}' was not found.");
            }
        }

        var orderPublicId = await _dbContext.Orders
            .Where(order => order.Id == orderId)
            .Select(order => order.PublicId)
            .SingleAsync(cancellationToken);

        var skuIds = lines.Select(line => skusByPublicId[line.SkuPublicId].Id).Distinct().ToArray();
        // dev@e41ef51 (#66) made InventoryMovement.UnitCostSnapshot required so the M-15 inventory
        // turnover report can cost movements. Sku.UnitCost is the authoritative source the other
        // writers already use (EfOrderService, EfSkuAdminService); the Skus are already materialised
        // here, so read the cost off them rather than issuing a second query.
        var unitCostsBySkuId = skusByPublicId.Values.ToDictionary(sku => sku.Id, sku => sku.UnitCost);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var balancesBySkuId = await _dbContext.InventoryBalances
                .Where(balance => skuIds.Contains(balance.SkuId))
                .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

            EnsureSufficientStock(lines, skusByPublicId, balancesBySkuId);

            var reservations = new List<InventoryReservation>(lines.Count);
            var movementInputs = new List<(InventoryReservation Reservation, int BeforeOnHand, int AfterOnHand, int BeforeReserved, int AfterReserved)>(lines.Count);
            foreach (var line in lines)
            {
                var sku = skusByPublicId[line.SkuPublicId];
                var balance = balancesBySkuId[sku.Id];
                var beforeOnHand = balance.OnHandQuantity;
                var beforeReserved = balance.ReservedQuantity;
                var afterReserved = beforeReserved + line.Quantity;
                balance.ApplyQuantities(beforeOnHand, afterReserved, now);

                var reservation = new InventoryReservation(
                    Guid.CreateVersion7(), sku.Id, orderId, line.Quantity, expiresAtUtc, now);
                _dbContext.InventoryReservations.Add(reservation);
                reservations.Add(reservation);
                movementInputs.Add((reservation, beforeOnHand, beforeOnHand, beforeReserved, afterReserved));
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachAddedReservations(reservations);
                await ReloadConflictedBalancesAsync(balancesBySkuId.Values, cancellationToken);
                if (attempt == MaxConcurrencyRetries)
                {
                    EnsureSufficientStock(lines, skusByPublicId, balancesBySkuId);
                    throw new InventoryWriteException(
                        InventoryWriteException.ErrorCodes.ConcurrencyConflict,
                        "The inventory balance changed too many times while reserving stock. Try again.");
                }

                continue;
            }

            var movements = movementInputs.Select(input => new InventoryMovement(
                Guid.CreateVersion7(),
                input.Reservation.SkuId,
                input.Reservation.Id,
                InventoryMovementTypes.Reserve,
                onHandDelta: 0,
                reservedDelta: input.Reservation.Quantity,
                input.BeforeOnHand,
                input.AfterOnHand,
                input.BeforeReserved,
                input.AfterReserved,
                unitCostSnapshot: unitCostsBySkuId[input.Reservation.SkuId],
                reasonCode: "order_checkout",
                referenceType: OrderReferenceType,
                orderPublicId,
                actorUserId: null,
                now));
            _dbContext.InventoryMovements.AddRange(movements);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
    }

    public async Task ConsumeAllForOrderAsync(long orderId, DateTime now, CancellationToken cancellationToken)
    {
        var reservations = await _dbContext.InventoryReservations
            .Where(reservation => reservation.OrderId == orderId && reservation.Status == InventoryReservationStatus.Active)
            .ToListAsync(cancellationToken);
        if (reservations.Count == 0)
        {
            return;
        }

        var orderPublicId = await _dbContext.Orders
            .Where(order => order.Id == orderId)
            .Select(order => order.PublicId)
            .SingleAsync(cancellationToken);
        var skuIds = reservations.Select(reservation => reservation.SkuId).Distinct().ToArray();
        var balancesBySkuId = await _dbContext.InventoryBalances
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);
        var unitCostsBySkuId = await ReadUnitCostsAsync(skuIds, cancellationToken);

        var movements = new List<InventoryMovement>(reservations.Count);
        foreach (var reservation in reservations)
        {
            var balance = balancesBySkuId[reservation.SkuId];
            var beforeOnHand = balance.OnHandQuantity;
            var beforeReserved = balance.ReservedQuantity;
            var afterOnHand = beforeOnHand - reservation.Quantity;
            var afterReserved = beforeReserved - reservation.Quantity;
            balance.ApplyQuantities(afterOnHand, afterReserved, now);
            reservation.Consume(now);
            movements.Add(new InventoryMovement(
                Guid.CreateVersion7(), reservation.SkuId, reservation.Id, InventoryMovementTypes.Ship,
                onHandDelta: -reservation.Quantity, reservedDelta: -reservation.Quantity,
                beforeOnHand, afterOnHand, beforeReserved, afterReserved,
                unitCostSnapshot: unitCostsBySkuId[reservation.SkuId],
                reasonCode: "order_shipped", referenceType: OrderReferenceType, orderPublicId,
                actorUserId: null, now));
        }

        _dbContext.InventoryMovements.AddRange(movements);
        await SaveWithConcurrencyCheckAsync(cancellationToken);
    }

    public async Task<int> ReleaseAllForOrderAsync(
        long orderId, string reasonCode, DateTime now, CancellationToken cancellationToken)
    {
        var reservations = await _dbContext.InventoryReservations
            .Where(reservation => reservation.OrderId == orderId && reservation.Status == InventoryReservationStatus.Active)
            .ToListAsync(cancellationToken);
        if (reservations.Count == 0)
        {
            return 0;
        }

        var orderPublicId = await _dbContext.Orders
            .Where(order => order.Id == orderId)
            .Select(order => order.PublicId)
            .SingleAsync(cancellationToken);
        await ReleaseCoreAsync(reservations, reasonCode, expired: false, OrderReferenceType, orderPublicId, now, cancellationToken);
        return reservations.Count;
    }

    public async Task<int> ExpireOverdueReservationsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var overdue = await _dbContext.InventoryReservations
            .Where(reservation =>
                reservation.Status == InventoryReservationStatus.Active &&
                reservation.ExpiresAtUtc != null &&
                reservation.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        if (overdue.Count == 0)
        {
            return 0;
        }

        var orderIds = overdue.Select(reservation => reservation.OrderId).Distinct().ToArray();
        var orderPublicIdsByOrderId = await _dbContext.Orders
            .Where(order => orderIds.Contains(order.Id))
            .ToDictionaryAsync(order => order.Id, order => order.PublicId, cancellationToken);

        var releasedCount = 0;
        foreach (var group in overdue.GroupBy(reservation => reservation.OrderId))
        {
            releasedCount += await ReleaseGroupSkippingAlreadyProcessedAsync(
                group.ToList(), orderPublicIdsByOrderId[group.Key], now, cancellationToken);
        }

        return releasedCount;
    }

    /// <summary>
    /// Releases one order's overdue reservations, tolerating another worker (a concurrent sweep run,
    /// a manual release, an order cancellation) having already claimed one of them since the caller
    /// loaded this batch. A single <c>SaveChangesAsync</c> across the whole group fails entirely on
    /// any RowVersion conflict — that used to abort the whole group instead of skipping just the
    /// already-processed reservation (組長 PR #36 round-4 review, item 5's "已處理者略過、可安全並行"
    /// contract).
    /// </summary>
    private async Task<int> ReleaseGroupSkippingAlreadyProcessedAsync(
        List<InventoryReservation> reservations, Guid orderPublicId, DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            // Reload every reservation's real current status before each attempt (including the
            // first) — ReleaseCoreAsync always reads Balance fresh, so trusting a stale in-memory
            // Active status here would recompute Reserved against a balance another worker already
            // decremented for the same reservation, going negative and throwing from the domain
            // guard before SaveChangesAsync ever runs (i.e. before RowVersion could catch it).
            foreach (var reservation in reservations)
            {
                await _dbContext.Entry(reservation).ReloadAsync(cancellationToken);
            }

            var active = reservations.Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToList();
            if (active.Count == 0)
            {
                return 0;
            }

            try
            {
                await ReleaseCoreAsync(
                    active, "payment_timeout", expired: true, OrderReferenceType, orderPublicId, now, cancellationToken);
                return active.Count;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt == MaxConcurrencyRetries)
                {
                    return 0;
                }

                await RecoverFromRaceAsync(active, cancellationToken);
                reservations = active;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Same race as the DbUpdateConcurrencyException case, caught one step earlier: this
                // attempt's reservation-status reload (above) can still be followed by another
                // worker's commit before ReleaseCoreAsync's own fresh Balance read — Balance is
                // already decremented for a reservation this attempt still (correctly, as of its own
                // reload) believed was Active, so subtracting its Quantity again goes negative and
                // ApplyQuantities throws before SaveChangesAsync ever runs, i.e. before RowVersion
                // gets a chance to catch it. Recover exactly like a concurrency conflict; the next
                // attempt's reload will see the reservation as no longer Active and skip it.
                if (attempt == MaxConcurrencyRetries)
                {
                    return 0;
                }

                await RecoverFromRaceAsync(active, cancellationToken);
                reservations = active;
            }
        }

        return 0;
    }

    /// <summary>Detaches this failed attempt's unsaved Movements and reloads the Balance rows it mutated in-memory, so the next attempt starts clean.</summary>
    private async Task RecoverFromRaceAsync(List<InventoryReservation> active, CancellationToken cancellationToken)
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<InventoryMovement>()
            .Where(entry => entry.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }

        var skuIds = active.Select(reservation => reservation.SkuId).ToHashSet();
        foreach (var balance in _dbContext.ChangeTracker.Entries<InventoryBalance>()
            .Where(entry => skuIds.Contains(entry.Entity.SkuId))
            .Select(entry => entry.Entity).ToList())
        {
            await _dbContext.Entry(balance).ReloadAsync(cancellationToken);
        }
    }

    public async Task ReleaseAsync(
        Guid reservationPublicId,
        string reasonCode,
        string note,
        string adminUserId,
        byte[] expectedRowVersion,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!InventoryReleaseReasonCodes.All.Contains(reasonCode))
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed,
                $"Unsupported reasonCode '{reasonCode}'.");
        }

        var reservation = await _dbContext.InventoryReservations
            .FirstOrDefaultAsync(candidate => candidate.PublicId == reservationPublicId, cancellationToken);
        if (reservation is null)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ResourceNotFound, "The reservation was not found.");
        }

        if (reservation.Status != InventoryReservationStatus.Active)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ReservationNotActive,
                "Only an Active reservation can be released.");
        }

        _dbContext.Entry(reservation).Property(candidate => candidate.RowVersion).OriginalValue = expectedRowVersion;

        var orderPublicId = await _dbContext.Orders
            .Where(order => order.Id == reservation.OrderId)
            .Select(order => order.PublicId)
            .SingleAsync(cancellationToken);

        try
        {
            await ReleaseCoreAsync(
                [reservation], reasonCode, expired: false, OrderReferenceType, orderPublicId, now, cancellationToken,
                actorUserId: adminUserId, note: note);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(reservation).ReloadAsync(cancellationToken);
            throw new InventoryWriteException(
                reservation.Status == InventoryReservationStatus.Active
                    ? InventoryWriteException.ErrorCodes.ConcurrencyConflict
                    : InventoryWriteException.ErrorCodes.ReservationAlreadyProcessed,
                "The reservation was already changed by someone else.");
        }
    }

    private async Task ReleaseCoreAsync(
        IReadOnlyList<InventoryReservation> reservations,
        string reasonCode,
        bool expired,
        string referenceType,
        Guid referencePublicId,
        DateTime now,
        CancellationToken cancellationToken,
        string? actorUserId = null,
        string? note = null)
    {
        var skuIds = reservations.Select(reservation => reservation.SkuId).Distinct().ToArray();
        var balancesBySkuId = await _dbContext.InventoryBalances
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);
        var unitCostsBySkuId = await ReadUnitCostsAsync(skuIds, cancellationToken);

        var movements = new List<InventoryMovement>(reservations.Count);
        foreach (var reservation in reservations)
        {
            var balance = balancesBySkuId[reservation.SkuId];
            var beforeOnHand = balance.OnHandQuantity;
            var beforeReserved = balance.ReservedQuantity;
            var afterReserved = beforeReserved - reservation.Quantity;
            balance.ApplyQuantities(beforeOnHand, afterReserved, now);
            // note has no column to land in: ReleaseReason/ReasonCode are both HasMaxLength(32)
            // domain reason codes. 組長 PR #36 review, item 4: the central Audit Log/IAuditWriter
            // this originally waited on has since merged into dev, but wiring manual release up to
            // it (same-transaction write, Action/Resource whitelist) is explicitly deferred to a
            // follow-up PR — this PR keeps the manual-release/reconciliation-resolve HTTP
            // endpoints withdrawn, so note stays validated but not persisted here for now.
            reservation.Release(reasonCode, expired, now);
            movements.Add(new InventoryMovement(
                Guid.CreateVersion7(), reservation.SkuId, reservation.Id, InventoryMovementTypes.Release,
                onHandDelta: 0, reservedDelta: -reservation.Quantity,
                beforeOnHand, beforeOnHand, beforeReserved, afterReserved,
                unitCostsBySkuId[reservation.SkuId],
                reasonCode, referenceType, referencePublicId, actorUserId, now));
        }

        _dbContext.InventoryMovements.AddRange(movements);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the authoritative per-SKU unit cost for <see cref="InventoryMovement.UnitCostSnapshot"/>,
    /// mirroring how EfOrderService sources it for its own release movements.
    /// </summary>
    private Task<Dictionary<long, decimal>> ReadUnitCostsAsync(
        IReadOnlyCollection<long> skuIds, CancellationToken cancellationToken) =>
        _dbContext.Skus
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, sku => sku.UnitCost, cancellationToken);

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ConcurrencyConflict,
                "The inventory balance was updated by someone else. Reload and try again.");
        }
    }

    private static void EnsureSufficientStock(
        IReadOnlyList<ReservationLine> lines,
        IReadOnlyDictionary<Guid, DoSelect.Domain.Catalog.Sku> skusByPublicId,
        IReadOnlyDictionary<long, InventoryBalance> balancesBySkuId)
    {
        foreach (var line in lines)
        {
            var sku = skusByPublicId[line.SkuPublicId];
            if (!balancesBySkuId.TryGetValue(sku.Id, out var balance) || balance.AvailableQuantity < line.Quantity)
            {
                throw new InventoryWriteException(
                    InventoryWriteException.ErrorCodes.InsufficientStock,
                    $"SKU '{line.SkuPublicId}' has insufficient available stock.");
            }
        }
    }

    private void DetachAddedReservations(IReadOnlyList<InventoryReservation> reservations)
    {
        foreach (var reservation in reservations)
        {
            _dbContext.Entry(reservation).State = EntityState.Detached;
        }
    }

    private async Task ReloadConflictedBalancesAsync(
        IEnumerable<InventoryBalance> balances, CancellationToken cancellationToken)
    {
        foreach (var balance in balances)
        {
            await _dbContext.Entry(balance).ReloadAsync(cancellationToken);
        }
    }
}
