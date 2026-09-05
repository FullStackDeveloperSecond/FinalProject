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
    /// 對帳案件以「核對基準錯誤」結案（組長對帳裁定 C1／D1／F1）：不動 Balance、不建 Movement；案件狀態與
    /// <c>inventory_reconciliation.dismiss</c> 稽核同一次 SaveChanges。reasonCode 限
    /// <see cref="DoSelect.Domain.Inventory.InventoryReconciliationReasonCodes.ForDismiss"/>，note 必填（trim 後存進案件
    /// ResolutionReason 與稽核 note）。Open／Acknowledged 以外的案件丟
    /// <see cref="InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen"/>；角色從 UserRoles 解析，
    /// 非 InventoryManager／SuperAdmin 丟 <see cref="DoSelect.Application.Common.DomainProblemException"/>（403）。
    /// 所有驗證都在第一個資料寫入之前完成。
    /// </summary>
    Task DismissAsync(
        Guid casePublicId,
        ReconciliationCaseResolutionCommand command,
        string adminUserId,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken);

    /// <summary>
    /// 以帳本重算值修正 Balance（高風險的人工庫存調整）。在同一個 SQL transaction 裡：先重驗 live Balance
    /// 仍等於案件的 Expected*、帳本重算仍等於 Actual*（偵測後合法的進貨／出貨／保留會讓快照過期，套用會
    /// 把那筆變更抹掉——丟 <see cref="InventoryWriteException.ErrorCodes.ConcurrencyConflict"/>），重算後
    /// Reserved &gt; OnHand 丟 <see cref="InventoryWriteException.ErrorCodes.ReconciliationLedgerInconsistent"/>
    /// （不是重送能修的，案件留著人工調查）；通過後第一次 SaveChanges 寫 Balance＋零差額
    /// <see cref="DoSelect.Domain.Inventory.InventoryMovementTypes.Adjustment"/> Movement（要拿 identity），
    /// 第二次寫案件狀態＋<c>inventory_reconciliation.resolve</c> 稽核，任一步失敗全部 rollback（裁定 F1）。
    /// reasonCode 限 <see cref="DoSelect.Domain.Inventory.InventoryReconciliationReasonCodes.ForResolve"/>；
    /// 其餘規則同 <see cref="DismissAsync"/>。
    /// </summary>
    Task ResolveAsync(
        Guid casePublicId,
        ReconciliationCaseResolutionCommand command,
        string adminUserId,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken);
}
