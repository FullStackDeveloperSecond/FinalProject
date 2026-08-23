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
        Assert.Equal(InventoryMovementTypes.ManualDecrease, movement.MovementType);
        Assert.Equal(-8, movement.OnHandDelta);
    }
}
