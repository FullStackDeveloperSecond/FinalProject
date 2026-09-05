using System.Data.Common;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Auditing;
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

    private static readonly AuditRequestContext TestAuditContext =
        new("release-test-correlation", "0123456789abcdef0123456789abcdef", null);

    [Fact]
    public async Task ReserveAsync_WhenStockIsSufficient_IncreasesReservedAndCreatesMovement()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 10);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));

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
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));

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
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));

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
        var serviceA = new EfInventoryReservationService(contextA, new EfAuditWriter(contextA, TimeProvider.System));
        var serviceB = new EfInventoryReservationService(contextB, new EfAuditWriter(contextB, TimeProvider.System));
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
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));

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
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));

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
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));

        await using var transaction = await context.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReserveAsync(
            orderId,
            [new ReservationLine(sku.PublicId, 3), new ReservationLine(sku.PublicId, 4)],
            null, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.InsufficientStock, exception.ErrorCode);
        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);
    }

    [Fact]
    public async Task ReleaseAsync_WhenActive_RestoresAvailableAndMarksReleased()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.InventoryManager);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        await service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "customer requested", adminUserId, reservation.RowVersion,
            TestAuditContext, now.AddMinutes(1), CancellationToken.None);

        var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(5, balance.AvailableQuantity);
        var updated = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservation.PublicId);
        Assert.Equal(InventoryReservationStatus.Released, updated.Status);
    }

    // ---------------------------------------------------------------------------------------
    // UC-ADM-INV-01「手動釋放成功 → 保存 InventoryMovement 與 Audit Log」。這幾支是這條路由被撤回
    // （PR #36 round 3）又補回來的理由：稽核要跟釋放同一次 SaveChanges，而且內容要能回答
    // 「誰、為什麼、從哪個狀態、哪張訂單、哪個 SKU、幾件」。
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ReleaseAsync_WhenActive_WritesTheCentralAuditLogWithActorReasonAndBeforeAfterValues()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.InventoryManager);
        var adminPublicId = await context.Users.AsNoTracking().Where(u => u.Id == adminUserId).Select(u => u.PublicId).SingleAsync();
        var orderPublicId = await context.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.PublicId).SingleAsync();
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        await service.ReleaseAsync(
            reservation.PublicId, "inventory_correction", "盤點後修正：實際庫存不足", adminUserId, reservation.RowVersion,
            TestAuditContext, now.AddMinutes(1), CancellationToken.None);

        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var audit = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.ResourcePublicId == reservation.PublicId);
        Assert.Equal(AuditActions.InventoryReservationRelease, audit.Action);
        Assert.Equal(AuditResourceTypes.InventoryReservation, audit.ResourceType);
        Assert.Equal(adminPublicId, audit.ActorPublicId);
        Assert.Contains(AuditRoleNames.InventoryManager, audit.ActorRolesJson);
        Assert.Equal("inventory_correction", audit.Reason);
        Assert.Equal(TestAuditContext.CorrelationId, audit.CorrelationId);
        Assert.Equal(TestAuditContext.TraceId, audit.TraceId);

        using var envelope = JsonDocument.Parse(audit.ChangedFieldsJson);
        Assert.Equal("盤點後修正：實際庫存不足", envelope.RootElement.GetProperty("note").GetString());
        var changes = envelope.RootElement.GetProperty("changes").EnumerateArray()
            .ToDictionary(
                change => change.GetProperty("field").GetString()!,
                change => (Before: change.GetProperty("beforeCode").GetString(), After: change.GetProperty("afterCode").GetString()));
        Assert.Equal(("Active", "Released"), changes["status"]);
        Assert.Equal((null, "inventory_correction"), changes["reasonCode"]);
        Assert.Equal((null, orderPublicId.ToString("D")), changes["orderPublicId"]);
        Assert.Equal((null, sku.PublicId.ToString("D")), changes["skuPublicId"]);
        Assert.Equal((null, "2"), changes["quantity"]);
        Assert.Equal(("2", "0"), changes["reservedQuantity"]);

        // 同一筆釋放也留了 Movement——驗收是「Movement 與 Audit Log」兩個都要。
        var movement = await verify.InventoryMovements.AsNoTracking()
            .SingleAsync(m => m.ReservationId == reservation.Id && m.MovementType == InventoryMovementTypes.Release);
        Assert.Equal(adminUserId, movement.ActorUserId);
    }

    /// <summary>
    /// 稽核與釋放必須在同一次 SaveChanges。這支把 AuditLogs 的 INSERT 弄壞：如果稽核是釋放**之後**
    /// 另一次 SaveChanges 才寫（ReleaseAsync 沒有自己的交易，那樣釋放早就提交了），Balance 會已經
    /// 扣掉、Reservation 已經 Released、卻沒有稽核——正是驗收不允許的狀態，這支就會轉紅。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenTheAuditInsertFails_ReleasesNothing()
    {
        await using var seedContext = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(seedContext, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(seedContext);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(seedContext, AuditRoleNames.InventoryManager);
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(
            new EfInventoryReservationService(seedContext, new EfAuditWriter(seedContext, TimeProvider.System)),
            seedContext, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await seedContext.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        var interceptor = new ThrowOnTableInsertInterceptor("[AuditLogs]");
        await using var context = InventoryReservationServiceFixture.CreateContext(interceptor);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "customer requested", adminUserId, reservation.RowVersion,
            TestAuditContext, now.AddMinutes(1), CancellationToken.None));
        Assert.True(interceptor.Engaged);

        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var unchanged = await verify.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservation.PublicId);
        Assert.Equal(InventoryReservationStatus.Active, unchanged.Status);
        var balance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == sku.Id);
        Assert.Equal(2, balance.ReservedQuantity);
        Assert.False(await verify.InventoryMovements.AsNoTracking()
            .AnyAsync(m => m.ReservationId == reservation.Id && m.MovementType == InventoryMovementTypes.Release));
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reservation.PublicId));
    }

    [Fact]
    public async Task ReleaseAsync_WhenTheNoteIsBlank_ThrowsValidationFailedAndChangesNothing()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.InventoryManager);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "   ", adminUserId, reservation.RowVersion,
            TestAuditContext, now, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        await AssertStillActiveAsync(reservation.PublicId, sku.Id, expectedReserved: 2);
    }

    /// <summary>
    /// note 進的是中央稽核，所以要過中央稽核的字元規則（不收 &lt; &gt; 引號等）。這要在任何東西送到
    /// 資料庫之前就擋成 validation_failed，而不是讓 AuditWriteRequest.Create 的 ArgumentException
    /// 變成 500。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenTheNoteBreaksTheAuditRules_ThrowsValidationFailedAndChangesNothing()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.InventoryManager);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "<script>alert(1)</script>", adminUserId, reservation.RowVersion,
            TestAuditContext, now, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        await AssertStillActiveAsync(reservation.PublicId, sku.Id, expectedReserved: 2);
    }

    /// <summary>稽核的角色快照從 UserRoles 讀；沒有 InventoryManager／SuperAdmin 的管理員不能釋放。</summary>
    [Fact]
    public async Task ReleaseAsync_WhenTheAdminLacksTheInventoryRole_ThrowsForbiddenAndChangesNothing()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "n/a", adminUserId, reservation.RowVersion,
            TestAuditContext, now, CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
        await AssertStillActiveAsync(reservation.PublicId, sku.Id, expectedReserved: 2);
    }

    private static async Task AssertStillActiveAsync(Guid reservationPublicId, long skuId, int expectedReserved)
    {
        await using var verify = InventoryReservationServiceFixture.CreateContext();
        var reservation = await verify.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservationPublicId);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        var balance = await verify.InventoryBalances.AsNoTracking().SingleAsync(b => b.SkuId == skuId);
        Assert.Equal(expectedReserved, balance.ReservedQuantity);
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == reservationPublicId));
    }

    /// <summary>Throws only when the SQL text is an INSERT into the named table.</summary>
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

    [Fact]
    public async Task ReleaseAsync_WhenAlreadyReleased_ThrowsReservationNotActive()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.InventoryManager);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        await service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "n/a", adminUserId, reservation.RowVersion,
            TestAuditContext, now, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReleaseAsync(
            reservation.PublicId, "customer_cancelled", "n/a", adminUserId, reservation.RowVersion,
            TestAuditContext, now, CancellationToken.None));
        Assert.Equal(InventoryWriteException.ErrorCodes.ReservationNotActive, exception.ErrorCode);

        // 驗收「同一請求重送 → 不得再次減少 ReservedQuantity」：第二次被拒之後仍只有一筆釋放稽核。
        Assert.Equal(1, await context.AuditLogs.AsNoTracking().CountAsync(a => a.ResourcePublicId == reservation.PublicId));
    }

    [Fact]
    public async Task ReleaseAsync_WhenReasonCodeIsNotInTheControlledWhitelist_ThrowsValidationFailed()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var adminUserId = await InventoryReservationServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.InventoryManager);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
        var now = DateTime.UtcNow;
        await ReserveWithinTransactionAsync(service, context, orderId, [new ReservationLine(sku.PublicId, 2)], null, now);
        var reservation = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);

        // "member_cancelled" was the whitelist's own draft value before 組長's PR #36 round-3
        // ruling superseded it with "customer_cancelled" (Guest orders can be released too) —
        // proving the old name is now rejected, not silently accepted as a synonym.
        var exception = await Assert.ThrowsAsync<InventoryWriteException>(() => service.ReleaseAsync(
            reservation.PublicId, "member_cancelled", "n/a", adminUserId, reservation.RowVersion,
            TestAuditContext, now, CancellationToken.None));

        Assert.Equal(InventoryWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        var unchanged = await context.InventoryReservations.AsNoTracking().SingleAsync(r => r.PublicId == reservation.PublicId);
        Assert.Equal(InventoryReservationStatus.Active, unchanged.Status);
    }

    [Fact]
    public async Task ConsumeAllForOrderAsync_DeductsOnHandAndMarksConsumed()
    {
        await using var context = InventoryReservationServiceFixture.CreateContext();
        var sku = await _fixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5);
        var orderId = await _fixture.SeedOrderAsync(context);
        var service = new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System));
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
