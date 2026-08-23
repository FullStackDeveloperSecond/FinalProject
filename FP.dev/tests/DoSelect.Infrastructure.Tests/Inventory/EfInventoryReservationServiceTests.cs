using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Inventory;

[CollectionDefinition(nameof(InventoryReservationServiceCollection))]
public sealed class InventoryReservationServiceCollection : ICollectionFixture<InventoryReservationServiceFixture>;

[Collection(nameof(InventoryReservationServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfInventoryReservationServiceTests
{
    private readonly InventoryReservationServiceFixture _fixture;

    public EfInventoryReservationServiceTests(InventoryReservationServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReserveAsync_WhenStockIsSufficient_IncreasesReservedAndCreatesMovement()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context);

        await service.ReserveAsync(
            orderId, [new ReservationLine(sku.PublicId, 3)], DateTime.UtcNow.AddMinutes(15), DateTime.UtcNow, CancellationToken.None);

        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(3, balance.ReservedQuantity);
        Assert.Equal(7, balance.AvailableQuantity);

        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(3, reservation.Quantity);

        var movement = await context.InventoryMovements.AsNoTracking().SingleAsync(m => m.SkuId == sku.Id);
        Assert.Equal(InventoryMovementTypes.Reserve, movement.MovementType);
        Assert.Equal(3, movement.ReservedDelta);
        Assert.Equal(0, movement.OnHandDelta);
        Assert.Equal(reservation.Id, movement.ReservationId);
    }

    [Fact]
    public async Task ReserveAsync_WhenOneOfSeveralSkusIsInsufficient_ReservesNothing()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var plentifulSku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var scarceSku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 1);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReserveAsync(
            orderId,
            [new ReservationLine(plentifulSku.PublicId, 2), new ReservationLine(scarceSku.PublicId, 5)],
            null, DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(InventoryWriteException.ErrorCodes.InsufficientStock, exception.ErrorCode);

        var plentifulBalance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == plentifulSku.Id);
        Assert.Equal(0, plentifulBalance.ReservedQuantity);
        Assert.Empty(await context.InventoryReservations.AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync());
    }

    [Fact]
    public async Task ReserveAsync_WhenTwoConcurrentRequestsRaceForTheLastUnit_OnlyOneSucceeds()
    {
        await using var seedContext = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(seedContext, onHandQuantity: 1);
        var orderAId = await _fixture.SeedOrderAsync(seedContext);
        var orderBId = await _fixture.SeedOrderAsync(seedContext);

        await using var contextA = InventoryReservationServiceFixture.CreateContext();
        await using var contextB = InventoryReservationServiceFixture.CreateContext();
        var serviceA = new EfInventoryReservationService(contextA);
        var serviceB = new EfInventoryReservationService(contextB);
        var line = new ReservationLine(sku.PublicId, 1);
        var now = DateTime.UtcNow;

        var results = await Task.WhenAll(
            RunOrCaptureInsufficientStockAsync(serviceA, orderAId, line, now),
            RunOrCaptureInsufficientStockAsync(serviceB, orderBId, line, now));

        var succeeded = results.Count(errorCode => errorCode is null);
        var failedWithInsufficientStock = results.Count(errorCode => errorCode == InventoryWriteException.ErrorCodes.InsufficientStock);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, failedWithInsufficientStock);

        await using var verifyContext = InventoryReservationServiceFixture.CreateContext();
        var balance = await verifyContext.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(1, balance.ReservedQuantity);
        Assert.Equal(0, balance.AvailableQuantity);
        var activeReservations = await verifyContext.InventoryReservations.AsNoTracking()
            .Where(r => r.SkuId == sku.Id && r.Status == InventoryReservationStatus.Active)
            .ToListAsync();
        Assert.Single(activeReservations);
    }

    private static async Task<string?> RunOrCaptureInsufficientStockAsync(
        EfInventoryReservationService service, long orderId, ReservationLine line, DateTime now)
    {
        try
        {
            await service.ReserveAsync(orderId, [line], null, now, CancellationToken.None);
            return null;
        }
        catch (InventoryWriteException exception)
        {
            return exception.ErrorCode;
        }
    }

    [Fact]
    public async Task ReleaseAsync_WhenActive_RestoresAvailableAndMarksReleased()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReservationService(context);
        var now = DateTime.UtcNow;
        await service.ReserveAsync(orderId, [new ReservationLine(sku.PublicId, 2)], null, now, CancellationToken.None);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        await service.ReleaseAsync(
            reservation.PublicId, "member_cancelled", "customer requested", adminUserId, reservation.RowVersion,
            now.AddMinutes(1), CancellationToken.None);

        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(5, balance.AvailableQuantity);
        var updated = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservation.PublicId);
        Assert.Equal(InventoryReservationStatus.Released, updated.Status);
    }

    [Fact]
    public async Task ReleaseAsync_WhenAlreadyReleased_ThrowsReservationNotActive()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReservationService(context);
        var now = DateTime.UtcNow;
        await service.ReserveAsync(orderId, [new ReservationLine(sku.PublicId, 2)], null, now, CancellationToken.None);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        await service.ReleaseAsync(reservation.PublicId, "member_cancelled", "n/a", adminUserId, reservation.RowVersion, now, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReleaseAsync(
            reservation.PublicId, "member_cancelled", "n/a", adminUserId, reservation.RowVersion, now, CancellationToken.None));
        Assert.Equal(InventoryWriteException.ErrorCodes.ReservationNotActive, exception.ErrorCode);
    }

    [Fact]
    public async Task ExpireOverdueReservationsAsync_ReleasesOnlyOverdueOnes_AndIsIdempotentOnASecondCall()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var overdueSku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var freshSku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var overdueOrderId = await _fixture.SeedOrderAsync(context);
        var freshOrderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context);
        var now = DateTime.UtcNow;
        await service.ReserveAsync(overdueOrderId, [new ReservationLine(overdueSku.PublicId, 2)], now.AddMinutes(-1), now.AddMinutes(-16), CancellationToken.None);
        await service.ReserveAsync(freshOrderId, [new ReservationLine(freshSku.PublicId, 2)], now.AddMinutes(15), now, CancellationToken.None);

        var firstSweepCount = await service.ExpireOverdueReservationsAsync(now, CancellationToken.None);
        Assert.Equal(1, firstSweepCount);

        var overdueReservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == overdueOrderId);
        Assert.Equal(InventoryReservationStatus.Expired, overdueReservation.Status);
        var freshReservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == freshOrderId);
        Assert.Equal(InventoryReservationStatus.Active, freshReservation.Status);

        var secondSweepCount = await service.ExpireOverdueReservationsAsync(now, CancellationToken.None);
        Assert.Equal(0, secondSweepCount);

        var overdueBalance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == overdueSku.Id);
        Assert.Equal(0, overdueBalance.ReservedQuantity);
    }

    [Fact]
    public async Task ConsumeAllForOrderAsync_DeductsOnHandAndMarksConsumed()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context);
        var now = DateTime.UtcNow;
        await service.ReserveAsync(orderId, [new ReservationLine(sku.PublicId, 2)], null, now, CancellationToken.None);

        await service.ConsumeAllForOrderAsync(orderId, now.AddHours(1), CancellationToken.None);

        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(3, balance.OnHandQuantity);
        Assert.Equal(0, balance.ReservedQuantity);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status);
        var shipMovement = await context.InventoryMovements.AsNoTracking()
            .SingleAsync(m => m.SkuId == sku.Id && m.MovementType == InventoryMovementTypes.Ship);
        Assert.Equal(-2, shipMovement.OnHandDelta);
    }
}
