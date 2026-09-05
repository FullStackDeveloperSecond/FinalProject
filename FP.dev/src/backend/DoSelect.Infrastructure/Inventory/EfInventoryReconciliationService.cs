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

    /// <summary>零差額修正 Movement 的 ReasonCode，固定值（組長對帳裁定 D1）。</summary>
    private const string CorrectionMovementReasonCode = "reconciliation_correction";

    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;

    public EfInventoryReconciliationService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
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

    public Task DismissAsync(
        Guid casePublicId,
        ReconciliationCaseResolutionCommand command,
        string adminUserId,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken) =>
        CloseAsync(casePublicId, command, adminUserId, auditContext, now, dismissed: true, cancellationToken);

    public Task ResolveAsync(
        Guid casePublicId,
        ReconciliationCaseResolutionCommand command,
        string adminUserId,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken) =>
        CloseAsync(casePublicId, command, adminUserId, auditContext, now, dismissed: false, cancellationToken);

    /// <summary>
    /// dismiss 與 resolve 共用的實作（組長對帳裁定 C1：兩條路由，內部沿用同一套演算法）。順序是裁定 F1
    /// 的要求：reasonCode／note／角色／案件狀態／稽核 request 全部在第一個資料寫入之前驗完，之後才開
    /// SQL transaction。
    /// </summary>
    private async Task CloseAsync(
        Guid casePublicId,
        ReconciliationCaseResolutionCommand command,
        string adminUserId,
        AuditRequestContext auditContext,
        DateTime now,
        bool dismissed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(auditContext);

        var reasonCode = RequireReasonCode(command.ReasonCode, dismissed);
        var note = RequireNote(command.Note);
        var actor = await ResolveActorAsync(adminUserId, cancellationToken);

        var reconciliationCase = await FindByPublicIdAsync(casePublicId, cancellationToken);
        if (reconciliationCase.Status is not (InventoryReconciliationStatus.Open or InventoryReconciliationStatus.Acknowledged))
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen,
                $"A {reconciliationCase.Status} reconciliation case cannot be processed again.");
        }

        var skuPublicId = await _dbContext.Skus
            .Where(sku => sku.Id == reconciliationCase.SkuId)
            .Select(sku => sku.PublicId)
            .SingleAsync(cancellationToken);

        // Movement 的 PublicId 在 client 端產生，所以 resolve 的稽核可以在任何寫入之前就組好——稽核
        // request 的驗證（note 字元／長度、代碼安全規則）也就一起落在第一個寫入之前。
        var movementPublicId = dismissed ? (Guid?)null : Guid.CreateVersion7();
        var auditRequest = BuildAuditRequest(
            actor, reconciliationCase, skuPublicId, reasonCode, note, movementPublicId, auditContext, dismissed);

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
            long? resolutionMovementId = null;
            if (!dismissed)
            {
                var movement = await ApplyLedgerCorrectionAsync(reconciliationCase, movementPublicId!.Value, adminUserId, now, cancellationToken);
                resolutionMovementId = movement.Id;
            }

            // 案件狀態與稽核同一次 SaveChanges（裁定 F1）：稽核寫不進去，結案就不成立。
            _dbContext.Entry(reconciliationCase).Property(candidate => candidate.RowVersion).OriginalValue = command.RowVersion;
            reconciliationCase.Resolve(adminUserId, resolutionMovementId, note, dismissed, now);
            _auditWriter.Add(auditRequest);
            await _dbContext.SaveChangesAsync(cancellationToken);

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

    /// <summary>
    /// resolve 的第一次 SaveChanges：重驗兩份快照後修正 Balance 並寫零差額 Movement。呼叫端已在同一個
    /// transaction 裡，這裡丟出的任何例外都會讓整筆 rollback。
    /// </summary>
    private async Task<InventoryMovement> ApplyLedgerCorrectionAsync(
        InventoryReconciliationCase reconciliationCase,
        Guid movementPublicId,
        string adminUserId,
        DateTime now,
        CancellationToken cancellationToken)
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
        // 這不是重新整理／重送能修好的過期，所以用專用碼，不當 concurrency_conflict（對帳裁定 G1）。
        if (actualReserved > actualOnHand)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ReconciliationLedgerInconsistent,
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
            movementPublicId,
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
            reasonCode: CorrectionMovementReasonCode,
            referenceType: "InventoryReconciliationCase",
            reconciliationCase.PublicId,
            adminUserId,
            now);
        _dbContext.InventoryMovements.Add(movement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return movement;
    }

    private static AuditWriteRequest BuildAuditRequest(
        AuditActor actor,
        InventoryReconciliationCase reconciliationCase,
        Guid skuPublicId,
        string reasonCode,
        string note,
        Guid? movementPublicId,
        AuditRequestContext auditContext,
        bool dismissed)
    {
        var targetStatus = dismissed ? InventoryReconciliationStatus.Dismissed : InventoryReconciliationStatus.Resolved;
        var changes = new List<AuditFieldChange>
        {
            AuditFieldChange.Code("status", reconciliationCase.Status.ToString(), targetStatus.ToString()),
            AuditFieldChange.Code("reasonCode", null, reasonCode),
            AuditFieldChange.Code("skuPublicId", null, skuPublicId.ToString("D")),
        };
        if (!dismissed)
        {
            // 數量記案件偵測時的 Expected→Actual（裁定 E1）；resolve 剛在同一交易裡驗過 live 值仍等於這組快照。
            changes.Add(AuditFieldChange.Code("onHandQuantity", Number(reconciliationCase.ExpectedOnHand), Number(reconciliationCase.ActualOnHand)));
            changes.Add(AuditFieldChange.Code("reservedQuantity", Number(reconciliationCase.ExpectedReserved), Number(reconciliationCase.ActualReserved)));
            changes.Add(AuditFieldChange.Code("resolutionMovementPublicId", null, movementPublicId!.Value.ToString("D")));
        }

        try
        {
            return AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                dismissed ? AuditActions.InventoryReconciliationDismiss : AuditActions.InventoryReconciliationResolve,
                AuditResourceTypes.InventoryReconciliationCase,
                reconciliationCase.PublicId,
                AuditResult.Success,
                errorCode: null,
                changes,
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
                $"The note is not accepted by the audit log: {exception.Message}");
        }
    }

    private static string RequireReasonCode(string? reasonCode, bool dismissed)
    {
        var whitelist = dismissed
            ? InventoryReconciliationReasonCodes.ForDismiss
            : InventoryReconciliationReasonCodes.ForResolve;
        if (string.IsNullOrWhiteSpace(reasonCode) || !whitelist.Contains(reasonCode, StringComparer.Ordinal))
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed,
                $"reasonCode must be one of: {string.Join(", ", whitelist)}.");
        }

        return reasonCode;
    }

    private static string RequireNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed,
                "A note explaining the resolution is required.");
        }

        if (trimmed.Length > ReconciliationCaseResolutionCommand.NoteMaxLength)
        {
            throw new InventoryWriteException(
                InventoryWriteException.ErrorCodes.ValidationFailed,
                $"note must be at most {ReconciliationCaseResolutionCommand.NoteMaxLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// 與 EfInventoryImportService.ResolveActorAsync 同形：稽核的角色快照要從真正的 UserRoles 讀，
    /// 不信任呼叫端傳來的任何角色字串。對帳的 Policy 沿用 InventoryManager／SuperAdmin（裁定 B1）。
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
            throw DomainProblemException.Forbidden("The administrator is not allowed to close inventory reconciliation cases.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

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
