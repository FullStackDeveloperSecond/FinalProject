using DoSelect.Application.Auditing;
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
    /// inside its own transaction alongside creating the Order row: this method writes the
    /// Balance/Reservation update and the InventoryMovement audit trail as two separate
    /// <c>SaveChangesAsync</c> calls, so without an ambient transaction a failure on the second
    /// save would leave the first committed with no matching Movement. Fails fast with
    /// <see cref="InvalidOperationException"/> if <c>Database.CurrentTransaction</c> is null when
    /// called, rather than silently reserving stock without the transaction the contract requires.
    /// Duplicate <see cref="ReservationLine.SkuPublicId"/> entries in <paramref name="lines"/> are
    /// merged into one quantity before the stock check, rather than validated line-by-line against
    /// the same starting balance.
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
    /// Manual admin release of one Active reservation (UC-ADM-INV-01). Uses the same domain
    /// operation as the timeout sweep. Throws <see cref="InventoryWriteException"/> with
    /// <see cref="InventoryWriteException.ErrorCodes.ReservationNotActive"/> if it is not
    /// currently Active, or <see cref="InventoryWriteException.ErrorCodes.ReservationAlreadyProcessed"/>
    /// if a concurrent request processed it first. <paramref name="reasonCode"/> must be one of
    /// <see cref="DoSelect.Domain.Inventory.InventoryReleaseReasonCodes.All"/>.
    /// <para>
    /// UC-ADM-INV-01 驗收：釋放成功要保存 InventoryMovement 與 Audit Log。這裡把
    /// <see cref="AuditActions.InventoryReservationRelease"/> 與 Balance／Reservation／Movement 放在
    /// 同一次 SaveChanges（同一筆交易）——寫不進稽核就整筆不算釋放。<paramref name="adminUserId"/>
    /// 必須是持有 InventoryManager 或 SuperAdmin 角色的管理員（稽核要留角色快照），否則
    /// <see cref="DomainProblemException"/> Forbidden。<paramref name="note"/> 是自由文字，會進稽核的
    /// note；不符合中央稽核的字元規則時以
    /// <see cref="InventoryWriteException.ErrorCodes.ValidationFailed"/> 拒絕，庫存不變。
    /// </para>
    /// </summary>
    Task ReleaseAsync(
        Guid reservationPublicId,
        string reasonCode,
        string note,
        string adminUserId,
        byte[] expectedRowVersion,
        AuditRequestContext auditContext,
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
    /// Dismissed: closes the case with a reason, no Balance change. Otherwise: re-verifies, inside
    /// the same transaction, that the live Balance and a fresh ledger recomputation still match
    /// what was captured at detection time (<see cref="InventoryReconciliationCaseDto.ExpectedOnHand"/>／
    /// <see cref="InventoryReconciliationCaseDto.ActualOnHand"/> etc.) — a legitimate StockIn／Ship／
    /// Reserve between detection and resolution makes the case's snapshot stale, and applying it
    /// anyway would silently erase that later change. A mismatch throws
    /// <see cref="InventoryWriteException.ErrorCodes.ConcurrencyConflict"/> and leaves the case
    /// unresolved rather than overwriting Balance with a stale target. Quantities that would leave
    /// Reserved &gt; OnHand are rejected the same way instead of throwing an unmapped domain
    /// exception — reconciliation exists to catch inconsistent ledgers, so an inconsistent result
    /// must be a stable, no-side-effect error, not a 500 that partially corrupts state. When the
    /// checks pass, creates an <see cref="DoSelect.Domain.Inventory.InventoryMovementTypes.Adjustment"/>
    /// InventoryMovement (zero OnHand／Reserved delta — a correction record, not a ledger change) and
    /// resolves the case referencing it. Not wired to any HTTP endpoint yet (PR #36 round-4 ruling,
    /// same class of gap as <see cref="IInventoryReservationService.ReleaseAsync"/>): a real
    /// non-dismissed resolution is a high-risk manual stock correction, and although the central
    /// <c>IAuditWriter</c> that records who／why now exists on dev, this PR does not wire the
    /// resolution up to it — that is deferred to a follow-up PR.
    /// </summary>
    Task ResolveAsync(
        Guid casePublicId,
        string adminUserId,
        ResolveReconciliationCaseRequest request,
        DateTime now,
        CancellationToken cancellationToken);
}
