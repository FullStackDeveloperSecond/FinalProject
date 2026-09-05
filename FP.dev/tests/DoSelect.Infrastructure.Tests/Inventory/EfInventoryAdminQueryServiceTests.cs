using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Inventory;

/// <summary>
/// SQL Server-backed coverage for the admin movement filter. 組長 PR #36 ruling A1 made CostChange a
/// first-class movement type: the SKU cost-change flow writes it, the M-15 turnover report reads it,
/// and the unfiltered movement list already returns it — so `movementTypes=CostChange` has to filter
/// rather than be rejected as unknown.
/// </summary>
[Collection(nameof(InventoryReservationServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfInventoryAdminQueryServiceTests
{
    private readonly InventoryReservationServiceFixture _fixture;

    public EfInventoryAdminQueryServiceTests(InventoryReservationServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListMovementsAsync_WhenFilteringByCostChange_ReturnsOnlyTheCostChangeRows()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 8);
        var costChangePublicId = await SeedZeroDeltaMovementAsync(
            context, sku, InventoryMovementTypes.CostChange, "sku_unit_cost_changed");
        // A second row of a different type on the same SKU: a filter that silently ignored the
        // parameter would return both and fail here, so this cannot pass vacuously.
        await SeedZeroDeltaMovementAsync(
            context, sku, InventoryMovementTypes.Adjustment, "reconciliation_correction");

        var service = new EfInventoryAdminQueryService(context);
        var page = await service.ListMovementsAsync(
            new InventoryMovementQuery(sku.PublicId, [InventoryMovementTypes.CostChange], null, null),
            CancellationToken.None);

        var movement = Assert.Single(page.Items);
        Assert.Equal(costChangePublicId, movement.PublicId);
        Assert.Equal(InventoryMovementTypes.CostChange, movement.MovementType);
    }

    [Fact]
    public async Task ListMovementsAsync_WhenNotFiltering_StillReturnsCostChangeRows()
    {
        // The half of the contract that made the rejection inconsistent in the first place: these rows
        // are visible without a filter, which is why refusing them as a filter value was a defect.
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var costChangePublicId = await SeedZeroDeltaMovementAsync(
            context, sku, InventoryMovementTypes.CostChange, "sku_unit_cost_changed");

        var service = new EfInventoryAdminQueryService(context);
        var page = await service.ListMovementsAsync(
            new InventoryMovementQuery(sku.PublicId, null, null, null),
            CancellationToken.None);

        var movement = Assert.Single(page.Items);
        Assert.Equal(costChangePublicId, movement.PublicId);
        Assert.Equal(InventoryMovementTypes.CostChange, movement.MovementType);
    }

    private static async Task<Guid> SeedZeroDeltaMovementAsync(
        DoSelectDbContext context, Domain.Catalog.Sku sku, string movementType, string reasonCode)
    {
        var balance = await context.InventoryBalances.SingleAsync(candidate => candidate.SkuId == sku.Id);
        var publicId = Guid.CreateVersion7();

        context.InventoryMovements.Add(new InventoryMovement(
            publicId,
            sku.Id,
            reservationId: null,
            movementType,
            onHandDelta: 0,
            reservedDelta: 0,
            beforeOnHand: balance.OnHandQuantity,
            afterOnHand: balance.OnHandQuantity,
            beforeReserved: balance.ReservedQuantity,
            afterReserved: balance.ReservedQuantity,
            unitCostSnapshot: sku.UnitCost,
            reasonCode,
            referenceType: "Sku",
            referencePublicId: sku.PublicId,
            actorUserId: null,
            occurredAtUtc: DateTime.UtcNow));
        await context.SaveChangesAsync();

        return publicId;
    }
}
