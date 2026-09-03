using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using DoSelect.Domain.Auditing;
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
    private readonly IAuditWriter _auditWriter;

    public EfInventoryReservationService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
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

    public async Task ReleaseAsync(
        Guid reservationPublicId,
        string reasonCode,
        string note,
        string adminUserId,
        byte[] expectedRowVersion,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!InventoryReleaseReasonCodes.All.Contains(reasonCode))
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed,
                $"Unsupported reasonCode '{reasonCode}'.");
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            // 驗收：「管理員未填原因，When 送出，Then API 拒絕操作且庫存數量不變」。reasonCode 是
            // 白名單裡的分類，note 才是人看得懂的原因，兩個都要。
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed, "A release note is required.");
        }

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);

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
        var skuPublicId = await _dbContext.Skus
            .Where(sku => sku.Id == reservation.SkuId)
            .Select(sku => sku.PublicId)
            .SingleAsync(cancellationToken);
        // 先把 Balance 追蹤進來拿稽核要記的 before 值。ReleaseCoreAsync 稍後查同一列時會拿到這個
        // 已追蹤的實體（同一個 DbContext），所以稽核寫的 before／after 跟它實際套用的是同一組數字。
        var balance = await _dbContext.InventoryBalances
            .SingleAsync(candidate => candidate.SkuId == reservation.SkuId, cancellationToken);
        var beforeReserved = balance.ReservedQuantity;

        // 中央稽核先加進 ChangeTracker，再由 ReleaseCoreAsync 的那一次 SaveChanges 跟 Balance／
        // Reservation／Movement 一起寫：稽核寫不進去，釋放就整筆不成立（驗收「保存 InventoryMovement
        // 與 Audit Log」是一個條件，不是兩個）。Create 會依中央規則驗 note 的字元與長度，這裡把
        // 它翻成 validation_failed——這時還沒有任何東西送到資料庫。
        AuditWriteRequest auditRequest;
        try
        {
            auditRequest = AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.InventoryReservationRelease,
                AuditResourceTypes.InventoryReservation,
                reservation.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("status", InventoryReservationStatus.Active.ToString(), InventoryReservationStatus.Released.ToString()),
                    AuditFieldChange.Code("reasonCode", null, reasonCode),
                    AuditFieldChange.Code("orderPublicId", null, orderPublicId.ToString("D")),
                    AuditFieldChange.Code("skuPublicId", null, skuPublicId.ToString("D")),
                    AuditFieldChange.Code("quantity", null, reservation.Quantity.ToString(CultureInfo.InvariantCulture)),
                    AuditFieldChange.Code(
                        "reservedQuantity",
                        beforeReserved.ToString(CultureInfo.InvariantCulture),
                        (beforeReserved - reservation.Quantity).ToString(CultureInfo.InvariantCulture)),
                ],
                reason: reasonCode,
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress,
                note: note);
        }
        catch (ArgumentException exception)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed,
                $"The release note is not accepted by the audit log: {exception.Message}");
        }

        _auditWriter.Add(auditRequest);

        try
        {
            await ReleaseCoreAsync(
                [reservation], reasonCode, expired: false, OrderReferenceType, orderPublicId, now, cancellationToken,
                actorUserId: adminUserId);
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

    /// <summary>
    /// 與 EfInventoryImportService.ResolveActorAsync 同形：稽核的角色快照要從真正的 UserRoles 讀，
    /// 不信任呼叫端傳來的任何角色字串。人工釋放的 Policy 是 InventoryManager／SuperAdmin。
    /// </summary>
    private async Task<AuditActor> ResolveActorAsync(string adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.Forbidden("The administrator identity is invalid.");

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
            throw DomainProblemException.Forbidden("The administrator is not allowed to release inventory reservations.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    private async Task ReleaseCoreAsync(
        IReadOnlyList<InventoryReservation> reservations,
        string reasonCode,
        bool expired,
        string referenceType,
        Guid referencePublicId,
        DateTime now,
        CancellationToken cancellationToken,
        string? actorUserId = null)
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
            // 人工釋放的自由文字 note 不在這裡：ReleaseReason／ReasonCode 都是 HasMaxLength(32) 的
            // 領域代碼，放不下；它落在 ReleaseAsync 寫的中央稽核（inventory_reservation.release）的 note。
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
