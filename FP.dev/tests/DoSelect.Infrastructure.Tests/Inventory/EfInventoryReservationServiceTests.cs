using System.Data.Common;
using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await service.ReserveAsync(
                orderId, [new ReservationLine(sku.PublicId, 3)], DateTime.UtcNow.AddMinutes(15), DateTime.UtcNow, CancellationToken.None);
            await transaction.CommitAsync();
        }

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

        await using var transaction = await context.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReserveAsync(
            orderId,
            [new ReservationLine(plentifulSku.PublicId, 2), new ReservationLine(scarceSku.PublicId, 5)],
            null, DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(InventoryWriteException.ErrorCodes.InsufficientStock, exception.ErrorCode);

        var plentifulBalance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == plentifulSku.Id);
        Assert.Equal(0, plentifulBalance.ReservedQuantity);
        Assert.Empty(await context.InventoryReservations.AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync());
    }

    /// <summary>
    /// 組長 PR #36 review, item 3: the existing "insufficient stock" rollback test above never
    /// reaches SaveChangesAsync at all — EnsureSufficientStock throws before either write. This
    /// exercises the actually-atomic-only-via-ambient-transaction claim ReserveAsync's own doc
    /// comment makes: Balance/Reservation write and *commit* fine on the first SaveChangesAsync,
    /// then the second SaveChangesAsync (the InventoryMovement insert) fails, and only the
    /// caller's ambient transaction — never committed here — is what actually undoes the first
    /// write. <see cref="ThrowOnInventoryMovementInsertInterceptor"/> forces that specific,
    /// otherwise near-impossible-to-trigger-in-a-test failure deterministically.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_WhenTheMovementInsertFails_RollsBackTheEarlierBalanceAndReservationWriteToo()
    {
        await using var seedContext = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(seedContext, onHandQuantity: 10);
        var orderId = await _fixture.SeedOrderAsync(seedContext);

        var interceptor = new ThrowOnInventoryMovementInsertInterceptor();
        await using var context = InventoryReservationServiceFixture.CreateContext(interceptor);
        var service = new EfInventoryReservationService(context);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            // EF wraps the interceptor's InvalidOperationException in a DbUpdateException.
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ReserveAsync(
                orderId, [new ReservationLine(sku.PublicId, 3)], null, DateTime.UtcNow, CancellationToken.None));

            // Deliberately no transaction.CommitAsync() — disposing an uncommitted transaction
            // rolls it back, which is the only thing that can undo the Balance/Reservation write
            // that already reached SQL Server via the first, successful SaveChangesAsync.
        }
        Assert.True(interceptor.Engaged);

        await using var verifyContext = InventoryReservationServiceFixture.CreateContext();
        var balance = await verifyContext.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(10, balance.AvailableQuantity);
        Assert.Empty(await verifyContext.InventoryReservations.AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync());
        Assert.Empty(await verifyContext.InventoryMovements.AsNoTracking().Where(m => m.SkuId == sku.Id).ToListAsync());
    }

    /// <summary>Throws only when the SQL text is an INSERT into InventoryMovements — lets the earlier Balance/Reservation SaveChangesAsync succeed normally.</summary>
    private sealed class ThrowOnInventoryMovementInsertInterceptor : DbCommandInterceptor
    {
        public bool Engaged { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfMovementInsert(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfMovementInsert(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void ThrowIfMovementInsert(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("[InventoryMovements]", StringComparison.OrdinalIgnoreCase))
            {
                Engaged = true;
                throw new InvalidOperationException("Injected InventoryMovement insert failure.");
            }
        }
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
            RunOrCaptureInsufficientStockAsync(serviceA, contextA, orderAId, line, now),
            RunOrCaptureInsufficientStockAsync(serviceB, contextB, orderBId, line, now));

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
        EfInventoryReservationService service, DoSelectDbContext context, long orderId, ReservationLine line, DateTime now)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await service.ReserveAsync(orderId, [line], null, now, CancellationToken.None);
            await transaction.CommitAsync();
            return null;
        }
        catch (InventoryWriteException exception)
        {
            return exception.ErrorCode;
        }
    }

    private static async Task ReserveWithinTransactionAsync(
        EfInventoryReservationService service, DoSelectDbContext context, long orderId,
        IReadOnlyList<ReservationLine> lines, DateTime? expiresAtUtc, DateTime now)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        await service.ReserveAsync(orderId, lines, expiresAtUtc, now, CancellationToken.None);
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Regression test: ReserveAsync used to write Balance/Reservation and the InventoryMovement
    /// audit trail as two separate SaveChangesAsync calls with no ambient transaction enforced —
    /// only atomic when the caller happened to wrap it. Now fails fast instead (組長 PR #36 round-4
    /// review, item 4).
    /// </summary>
    [Fact]
    public async Task ReserveAsync_WhenCalledWithoutAnAmbientTransaction_ThrowsImmediately()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReserveAsync(
            orderId, [new ReservationLine(sku.PublicId, 1)], null, DateTime.UtcNow, CancellationToken.None));

        Assert.False(await context.InventoryReservations.AsNoTracking().AnyAsync(r => r.OrderId == orderId));
    }

    /// <summary>
    /// Regression test: the same SKU appearing twice in `lines` used to validate each line
    /// separately against the same starting AvailableQuantity (each individually looking
    /// sufficient), then throw a raw domain exception once the summed reservation actually got
    /// applied. Now merges duplicate lines and validates the combined total once (組長 PR #36
    /// round-4 review, item 4).
    /// </summary>
    [Fact]
    public async Task ReserveAsync_WhenTheSameSkuAppearsTwiceInLines_MergesQuantitiesIntoOneReservation()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context);

        await ReserveWithinTransactionAsync(
            service, context, orderId,
            [new ReservationLine(sku.PublicId, 2), new ReservationLine(sku.PublicId, 3)],
            null, DateTime.UtcNow);

        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        Assert.Equal(5, reservation.Quantity);
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(5, balance.ReservedQuantity);
    }

    [Fact]
    public async Task ReserveAsync_WhenDuplicateSkuLinesSumToMoreThanAvailable_ThrowsInsufficientStockAndReservesNothing()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        // 3 + 4 = 7 exceeds the balance of 5, even though 3 <= 5 and 4 <= 5 would each pass a
        // per-line check individually.
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context);

        await using var transaction = await context.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReserveAsync(
            orderId,
            [new ReservationLine(sku.PublicId, 3), new ReservationLine(sku.PublicId, 4)],
            null, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.InsufficientStock, exception.ErrorCode);
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);
    }

    /// <summary>
    /// Regression test: two concurrent sweeps racing over the same overdue reservations used to
    /// have one of them fail its whole batch SaveChanges on a RowVersion conflict and abort
    /// entirely, instead of skipping whatever the other sweep already claimed and continuing
    /// (組長 PR #36 round-4 review, item 5 — "已處理者略過、可安全並行").
    /// </summary>
    [Fact]
    public async Task ExpireOverdueReservationsAsync_WhenTwoSweepsRunTrulyConcurrently_EachReservationIsReleasedExactlyOnce()
    {
        await using var seedContext = InventoryReservationServiceFixture.CreateContext();
        var skuA = await _fixture.SeedSkuWithBalanceAsync(seedContext, onHandQuantity: 5);
        var skuB = await _fixture.SeedSkuWithBalanceAsync(seedContext, onHandQuantity: 5);
        var orderAId = await _fixture.SeedOrderAsync(seedContext);
        var orderBId = await _fixture.SeedOrderAsync(seedContext);
        var seedService = new EfInventoryReservationService(seedContext);
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(
            seedService, seedContext, orderAId, [new ReservationLine(skuA.PublicId, 2)], now.AddMinutes(-1), now.AddMinutes(-16));
        await ReserveWithinTransactionAsync(
            seedService, seedContext, orderBId, [new ReservationLine(skuB.PublicId, 2)], now.AddMinutes(-1), now.AddMinutes(-16));

        await using var contextA = InventoryReservationServiceFixture.CreateContext();
        await using var contextB = InventoryReservationServiceFixture.CreateContext();
        var sweepA = new EfInventoryReservationService(contextA);
        var sweepB = new EfInventoryReservationService(contextB);

        var results = await Task.WhenAll(
            sweepA.ExpireOverdueReservationsAsync(now, CancellationToken.None),
            sweepB.ExpireOverdueReservationsAsync(now, CancellationToken.None));

        Assert.Equal(2, results.Sum());
        await using var verifyContext = InventoryReservationServiceFixture.CreateContext();
        var reservationA = await verifyContext.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderAId);
        var reservationB = await verifyContext.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderBId);
        Assert.Equal(InventoryReservationStatus.Expired, reservationA.Status);
        Assert.Equal(InventoryReservationStatus.Expired, reservationB.Status);
        var balanceA = await verifyContext.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == skuA.Id);
        var balanceB = await verifyContext.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == skuB.Id);
        Assert.Equal(0, balanceA.ReservedQuantity);
        Assert.Equal(0, balanceB.ReservedQuantity);
        // Exactly one Release movement per SKU — neither sweep double-released the other's reservation.
        Assert.Equal(1, await verifyContext.InventoryMovements.AsNoTracking()
            .CountAsync(m => m.SkuId == skuA.Id && m.MovementType == InventoryMovementTypes.Release));
        Assert.Equal(1, await verifyContext.InventoryMovements.AsNoTracking()
            .CountAsync(m => m.SkuId == skuB.Id && m.MovementType == InventoryMovementTypes.Release));
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
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        await service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "customer requested", adminUserId, reservation.RowVersion,
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
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        await service.ReleaseAsync(reservation.PublicId, "customer_cancelled", "n/a", adminUserId, reservation.RowVersion, now, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "n/a", adminUserId, reservation.RowVersion, now, CancellationToken.None));
        Assert.Equal(InventoryWriteException.ErrorCodes.ReservationNotActive, exception.ErrorCode);
    }

    [Fact]
    public async Task ReleaseAsync_WhenReasonCodeIsNotInTheControlledWhitelist_ThrowsValidationFailed()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfInventoryReservationService(context);
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        // "member_cancelled" was the whitelist's own draft value before 組長's PR #36 round-3
        // ruling superseded it with "customer_cancelled" (Guest orders can be released too) —
        // proving the old name is now rejected, not silently accepted as a synonym.
        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReleaseAsync(
            reservation.PublicId, "member_cancelled", "n/a", adminUserId, reservation.RowVersion, now, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        var unchanged = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservation.PublicId);
        Assert.Equal(InventoryReservationStatus.Active, unchanged.Status);
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
        await ReserveWithinTransactionAsync(
            service, context, overdueOrderId, [new ReservationLine(overdueSku.PublicId, 2)], now.AddMinutes(-1), now.AddMinutes(-16));
        await ReserveWithinTransactionAsync(
            service, context, freshOrderId, [new ReservationLine(freshSku.PublicId, 2)], now.AddMinutes(15), now);

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
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);

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
