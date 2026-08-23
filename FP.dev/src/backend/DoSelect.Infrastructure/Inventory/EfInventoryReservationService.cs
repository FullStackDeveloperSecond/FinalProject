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

        foreach (var group in overdue.GroupBy(reservation => reservation.OrderId))
        {
            await ReleaseCoreAsync(
                group.ToList(), "payment_timeout", expired: true, OrderReferenceType,
                orderPublicIdsByOrderId[group.Key], now, cancellationToken);
        }

        return overdue.Count;
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

        var movements = new List<InventoryMovement>(reservations.Count);
        foreach (var reservation in reservations)
        {
            var balance = balancesBySkuId[reservation.SkuId];
            var beforeOnHand = balance.OnHandQuantity;
            var beforeReserved = balance.ReservedQuantity;
            var afterReserved = beforeReserved - reservation.Quantity;
            balance.ApplyQuantities(beforeOnHand, afterReserved, now);
            // note (up to 500 chars, required by ReleaseReservationRequest per the DTO contract)
            // has no column to land in: ReleaseReason/ReasonCode are both HasMaxLength(32) domain
            // reason codes, and there is no Audit Log subsystem yet (alex's shared infrastructure,
            // not built) to hold free text. It is validated but not persisted here — flagged in
            // the PR as a known gap rather than truncating it into a 32-char column.
            reservation.Release(reasonCode, expired, now);
            movements.Add(new InventoryMovement(
                Guid.CreateVersion7(), reservation.SkuId, reservation.Id, InventoryMovementTypes.Release,
                onHandDelta: 0, reservedDelta: -reservation.Quantity,
                beforeOnHand, beforeOnHand, beforeReserved, afterReserved,
                reasonCode, referenceType, referencePublicId, actorUserId, now));
        }

        _dbContext.InventoryMovements.AddRange(movements);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

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
