using DoSelect.Application.Common;

namespace DoSelect.Application.Inventory;

/// <summary>Admin read surface behind A-11／A-12 (balances, movements, reservation queue).</summary>
public interface IInventoryAdminQueryService
{
    Task<PageResult<InventoryBalanceDto>> ListBalancesAsync(
        InventoryBalanceQuery query, CancellationToken cancellationToken);

    Task<PageResult<InventoryMovementDto>> ListMovementsAsync(
        InventoryMovementQuery query, CancellationToken cancellationToken);

    Task<CursorPage<InventoryReservationDto>> ListReservationsAsync(
        InventoryReservationListQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// The cross-module reservation contract (庫存規則.md). Checkout (haru／yinyin, not yet built) is
/// expected to call <see cref="ReserveAsync"/> from inside its own transaction — this service never
/// opens a transaction of its own, mirroring <c>EfCartService</c>'s plain-DbContext services that
/// let an outer coordinator (there, <c>IIdempotencyExecutor</c>) own the unit of work. Every method
/// here shares one core release/consume domain operation so manual admin release, order
/// cancellation, and the timeout sweep can never double-release the same reservation.
/// </summary>
public interface IInventoryReservationService
{
    /// <summary>
    /// Atomically reserves every line for one order. If any SKU's available quantity is
    /// insufficient, nothing is reserved — throws <see cref="InventoryWriteException"/> with
    /// <see cref="InventoryWriteException.ErrorCodes.InsufficientStock"/>. Caller must run this
    /// inside its own transaction alongside creating the Order row.
    /// </summary>
    Task ReserveAsync(
        long orderId,
        IReadOnlyList<ReservationLine> lines,
        DateTime? expiresAtUtc,
        DateTime now,
        CancellationToken cancellationToken);

    /// <summary>Ship-time: converts every Active reservation for the order to Consumed and deducts OnHand.</summary>
    Task ConsumeAllForOrderAsync(long orderId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Cancellation/timeout at the order level (e.g. order cancelled before shipment). Returns the count released.</summary>
    Task<int> ReleaseAllForOrderAsync(
        long orderId, string reasonCode, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Idempotent sweep for the background timeout job (庫存規則.md 逾時取消): releases every Active
    /// reservation whose ExpiresAtUtc has passed. Safe to call repeatedly / concurrently — an
    /// already-released reservation is simply skipped, never double-released.
    /// </summary>
    Task<int> ExpireOverdueReservationsAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Manual admin release of one Active reservation (UC-ADM-INV-01). Uses the same domain
    /// operation as the timeout sweep. Throws <see cref="InventoryWriteException"/> with
    /// <see cref="InventoryWriteException.ErrorCodes.ReservationNotActive"/> if it is not
    /// currently Active, or <see cref="InventoryWriteException.ErrorCodes.ReservationAlreadyProcessed"/>
    /// if a concurrent request processed it first.
    /// </summary>
    Task ReleaseAsync(
        Guid reservationPublicId,
        string reasonCode,
        string note,
        string adminUserId,
        byte[] expectedRowVersion,
        DateTime now,
        CancellationToken cancellationToken);
}

/// <summary>
/// Daily Balance／Movement／Reservation reconciliation (庫存規則.md; entity/schema already defined,
/// admin API/entity/tests were listed as 待實作 in 背景工作與Hangfire設計.md — no owner or slice was
/// named anywhere in the docs, so this is built here since it's the same Inventory domain, and
/// flagged to 組長 in the PR since it is not in the official API Endpoint目錄).
/// </summary>
public interface IInventoryReconciliationService
{
    /// <summary>
    /// Recomputes each SKU's true Reserved (from Active reservations) and OnHand (from the
    /// Movement ledger) and compares against the live Balance row. Opens an `Open`
    /// InventoryReconciliationCase for any mismatch — idempotent per SKU (the unique filtered
    /// index only allows one Open case per SKU at a time). Never mutates Balance itself. Returns
    /// the number of new cases opened.
    /// </summary>
    Task<int> DetectDiscrepanciesAsync(DateTime now, CancellationToken cancellationToken);

    Task<PageResult<InventoryReconciliationCaseDto>> ListCasesAsync(
        InventoryReconciliationCaseQuery query, CancellationToken cancellationToken);

    Task AcknowledgeAsync(
        Guid casePublicId, string adminUserId, byte[] expectedRowVersion, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Dismissed: closes the case with a reason, no Balance change. Otherwise: creates a
    /// ManualIncrease／ManualDecrease InventoryMovement moving Balance to the case's already-recorded
    /// Actual* values, and resolves the case referencing that Movement.
    /// </summary>
    Task ResolveAsync(
        Guid casePublicId,
        string adminUserId,
        ResolveReconciliationCaseRequest request,
        DateTime now,
        CancellationToken cancellationToken);
}
