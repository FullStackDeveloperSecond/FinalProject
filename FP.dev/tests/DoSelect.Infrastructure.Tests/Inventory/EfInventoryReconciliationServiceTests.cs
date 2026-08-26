using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Inventory;

[Collection(nameof(InventoryReservationServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfInventoryReconciliationServiceTests
{
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
        var service = new EfInventoryReconciliationService(context);

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
        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);

        var openedAgain = await service.DetectDiscrepanciesAsync(DateTime.UtcNow.AddMinutes(1), CancellationToken.None);

        Assert.Equal(0, openedAgain);
        var cases = await context.InventoryReconciliationCases.AsNoTracking().Where(c => c.SkuId == sku.Id).ToListAsync();
        Assert.Single(cases);
    }

    [Fact]
    public async Task ResolveAsync_WhenDismissed_ClosesTheCaseWithoutTouchingBalance()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);

        await service.ResolveAsync(
            reconciliationCase.PublicId, adminUserId,
            new ResolveReconciliationCaseRequest(Dismissed: true, Reason: "count basis was wrong", reconciliationCase.RowVersion),
            DateTime.UtcNow, CancellationToken.None);

        var resolved = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);
        Assert.Equal(InventoryReconciliationStatus.Dismissed, resolved.Status);
        Assert.Null(resolved.ResolutionMovementId);
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(8, balance.OnHandQuantity);
    }

    [Fact]
    public async Task ResolveAsync_WhenNotDismissed_CreatesCorrectiveMovementAndAppliesActualToBalance()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);

        await service.ResolveAsync(
            reconciliationCase.PublicId, adminUserId,
            new ResolveReconciliationCaseRequest(Dismissed: false, Reason: null, reconciliationCase.RowVersion),
            DateTime.UtcNow, CancellationToken.None);

        var resolved = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.PublicId == reconciliationCase.PublicId);
        Assert.Equal(InventoryReconciliationStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolutionMovementId);
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.OnHandQuantity);
        var movement = await context.InventoryMovements.AsNoTracking().SingleAsync(m => m.Id == resolved.ResolutionMovementId);
        Assert.Equal(InventoryMovementTypes.Adjustment, movement.MovementType);
        // Movement/Reservation is the ledger source of truth (庫存規則.md) — the resolution movement
        // must NOT change the ledger's own sum (onHandDelta must be 0), otherwise the next
        // DetectDiscrepanciesAsync run recomputes Actual* including this very correction and
        // immediately reopens a new case for the same SKU (組長 PR #36 round-2 review).
        Assert.Equal(0, movement.OnHandDelta);
        Assert.Equal(0, movement.ReservedDelta);
    }

    [Fact]
    public async Task ResolveAsync_WhenNotDismissed_DoesNotReopenACaseOnTheNextDetectRun()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        // Balance=10, ledger sums to 8 — the exact scenario 組長 flagged: Resolve must not leave the
        // ledger in a state where the very next Detect run recomputes a different Actual* and
        // reopens a case for the same SKU.
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);

        await service.ResolveAsync(
            reconciliationCase.PublicId, adminUserId,
            new ResolveReconciliationCaseRequest(Dismissed: false, Reason: null, reconciliationCase.RowVersion),
            DateTime.UtcNow, CancellationToken.None);
        var reopened = await service.DetectDiscrepanciesAsync(DateTime.UtcNow.AddMinutes(1), CancellationToken.None);

        Assert.Equal(0, reopened);
        var cases = await context.InventoryReconciliationCases.AsNoTracking().Where(c => c.SkuId == sku.Id).ToListAsync();
        Assert.Single(cases);
        Assert.Equal(InventoryReconciliationStatus.Resolved, cases[0].Status);
    }

    [Fact]
    public async Task ResolveAsync_WhenRowVersionIsStale_RollsBackTheBalanceCorrectionToo()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        var staleRowVersion = reconciliationCase.RowVersion;
        // Acknowledge first so the case's real RowVersion has moved on, making staleRowVersion
        // genuinely stale for the Resolve call below (single-transaction rollback needs a real
        // conflict on the *second* SaveChanges, not the first).
        await service.AcknowledgeAsync(reconciliationCase.PublicId, adminUserId, staleRowVersion, DateTime.UtcNow, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId, adminUserId,
            new ResolveReconciliationCaseRequest(Dismissed: false, Reason: null, staleRowVersion),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
        // The Balance correction from earlier in the same ResolveAsync call must have rolled back
        // too — not just the case's own status update.
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(8, balance.OnHandQuantity);
        var stillAcknowledged = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Acknowledged, stillAcknowledged.Status);
        Assert.False(await context.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
    }

    [Fact]
    public async Task DetectDiscrepanciesAsync_WhenACaseIsAcknowledgedNotResolved_DoesNotOpenASecondCase()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReconciliationService(context);
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

    /// <summary>
    /// Regression test: a legitimate StockIn between Detect and Resolve used to be silently erased
    /// because Resolve blindly applied the case's detect-time Actual* to the live Balance (組長 PR
    /// #36 round-4 review, item 1).
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenBalanceChangedSinceDetection_ThrowsConcurrencyConflictAndLeavesTheCaseOpen()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        // Balance=10, ledger sums to 8 at Detect time.
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);

        // A legitimate stock-in after detection: Balance moves to 12, independent of the case.
        var balance = await context.InventoryBalances.SingleAsync(b => b.SkuId == sku.Id);
        balance.ApplyQuantities(12, balance.ReservedQuantity, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId, adminUserId,
            new ResolveReconciliationCaseRequest(Dismissed: false, Reason: null, reconciliationCase.RowVersion),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
        // The legitimate stock-in must not have been overwritten by the stale target (8), and the
        // case must stay open for re-detection rather than being marked Resolved against stale data.
        var unchanged = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(12, unchanged.OnHandQuantity);
        var stillOpen = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.False(await context.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
    }

    /// <summary>
    /// Regression test: a case whose recomputed ledger has Reserved &gt; OnHand used to make Resolve
    /// throw an unmapped ArgumentOutOfRangeException (500) from InventoryBalance.ApplyQuantities,
    /// instead of a stable no-side-effect error (組長 PR #36 round-4 review, item 2).
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenRecomputedLedgerHasReservedExceedingOnHand_ThrowsConcurrencyConflictWithoutSideEffects()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);

        // An incomplete ledger: an Active reservation for 2 units exists, but there is no matching
        // InventoryMovement trail recording any on-hand stock at all — DetectDiscrepanciesAsync's
        // recomputation then yields ActualOnHand=0, ActualReserved=2, an illegal combination.
        var order = await _fixture.SeedOrderAsync(context);
        context.InventoryReservations.Add(new InventoryReservation(
            Guid.CreateVersion7(), sku.Id, order, 2, DateTime.UtcNow.AddMinutes(15), DateTime.UtcNow));
        await context.SaveChangesAsync();

        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(0, reconciliationCase.ActualOnHand);
        Assert.Equal(2, reconciliationCase.ActualReserved);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ResolveAsync(
            reconciliationCase.PublicId, adminUserId,
            new ResolveReconciliationCaseRequest(Dismissed: false, Reason: null, reconciliationCase.RowVersion),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
        var unchangedBalance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(5, unchangedBalance.OnHandQuantity);
        Assert.Equal(0, unchangedBalance.ReservedQuantity);
        var stillOpen = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
        Assert.Equal(InventoryReconciliationStatus.Open, stillOpen.Status);
        Assert.False(await context.InventoryMovements.AsNoTracking().AnyAsync(m => m.SkuId == sku.Id));
    }

    [Fact]
    public async Task ListCasesAsync_ReturnsActorSummariesWithMaskedEmailInsteadOfIdentityIds()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var admin = await context.Users.AsNoTracking().SingleAsync(u => u.Id == adminUserId);
        var service = new EfInventoryReconciliationService(context);
        await service.DetectDiscrepanciesAsync(DateTime.UtcNow, CancellationToken.None);
        var reconciliationCase = await context.InventoryReconciliationCases.AsNoTracking().SingleAsync(c => c.SkuId == sku.Id);
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
        var service = new EfInventoryReconciliationService(context);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ListCasesAsync(
            new InventoryReconciliationCaseQuery("not-a-real-status", 1, 20), CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task ListCasesAsync_WhenStatusIsAnUndefinedNumber_ThrowsValidationFailed()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var service = new EfInventoryReconciliationService(context);

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
        var service = new EfInventoryReconciliationService(context);

        var page = await service.ListCasesAsync(
            new InventoryReconciliationCaseQuery(null, int.MaxValue / 100, 200), CancellationToken.None);

        Assert.Empty(page.Items);
    }
}
