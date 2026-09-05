using System.Data.Common;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Inventory;

[Collection(nameof(InventoryReservationServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfInventoryReconciliationServiceTests
{
    private static readonly AuditRequestContext TestAuditContext =
        new("reconciliation-test-correlation", "0123456789abcdef0123456789abcdef", null);

    private readonly InventoryReservationServiceFixture _fixture;

    public EfInventoryReconciliationServiceTests(InventoryReservationServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DetectDiscrepanciesAsync_WhenBalanceHasNoMatchingMovementTrail_OpensACase()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        // Seeded directly with no InventoryMovement rows, so the ledger sums to 0 while Balance
        // says 8 — a genuine drift the daily job is meant to catch.
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var service = CreateService(context);

        // Other tests in this shared-database collection seed their own SKUs without a matching
        // Movement trail too, so `opened` reflects however many SKUs are currently drifted
        // database-wide, not just this test's one SKU — assert on this SKU's own case instead of
        // the global count.
        var opened = await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.True(opened >= 1);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Open, reconciliationCase.Status);
        Assert.Equal(8, reconciliationCase.ExpectedOnHand);
        Assert.Equal(0, reconciliationCase.ActualOnHand);
    }

    [Fact]
    public async Task DetectDiscrepanciesAsync_WhenAnOpenCaseAlreadyExistsForTheSku_DoesNotDuplicate()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var service = CreateService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);

        var openedAgain = await service.DetectDiscrepanciesAsync(DateTime.UtcNow.AddMinutes(1), CancellationToken.None);

        Assert.Equal(0, openedAgain);
        var cases = await context.InventoryReconciliationCases.AsNoTracking().Where(c => c.SkuId == sku.Id).ToListAsync();
        Assert.Single(cases);
    }

    [Fact]
    public async Task DetectDiscrepanciesAsync_WhenACaseIsAcknowledgedNotResolved_DoesNotOpenASecondCase()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        await service.AcknowledgeAsync(
            reconciliationCase.PublicId, adminUserId, reconciliationCase.RowVersion, DateTime.UtcNow, CancellationToken.None);

        var openedAgain = await service.DetectDiscrepanciesAsync(DateTime.UtcNow.AddMinutes(1), CancellationToken.None);

        Assert.Equal(0, openedAgain);
        var cases = await context.InventoryReconciliationCases.AsNoTracking().Where(c => c.SkuId == sku.Id).ToListAsync();
        Assert.Single(cases);
        Assert.Equal(InventoryReconciliationStatus.Acknowledged, cases[0].Status);
    }

    // ---------------------------------------------------------------------------------------
    // dismiss（組長對帳裁定 C1／D1／E1／F1／H1）
    // ---------------------------------------------------------------------------------------

    /// <summary>驗收 H1：dismiss 不動 Balance、不建 Movement；案件狀態與稽核同一次 SaveChanges。</summary>
    [Fact]
    public async Task DismissAsync_ClosesTheCaseWithoutTouchingBalanceAndAuditsItInTheSameSave()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);

        await service.DismissAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("false_positive", "  盤點基準用錯批號  ", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None);

        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var dismissed = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);
        Assert.Equal(InventoryReconciliationStatus.Dismissed, dismissed.Status);
        Assert.Null(dismissed.ResolutionMovementId);
        Assert.Equal("盤點基準用錯批號", dismissed.ResolutionReason);
        Assert.Equal(adminUserId, dismissed.ResolvedByAdminUserId);
        var balance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(8, balance.OnHandQuantity);
        Assert.False(await verify.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));

        var audit = await AuditAsync(verify, reconciliationCase.PublicId);
        Assert.Equal(AuditActions.InventoryReconciliationDismiss, audit.Action);
        Assert.Equal(AuditResourceTypes.InventoryReconciliationCase, audit.ResourceType);
        Assert.Equal("false_positive", audit.Reason);
        Assert.Contains(AuditRoleNames.InventoryManager, audit.ActorRolesJson);
        Assert.Equal(TestAuditContext.CorrelationId, audit.CorrelationId);
        var changes = Changes(audit);
        Assert.Equal(("Open", "Dismissed"), changes["status"]);
        Assert.Equal((null, "false_positive"), changes["reasonCode"]);
        Assert.Equal((null, sku.PublicId.ToString("D")), changes["skuPublicId"]);
        Assert.DoesNotContain("onHandQuantity", changes.Keys);
        Assert.DoesNotContain("resolutionMovementPublicId", changes.Keys);
        Assert.Contains("盤點基準用錯批號", audit.ChangedFieldsJson);
        // 稽核不記 Identity 內部 ID（裁定 E1）。
        Assert.DoesNotContain(adminUserId, audit.ChangedFieldsJson);
    }

    /// <summary>Acknowledged 的案件也能結案（裁定 C1：Open／Acknowledged 可 dismiss 或 resolve）。</summary>
    [Fact]
    public async Task DismissAsync_OnAnAcknowledgedCase_RecordsAcknowledgedAsTheBeforeStatus()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        await service.AcknowledgeAsync(
            reconciliationCase.PublicId, adminUserId, reconciliationCase.RowVersion, DateTime.UtcNow, CancellationToken.None);
        var acknowledged = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);

        await service.DismissAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("system_error", "偵測那天排程重複執行", acknowledged.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None);

        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var dismissed = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);
        Assert.Equal(InventoryReconciliationStatus.Dismissed, dismissed.Status);
        Assert.Equal(("Acknowledged", "Dismissed"), Changes(await AuditAsync(verify, reconciliationCase.PublicId))["status"]);
    }

    /// <summary>驗收 H1：稽核 INSERT 失敗，dismiss 整筆 rollback，案件維持 Open。</summary>
    [Fact]
    public async Task DismissAsync_WhenTheAuditInsertFails_LeavesTheCaseOpen()
    {
        await using var seedContext = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(seedContext, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(seedContext);
        var reconciliationCase = await DetectAsync(CreateService(seedContext), seedContext, sku.Id);

        var interceptor = new ThrowOnTableInsertInterceptor("[AuditLogs]");
        await using var context = InventoryReservationServiceFixture.CreateContext(interceptor);
        var service = CreateService(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.DismissAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("false_positive", "n/a", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None));
        Assert.True(interceptor.Engaged);

        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var stillOpen = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.Equal(reconciliationCase.RowVersion, stillOpen.RowVersion);
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    // ---------------------------------------------------------------------------------------
    // resolve
    // ---------------------------------------------------------------------------------------

    /// <summary>驗收 H1：resolve 只在兩份 snapshot 仍一致時修正 Balance，並建立零差額 Adjustment；稽核記 Expected→Actual 與 Movement。</summary>
    [Fact]
    public async Task ResolveAsync_CreatesCorrectiveMovementAppliesActualToBalanceAndAuditsTheCorrection()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);

        await service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "實點確認為 0", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None);

        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var resolved = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);
        Assert.Equal(InventoryReconciliationStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolutionMovementId);
        Assert.Equal("實點確認為 0", resolved.ResolutionReason);
        var balance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.OnHandQuantity);
        var movement = await verify.InventoryMovements.AsNoTracking().SingleAsync(m => m.Id == resolved.ResolutionMovementId);
        Assert.Equal(InventoryMovementTypes.Adjustment, movement.MovementType);
        Assert.Equal("reconciliation_correction", movement.ReasonCode);
        Assert.Equal(adminUserId, movement.ActorUserId);
        // Movement/Reservation is the ledger source of truth (庫存規則.md) — the resolution movement
        // must NOT change the ledger's own sum (onHandDelta must be 0), otherwise the next
        // DetectDiscrepanciesAsync run recomputes Actual* including this very correction and
        // immediately reopens a new case for the same SKU (組長 PR #36 round-2 review).
        Assert.Equal(0, movement.OnHandDelta);
        Assert.Equal(0, movement.ReservedDelta);

        var audit = await AuditAsync(verify, reconciliationCase.PublicId);
        Assert.Equal(AuditActions.InventoryReconciliationResolve, audit.Action);
        Assert.Equal(AuditResourceTypes.InventoryReconciliationCase, audit.ResourceType);
        Assert.Equal("count_verified", audit.Reason);
        Assert.Contains(AuditRoleNames.InventoryManager, audit.ActorRolesJson);
        var changes = Changes(audit);
        Assert.Equal(("Open", "Resolved"), changes["status"]);
        Assert.Equal((null, "count_verified"), changes["reasonCode"]);
        Assert.Equal((null, sku.PublicId.ToString("D")), changes["skuPublicId"]);
        Assert.Equal(("8", "0"), changes["onHandQuantity"]);
        Assert.Equal(("0", "0"), changes["reservedQuantity"]);
        Assert.Equal((null, movement.PublicId.ToString("D")), changes["resolutionMovementPublicId"]);
        Assert.Contains("實點確認為 0", audit.ChangedFieldsJson);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotReopenACaseOnTheNextDetectRun()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        // Balance=10, ledger sums to 0 — the exact scenario 組長 flagged: Resolve must not leave the
        // ledger in a state where the very next Detect run recomputes a different Actual* and
        // reopens a case for the same SKU.
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);

        await service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "n/a", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None);
        var reopened = await service.DetectDiscrepanciesAsync(DateTime.UtcNow.AddMinutes(1), CancellationToken.None);

        Assert.Equal(0, reopened);
        var cases = await context.InventoryReconciliationCases.AsNoTracking().Where(c => c.SkuId == sku.Id).ToListAsync();
        Assert.Single(cases);
        Assert.Equal(InventoryReconciliationStatus.Resolved, cases[0].Status);
    }

    /// <summary>驗收 H1：RowVersion 重送不重複處理——第一次成功後案件已 Resolved，舊 RowVersion 再送被拒且沒有第二筆 Movement。</summary>
    [Fact]
    public async Task ResolveAsync_WhenResentWithTheSameRowVersion_IsRejectedWithoutASecondCorrection()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        var command = new ReconciliationCaseResolutionCommand("count_verified", "n/a", reconciliationCase.RowVersion);
        await service.ResolveAsync(reconciliationCase.PublicId, command, adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var retryContext = InventoryReservationServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => CreateService(retryContext).ResolveAsync(
            reconciliationCase.PublicId, command, adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen, exception.ErrorCode);
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        Assert.Equal(1, await verify.InventoryMovements.AsNoTracking().CountAsync(m => m.SkuId == sku.Id));
        Assert.Equal(1, await verify.AuditLogs.AsNoTracking().CountAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    [Fact]
    public async Task ResolveAsync_WhenRowVersionIsStale_RollsBackTheBalanceCorrectionToo()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        var staleRowVersion = reconciliationCase.RowVersion;
        // Acknowledge first so the case's real RowVersion has moved on, making staleRowVersion
        // genuinely stale for the Resolve call below (single-transaction rollback needs a real
        // conflict on the *second* SaveChanges, not the first).
        await service.AcknowledgeAsync(reconciliationCase.PublicId, adminUserId, staleRowVersion, DateTime.UtcNow, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "n/a", staleRowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
        // The Balance correction from earlier in the same ResolveAsync call must have rolled back
        // too — not just the case's own status update.
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var balance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(8, balance.OnHandQuantity);
        var stillAcknowledged = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Acknowledged, stillAcknowledged.Status);
        Assert.False(await verify.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    /// <summary>
    /// Regression test: a legitimate StockIn between Detect and Resolve used to be silently erased
    /// because Resolve blindly applied the case's detect-time Actual* to the live Balance (組長 PR
    /// #36 round-4 review, item 1).
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenBalanceChangedSinceDetection_ThrowsConcurrencyConflictAndLeavesTheCaseOpen()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        // Balance=10, ledger sums to 0 at Detect time.
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);

        // A legitimate stock-in after detection: Balance moves to 12, independent of the case.
        var balance = await context.InventoryBalances.SingleAsync(b => b.SkuId == sku.Id);
        balance.ApplyQuantities(12, balance.ReservedQuantity, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "n/a", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
        // The legitimate stock-in must not have been overwritten by the stale target (0), and the
        // case must stay open for re-detection rather than being marked Resolved against stale data.
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var unchanged = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(12, unchanged.OnHandQuantity);
        var stillOpen = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.False(await verify.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    /// <summary>驗收 H1（ledger 競態）：偵測後帳本多了一筆 Movement，重算值不再等於案件的 Actual*，不能套用過期快照。</summary>
    [Fact]
    public async Task ResolveAsync_WhenLedgerChangedSinceDetection_ThrowsConcurrencyConflictAndLeavesTheCaseOpen()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);

        // A stock-in written to the ledger only (Balance untouched): ledger now sums to 3, case says 0.
        context.InventoryMovements.Add(new InventoryMovement(
            Guid.CreateVersion7(), sku.Id, reservationId: null, InventoryMovementTypes.StockIn,
            onHandDelta: 3, reservedDelta: 0, beforeOnHand: 0, afterOnHand: 3, beforeReserved: 0, afterReserved: 0,
            unitCostSnapshot: 600m, reasonCode: "purchase_received", referenceType: "Test", Guid.CreateVersion7(),
            adminUserId, DateTime.UtcNow));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "n/a", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var unchanged = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(10, unchanged.OnHandQuantity);
        var stillOpen = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.Equal(1, await verify.InventoryMovements.AsNoTracking().CountAsync(m => m.SkuId == sku.Id));
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    /// <summary>
    /// Regression test: a case whose recomputed ledger has Reserved &gt; OnHand used to make Resolve
    /// throw an unmapped ArgumentOutOfRangeException (500) from InventoryBalance.ApplyQuantities
    /// (組長 PR #36 round-4 review, item 2). 對帳裁定 G1 再把它從 concurrency_conflict 拆成專用碼：
    /// 重新整理／重送修不好不一致的帳本，案件要留著人工調查，而且零副作用。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenRecomputedLedgerHasReservedExceedingOnHand_ThrowsLedgerInconsistentWithoutSideEffects()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var adminUserId = await SeedInventoryManagerAsync(context);

        // An incomplete ledger: an Active reservation for 2 units exists, but there is no matching
        // InventoryMovement trail recording any on-hand stock at all — DetectDiscrepanciesAsync's
        // recomputation then yields ActualOnHand=0, ActualReserved=2, an illegal combination.
        var order = await _fixture.SeedOrderAsync(context);
        context.InventoryReservations.Add(new InventoryReservation(
            Guid.CreateVersion7(), sku.Id, order, 2, DateTime.UtcNow.AddMinutes(15), DateTime.UtcNow));
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        Assert.Equal(0, reconciliationCase.ActualOnHand);
        Assert.Equal(2, reconciliationCase.ActualReserved);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "n/a", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ReconciliationLedgerInconsistent, exception.ErrorCode);
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var unchangedBalance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(5, unchangedBalance.OnHandQuantity);
        Assert.Equal(0, unchangedBalance.ReservedQuantity);
        var stillOpen = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.False(await verify.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    /// <summary>驗收 H1：稽核 INSERT 失敗，resolve 的 Balance 修正、Movement 與案件狀態全部 rollback（同一個 SQL transaction）。</summary>
    [Fact]
    public async Task ResolveAsync_WhenTheAuditInsertFails_RollsBackTheBalanceCorrectionAndTheMovement()
    {
        await using var seedContext = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(seedContext, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(seedContext);
        var reconciliationCase = await DetectAsync(CreateService(seedContext), seedContext, sku.Id);

        var interceptor = new ThrowOnTableInsertInterceptor("[AuditLogs]");
        await using var context = InventoryReservationServiceFixture.CreateContext(interceptor);
        var service = CreateService(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "n/a", reconciliationCase.RowVersion),
            adminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None));
        Assert.True(interceptor.Engaged);

        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var balance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(8, balance.OnHandQuantity);
        Assert.False(await verify.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
        var stillOpen = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    // ---------------------------------------------------------------------------------------
    // 兩條共用的驗證、權限與狀態規則
    // ---------------------------------------------------------------------------------------

    /// <summary>裁定 D1：白名單依動作分開；note 必填且 ≤ 500。任何一項不合法都在第一個寫入前擋下，零副作用。</summary>
    [Theory]
    [InlineData(true, "count_verified", "n/a")]
    [InlineData(false, "false_positive", "n/a")]
    [InlineData(true, "", "n/a")]
    [InlineData(false, "not_a_code", "n/a")]
    [InlineData(true, "false_positive", "   ")]
    [InlineData(false, "count_verified", null)]
    [InlineData(false, "count_verified", "too-long")]
    public async Task DismissOrResolve_WhenReasonCodeOrNoteIsInvalid_ThrowsValidationFailedWithoutSideEffects(
        bool dismiss, string reasonCode, string? note)
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        var command = new ReconciliationCaseResolutionCommand(
            reasonCode, note == "too-long" ? new string('長', ReconciliationCaseResolutionCommand.NoteMaxLength + 1) : note!, reconciliationCase.RowVersion);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => dismiss
            ? service.DismissAsync(reconciliationCase.PublicId, command, adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None)
            : service.ResolveAsync(reconciliationCase.PublicId, command, adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        await AssertUntouchedAsync(sku.Id, reconciliationCase.PublicId, expectedOnHand: 8);
    }

    /// <summary>裁定 B1：角色從 UserRoles 解析；沒有 InventoryManager／SuperAdmin 的管理員被拒（403），零副作用。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DismissOrResolve_WhenAdminLacksInventoryManagerRole_ThrowsForbiddenWithoutSideEffects(bool dismiss)
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var unrelatedAdmin = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        var command = new ReconciliationCaseResolutionCommand(dismiss ? "false_positive" : "count_verified", "n/a", reconciliationCase.RowVersion);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => dismiss
            ? service.DismissAsync(reconciliationCase.PublicId, command, unrelatedAdmin, TestAuditContext, DateTime.UtcNow, CancellationToken.None)
            : service.ResolveAsync(reconciliationCase.PublicId, command, unrelatedAdmin, TestAuditContext, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
        await AssertUntouchedAsync(sku.Id, reconciliationCase.PublicId, expectedOnHand: 8);
    }

    /// <summary>裁定 C1：Resolved／Dismissed 不可再次處理——不論用哪個動作、帶哪個 RowVersion。</summary>
    [Fact]
    public async Task DismissOrResolve_OnAClosedCase_ThrowsCaseNotOpen()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        await service.DismissAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("false_positive", "n/a", reconciliationCase.RowVersion),
            adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);
        var dismissed = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);

        var resolveAgain = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("count_verified", "n/a", dismissed.RowVersion),
            adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));
        var dismissAgain = await Assert.ThrowsAsync<InventoryWriteException>(() => service.DismissAsync(
            reconciliationCase.PublicId,
            new ReconciliationCaseResolutionCommand("other", "n/a", dismissed.RowVersion),
            adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen, resolveAgain.ErrorCode);
        Assert.Equal(InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen, dismissAgain.ErrorCode);
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        Assert.False(await verify.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
        Assert.Equal(1, await verify.AuditLogs.AsNoTracking().CountAsync(a => a.ResourcePublicId == reconciliationCase.PublicId));
    }

    [Fact]
    public async Task DismissAsync_WhenTheCaseDoesNotExist_ThrowsResourceNotFound()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var adminUserId = await SeedInventoryManagerAsync(context);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => CreateService(context).DismissAsync(
            Guid.NewGuid(),
            new ReconciliationCaseResolutionCommand("false_positive", "n/a", new byte[8]),
            adminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    // ---------------------------------------------------------------------------------------
    // list
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ListCasesAsync_ReturnsActorSummariesWithMaskedEmailInsteadOfIdentityIds()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await SeedInventoryManagerAsync(context);
        var admin = await context.Users.AsNoTracking().SingleAsync(u => u.Id == adminUserId);
        var service = CreateService(context);
        var reconciliationCase = await DetectAsync(service, context, sku.Id);
        await service.AcknowledgeAsync(
            reconciliationCase.PublicId, adminUserId, reconciliationCase.RowVersion, DateTime.UtcNow, CancellationToken.None);

        var page = await service.ListCasesAsync(new InventoryReconciliationCaseQuery(null, 1, 20), CancellationToken.None);

        var adminEmail = admin.Email!;
        var dto = page.Items.Single(item => item.Sku.PublicId == sku.PublicId);
        Assert.NotNull(dto.AcknowledgedBy);
        Assert.Equal(admin.PublicId, dto.AcknowledgedBy!.PublicId);
        Assert.NotEqual(adminEmail, dto.AcknowledgedBy.Email);
        Assert.EndsWith(adminEmail[adminEmail.IndexOf('@')..], dto.AcknowledgedBy.Email!);
    }

    [Fact]
    public async Task ListCasesAsync_WhenStatusIsInvalid_ThrowsValidationFailed()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var service = CreateService(context);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ListCasesAsync(
            new InventoryReconciliationCaseQuery("not-a-real-status", 1, 20), CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task ListCasesAsync_WhenStatusIsAnUndefinedNumber_ThrowsValidationFailed()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var service = CreateService(context);

        // Enum.TryParse accepts any numeric string convertible to the enum's underlying type even
        // when it names no defined member — "999" parses "successfully" without Enum.IsDefined.
        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ListCasesAsync(
            new InventoryReconciliationCaseQuery("999", 1, 20), CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task ListCasesAsync_WhenPageNumberIsExtreme_DoesNotThrowAndReturnsAnEmptyPage()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var service = CreateService(context);

        var page = await service.ListCasesAsync(
            new InventoryReconciliationCaseQuery(null, int.MaxValue / 100, 200), CancellationToken.None);

        Assert.Empty(page.Items);
    }

    // ---------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------

    private static EfInventoryReconciliationService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));

    private static Task<string> SeedInventoryManagerAsync(DoSelectDbContext context) =>
        InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.InventoryManager);

    private static async Task<InventoryReconciliationCase> DetectAsync(
        EfInventoryReconciliationService service, DoSelectDbContext context, long skuId)
    {
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        return await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == skuId);
    }

    private static async Task AssertUntouchedAsync(long skuId, Guid casePublicId, int expectedOnHand)
    {
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var balance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == skuId);
        Assert.Equal(expectedOnHand, balance.OnHandQuantity);
        var stillOpen = await verify.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == casePublicId);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.Null(stillOpen.ResolutionReason);
        Assert.False(await verify.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == skuId));
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == casePublicId));
    }

    private static async Task<AuditLog> AuditAsync(DoSelectDbContext context, Guid casePublicId) =>
        await context.AuditLogs.AsNoTracking().SingleAsync(a => a.ResourcePublicId == casePublicId);

    private static Dictionary<string, (string? Before, string? After)> Changes(AuditLog audit)
    {
        using var envelope = JsonDocument.Parse(audit.ChangedFieldsJson);
        return envelope.RootElement.GetProperty("changes").EnumerateArray()
            .ToDictionary(
                change => change.GetProperty("field").GetString()!,
                change => (change.GetProperty("beforeCode").GetString(), change.GetProperty("afterCode").GetString()));
    }

    private sealed class ThrowOnTableInsertInterceptor(string table) : DbCommandInterceptor
    {
        public bool Engaged { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfInsert(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfInsert(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void ThrowIfInsert(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(table, StringComparison.OrdinalIgnoreCase))
            {
                Engaged = true;
                throw new InvalidOperationException($"Injected {table} insert failure.");
            }
        }
    }
}
