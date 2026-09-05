using System.Data.Common;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Payments;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Orders;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Shipping;
using DoSelect.Infrastructure.Tests.Orders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Shipping;

/// <summary>
/// M-11 物流狀態命令（組長 2026-09-04 裁定 A1～E1）。對真實 SQL Server 驗證，因為要證明的是交易邊界：
/// 狀態、歷程、Order 投影、COD 收款、Completed、Audit、Outbox 同一筆交易；重播不重複副作用；
/// RowVersion 競態只有一個贏。
/// </summary>
[Collection(nameof(OrderServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfShipmentStatusServiceTests
{
    private static readonly AuditRequestContext TestAuditContext =
        new("shipment-status-correlation", "0123456789abcdef0123456789abcdef", null);

    [Fact]
    public async Task HomeDelivery_InTransitThenDelivered_WritesHistoriesProjectionCompletionAuditAndNotifications()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: false);

        var inTransit = await Execute(context, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion);
        Assert.False(inTransit.IsReplay);
        Assert.Equal("InTransit", inTransit.Order.Shipment!.Status);
        Assert.Equal([ShipmentStatusActions.Delivered, ShipmentStatusActions.DeliveryFailed], inTransit.Order.Shipment.AvailableActions);

        var delivered = await Execute(context, seed, ShipmentStatusActions.Delivered, inTransit.Order.Shipment.RowVersion);

        Assert.Equal("Delivered", delivered.Order.Shipment!.Status);
        Assert.Empty(delivered.Order.Shipment.AvailableActions);
        Assert.Equal("Delivered", delivered.Order.FulfillmentStatus);
        Assert.Equal("Completed", delivered.Order.OrderStatus);
        Assert.NotNull(delivered.Order.DeliveredAtUtc);
        Assert.NotNull(delivered.Order.CompletedAtUtc);
        Assert.Equal(3, delivered.Order.Shipment.History.Count(entry => entry.ToStatus is "Shipped" or "InTransit" or "Delivered"));

        await using var verify = OrderServiceFixture.CreateContext();
        var histories = await verify.OrderStatusHistories.AsNoTracking().Where(h => h.OrderId == seed.Order.Id).ToListAsync();
        Assert.Contains(histories, h => h.StateDimension == OrderStateDimension.FulfillmentStatus && h.ToStatus == "InTransit" && h.ActorUserId == seed.AdminUserId);
        Assert.Contains(histories, h => h.StateDimension == OrderStateDimension.FulfillmentStatus && h.ToStatus == "Delivered");
        Assert.Contains(histories, h => h.StateDimension == OrderStateDimension.OrderStatus && h.FromStatus == "Processing" && h.ToStatus == "Completed");

        var audits = await verify.AuditLogs.AsNoTracking().Where(a => a.ResourcePublicId == seed.Order.PublicId).OrderBy(a => a.OccurredAtUtc).ToListAsync();
        Assert.Equal([AuditActions.ShipmentMarkInTransit, AuditActions.ShipmentMarkDelivered], audits.Select(a => a.Action).ToArray());
        var deliveredChanges = Changes(audits[1].ChangedFieldsJson);
        Assert.Equal(("InTransit", "Delivered"), deliveredChanges["fulfillmentStatus"]);
        Assert.Equal(("Processing", "Completed"), deliveredChanges["orderStatus"]);
        Assert.False(deliveredChanges.ContainsKey("paymentStatus"));

        // 非 COD：付款不動、沒有付款嘗試被建立或改動、沒有發票 Outbox；有兩次物流通知。
        var refreshedOrder = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == seed.Order.Id);
        Assert.Equal(PaymentStatus.Paid, refreshedOrder.PaymentStatus);
        Assert.Equal(seed.Order.PaidAtUtc, refreshedOrder.PaidAtUtc);
        Assert.False(await verify.PaymentAttempts.AsNoTracking().AnyAsync(a => a.OrderId == seed.Order.Id));
        var outbox = await verify.OutboxMessages.AsNoTracking()
            .Where(m => m.AggregatePublicId == seed.Shipment.PublicId || m.AggregatePublicId == seed.Order.PublicId)
            .ToListAsync();
        // 兩次狀態命令 × （Email＋會員站內）＝ 4 筆 shipment.updated 通知。
        Assert.Equal(4, outbox.Count(m => m.AggregateType == "Shipment" && m.PayloadJson.Contains("shipment.updated")));
        Assert.DoesNotContain(outbox, m => m.AggregateType == AuditResourceTypes.Order);
    }

    [Fact]
    public async Task Delivered_DirectlyFromShipped_IsRejectedAndNothingIsWritten()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: false);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            Execute(context, seed, ShipmentStatusActions.Delivered, seed.Shipment.RowVersion));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(ShippingErrorCodes.ShippingStatusTransitionInvalid, exception.Code);
        await AssertUntouchedAsync(seed, FulfillmentStatus.Shipped);
    }

    /// <summary>B1：宅配才允許 Delivered；超取才允許 PickupReady／PickedUp。</summary>
    [Theory]
    [InlineData(false, ShipmentStatusActions.PickupReady)]
    [InlineData(true, ShipmentStatusActions.Delivered)]
    public async Task MethodKind_RestrictsTheTargetStatus(bool storePickup, string action)
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup, cashOnDelivery: false);
        var inTransit = await Execute(context, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion);
        Assert.DoesNotContain(action, inTransit.Order.Shipment!.AvailableActions);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            Execute(context, seed, action, inTransit.Order.Shipment.RowVersion));

        Assert.Equal(ShippingErrorCodes.ShippingStatusTransitionInvalid, exception.Code);
        await AssertUntouchedAsync(seed, FulfillmentStatus.InTransit);
    }

    /// <summary>B1：超取 COD 在 PickedUp 同一交易收款、開票、通知，並把訂單推進 Completed。</summary>
    [Fact]
    public async Task StorePickup_PickupReadyThenPickedUp_CompletesCashOnDeliveryAndTheOrderAtomically()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: true, cashOnDelivery: true);

        var inTransit = await Execute(context, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion);
        var ready = await Execute(context, seed, ShipmentStatusActions.PickupReady, inTransit.Order.Shipment!.RowVersion);
        Assert.Equal("AwaitingPayment", ready.Order.PaymentStatus);
        Assert.Equal([ShipmentStatusActions.PickedUp, ShipmentStatusActions.DeliveryFailed], ready.Order.Shipment!.AvailableActions);

        var pickedUp = await Execute(context, seed, ShipmentStatusActions.PickedUp, ready.Order.Shipment.RowVersion);

        Assert.Equal("PickedUp", pickedUp.Order.Shipment!.Status);
        Assert.Equal("Paid", pickedUp.Order.PaymentStatus);
        Assert.Equal("Completed", pickedUp.Order.OrderStatus);
        Assert.Equal(seed.Order.GrandTotal, pickedUp.Order.Amounts.PaidAmount);

        await using var verify = OrderServiceFixture.CreateContext();
        var attempt = await verify.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == seed.CodAttemptId);
        Assert.Equal(PaymentAttemptStatus.Paid, attempt.Status);
        var paymentEvent = await verify.PaymentEvents.AsNoTracking().SingleAsync(e => e.PaymentAttemptId == attempt.Id);
        Assert.Equal($"cod-delivery:{seed.Shipment.PublicId:N}:PickedUp", paymentEvent.ExternalEventId);
        var histories = await verify.OrderStatusHistories.AsNoTracking().Where(h => h.OrderId == seed.Order.Id).ToListAsync();
        Assert.Contains(histories, h => h.StateDimension == OrderStateDimension.PaymentStatus && h.FromStatus == "AwaitingPayment" && h.ToStatus == "Paid");
        Assert.Contains(histories, h => h.StateDimension == OrderStateDimension.OrderStatus && h.ToStatus == "Completed");

        var outbox = await verify.OutboxMessages.AsNoTracking()
            .Where(m => m.AggregatePublicId == attempt.PublicId || m.AggregatePublicId == seed.Order.PublicId)
            .ToListAsync();
        Assert.Contains(outbox, m => m.AggregateType == AuditResourceTypes.PaymentAttempt && m.PayloadJson.Contains("payment.succeeded"));
        Assert.Single(outbox, m => m.AggregateType == AuditResourceTypes.Order && m.AggregatePublicId == seed.Order.PublicId);

        var audit = await verify.AuditLogs.AsNoTracking().SingleAsync(a => a.ResourcePublicId == seed.Order.PublicId && a.Action == AuditActions.ShipmentMarkPickedUp);
        var changes = Changes(audit.ChangedFieldsJson);
        Assert.Equal(("PickupReady", "PickedUp"), changes["fulfillmentStatus"]);
        // paymentStatus 只能記「改了」（稽核安全代碼不收含 payment 的值）；實際前後值在 PaymentStatus 維度的歷程。
        Assert.True(changes.ContainsKey("paymentStatus"));
        Assert.Equal(("Processing", "Completed"), changes["orderStatus"]);
    }

    /// <summary>A1：delivery-failed／returned 必須有 reasonCode；E1：DeliveryFailed／Returned 不得誤改付款。</summary>
    [Fact]
    public async Task DeliveryFailedAndReturned_RequireAReasonAndNeverTouchPaymentOrOrderStatus()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: true);
        var inTransit = await Execute(context, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion);

        var missingReason = await Assert.ThrowsAsync<DomainProblemException>(() =>
            Execute(context, seed, ShipmentStatusActions.DeliveryFailed, inTransit.Order.Shipment!.RowVersion));
        Assert.Equal(400, missingReason.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, missingReason.Code);

        var failed = await Execute(context, seed, ShipmentStatusActions.DeliveryFailed, inTransit.Order.Shipment!.RowVersion, reasonCode: ShipmentStatusReasonCodes.RecipientAbsent, note: "無人簽收");
        Assert.Equal("DeliveryFailed", failed.Order.Shipment!.Status);
        Assert.Equal([ShipmentStatusActions.InTransit, ShipmentStatusActions.Returned], failed.Order.Shipment.AvailableActions);

        var returned = await Execute(context, seed, ShipmentStatusActions.Returned, failed.Order.Shipment.RowVersion, reasonCode: ShipmentStatusReasonCodes.RecipientRefused);

        Assert.Equal("Returned", returned.Order.Shipment!.Status);
        Assert.Equal("AwaitingPayment", returned.Order.PaymentStatus);
        Assert.Equal("Processing", returned.Order.OrderStatus);
        await using var verify = OrderServiceFixture.CreateContext();
        var attempt = await verify.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == seed.CodAttemptId);
        Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempt.Status);
        Assert.False(await verify.PaymentEvents.AsNoTracking().AnyAsync(e => e.PaymentAttemptId == attempt.Id));
        Assert.False(await verify.OutboxMessages.AsNoTracking().AnyAsync(m => m.AggregateType == AuditResourceTypes.Order && m.AggregatePublicId == seed.Order.PublicId));
        // D1：reasonCode 進歷程與稽核，note 只進稽核。
        Assert.Contains(await verify.OrderStatusHistories.AsNoTracking().Where(h => h.OrderId == seed.Order.Id).ToListAsync(),
            h => h.ToStatus == "DeliveryFailed" && h.ReasonCode == ShipmentStatusReasonCodes.RecipientAbsent);
        var audit = await verify.AuditLogs.AsNoTracking().SingleAsync(a => a.ResourcePublicId == seed.Order.PublicId && a.Action == AuditActions.ShipmentMarkDeliveryFailed);
        Assert.Equal(ShipmentStatusReasonCodes.RecipientAbsent, audit.Reason);
        Assert.Contains("無人簽收", audit.ChangedFieldsJson);
        Assert.DoesNotContain(returned.Order.Shipment.History, entry => entry.ToStatus.Contains("無人簽收"));
    }

    /// <summary>A1：同鍵同 payload 重播回傳目前的訂單；E1：重播不得產生重複付款、歷程、Audit、通知或發票。</summary>
    [Fact]
    public async Task Replay_SameKeyAndPayload_ReturnsTheCurrentOrderWithoutSecondSideEffects()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: true);
        var inTransit = await Execute(context, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion);
        var key = $"key-{Guid.NewGuid():N}";

        await using var firstContext = OrderServiceFixture.CreateContext();
        var first = await Execute(firstContext, seed, ShipmentStatusActions.Delivered, inTransit.Order.Shipment!.RowVersion, key: key);
        Assert.False(first.IsReplay);
        Assert.Equal("Completed", first.Order.OrderStatus);

        await using var secondContext = OrderServiceFixture.CreateContext();
        var replay = await Execute(secondContext, seed, ShipmentStatusActions.Delivered, inTransit.Order.Shipment.RowVersion, key: key);

        Assert.True(replay.IsReplay);
        Assert.Equal(first.Order.Shipment!.RowVersion, replay.Order.Shipment!.RowVersion);
        Assert.Equal("Delivered", replay.Order.Shipment.Status);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(1, await verify.PaymentEvents.AsNoTracking().CountAsync(e => e.PaymentAttemptId == seed.CodAttemptId));
        Assert.Equal(1, await verify.AuditLogs.AsNoTracking().CountAsync(a => a.ResourcePublicId == seed.Order.PublicId && a.Action == AuditActions.ShipmentMarkDelivered));
        Assert.Equal(1, await verify.OutboxMessages.AsNoTracking().CountAsync(m => m.AggregateType == AuditResourceTypes.Order && m.AggregatePublicId == seed.Order.PublicId));
        Assert.Equal(1, await verify.ShipmentStatusHistories.AsNoTracking().CountAsync(h => h.ShipmentId == seed.Shipment.Id && h.ToStatus == FulfillmentStatus.Delivered));
        Assert.Equal(1, await verify.OrderStatusHistories.AsNoTracking().CountAsync(h => h.OrderId == seed.Order.Id && h.StateDimension == OrderStateDimension.PaymentStatus));

        await using var conflictContext = OrderServiceFixture.CreateContext();
        var conflict = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            Execute(conflictContext, seed, ShipmentStatusActions.Delivered, inTransit.Order.Shipment.RowVersion, key: key, note: "different payload"));
        Assert.Equal(IdempotencyErrorCodes.PayloadConflict, conflict.ErrorCode);
    }

    /// <summary>
    /// 組長 #106 裁定 A1：冪等保證是「不重複副作用」，重播回的是目前最新的 AdminOrderDto——鍵 A 推 InTransit、
    /// 鍵 B 再推 Delivered，之後重播鍵 A 拿到的是 Delivered，而且沒有任何新的歷程、稽核、通知或冪等記錄。
    /// </summary>
    [Fact]
    public async Task Replay_AfterAnotherKeyAdvancedTheShipment_ReturnsTheLatestOrderWithoutNewSideEffects()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: false);
        var keyA = $"key-{Guid.NewGuid():N}";

        await using var contextA = OrderServiceFixture.CreateContext();
        var inTransit = await Execute(contextA, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion, key: keyA);
        await using var contextB = OrderServiceFixture.CreateContext();
        var delivered = await Execute(contextB, seed, ShipmentStatusActions.Delivered, inTransit.Order.Shipment!.RowVersion);
        Assert.Equal("Delivered", delivered.Order.Shipment!.Status);
        var before = await SnapshotSideEffectsAsync(seed);

        await using var replayContext = OrderServiceFixture.CreateContext();
        var replay = await Execute(replayContext, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion, key: keyA);

        Assert.True(replay.IsReplay);
        Assert.Equal("Delivered", replay.Order.Shipment!.Status);
        Assert.Equal(delivered.Order.Shipment.RowVersion, replay.Order.Shipment.RowVersion);
        Assert.Equal(delivered.Order.Shipment.History.Count, replay.Order.Shipment.History.Count);
        Assert.Equal(before, await SnapshotSideEffectsAsync(seed));
    }

    /// <summary>
    /// 組長 #106 P2：note 含中央 Audit 規則拒絕的字元（`@`、單引號…）要在第一個寫入前擋成 400 validation_failed，
    /// 不是交易裡的 500；Shipment、歷程、Audit、Outbox 與冪等記錄都不能有殘留。
    /// </summary>
    [Theory]
    [InlineData("請聯絡 ops@doselect.test")]
    [InlineData("recipient's neighbour signed")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Note_RejectedByTheCentralAuditRule_Returns400BeforeAnyWrite(string note)
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: false);
        var before = await SnapshotSideEffectsAsync(seed);

        await using var attempt = OrderServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            Execute(attempt, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion, note: note));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, exception.Code);
        await AssertUntouchedAsync(seed, FulfillmentStatus.Shipped);
        Assert.Equal(before, await SnapshotSideEffectsAsync(seed));
    }

    /// <summary>E1：RowVersion 競態——過期版本被拒；兩個同時的請求只有一個贏。</summary>
    [Fact]
    public async Task RowVersion_StaleIsRejectedAndOnlyOneOfTwoRacingRequestsWins()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: false);

        await using var contextA = OrderServiceFixture.CreateContext();
        await using var contextB = OrderServiceFixture.CreateContext();
        var results = await Task.WhenAll(
            TryExecute(contextA, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion),
            TryExecute(contextB, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion));

        Assert.Equal(1, results.Count(result => result is null));
        var loser = results.Single(result => result is not null)!;
        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, loser.Code);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(1, await verify.ShipmentStatusHistories.AsNoTracking().CountAsync(h => h.ShipmentId == seed.Shipment.Id && h.ToStatus == FulfillmentStatus.InTransit));

        await using var staleContext = OrderServiceFixture.CreateContext();
        var stale = await Assert.ThrowsAsync<DomainProblemException>(() =>
            Execute(staleContext, seed, ShipmentStatusActions.Delivered, seed.Shipment.RowVersion));
        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, stale.Code);
    }

    /// <summary>B1：收不了款交付就不算完成——COD 計畫被拒時整筆回滾。</summary>
    [Fact]
    public async Task CashOnDeliveryRejection_RollsBackTheWholeCommand()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(context, storePickup: false, cashOnDelivery: true);
        var inTransit = await Execute(context, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion);
        var attempt = await context.PaymentAttempts.SingleAsync(a => a.Id == seed.CodAttemptId);
        attempt.Transition(PaymentAttemptStatus.Cancelled, DateTime.UtcNow);
        await context.SaveChangesAsync();

        await using var actContext = OrderServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            Execute(actContext, seed, ShipmentStatusActions.Delivered, inTransit.Order.Shipment!.RowVersion));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, exception.Code);
        await AssertUntouchedAsync(seed, FulfillmentStatus.InTransit);
    }

    /// <summary>B1：稽核寫不進去，狀態轉移也不成立。</summary>
    [Fact]
    public async Task AuditInsertFailure_RollsBackTheStatusChange()
    {
        await using var seedContext = OrderServiceFixture.CreateContext();
        var seed = await SeedAsync(seedContext, storePickup: false, cashOnDelivery: false);

        var interceptor = new ThrowOnAuditInsertInterceptor();
        await using var context = OrderServiceFixture.CreateContext(interceptor);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Execute(context, seed, ShipmentStatusActions.InTransit, seed.Shipment.RowVersion));
        Assert.True(interceptor.Engaged);

        await AssertUntouchedAsync(seed, FulfillmentStatus.Shipped);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<ShipmentStatusResult> Execute(
        DoSelectDbContext context,
        Seed seed,
        string action,
        byte[] rowVersion,
        string? reasonCode = null,
        string? note = null,
        string? key = null) =>
        await CreateService(context).ExecuteAsync(
            new ShipmentStatusCommand(seed.Shipment.PublicId, action, rowVersion, reasonCode, note, key ?? $"key-{Guid.NewGuid():N}"),
            seed.AdminUserId,
            TestAuditContext,
            CancellationToken.None);

    private static async Task<DomainProblemException?> TryExecute(DoSelectDbContext context, Seed seed, string action, byte[] rowVersion)
    {
        try
        {
            await Execute(context, seed, action, rowVersion);
            return null;
        }
        catch (DomainProblemException exception)
        {
            return exception;
        }
    }

    private static async Task AssertUntouchedAsync(Seed seed, FulfillmentStatus expectedStatus)
    {
        await using var verify = OrderServiceFixture.CreateContext();
        var shipment = await verify.Shipments.AsNoTracking().SingleAsync(s => s.Id == seed.Shipment.Id);
        Assert.Equal(expectedStatus, shipment.Status);
        var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == seed.Order.Id);
        Assert.Equal(expectedStatus, order.FulfillmentStatus);
        Assert.Equal(OrderStatus.Processing, order.OrderStatus);
        // 被拒的那一步不留任何歷程：只允許種資料時的 Shipped 與（若已推進）InTransit。
        var allowed = expectedStatus == FulfillmentStatus.Shipped
            ? new[] { FulfillmentStatus.Preparing, FulfillmentStatus.Shipped }
            : new[] { FulfillmentStatus.Preparing, FulfillmentStatus.Shipped, FulfillmentStatus.InTransit };
        Assert.False(await verify.ShipmentStatusHistories.AsNoTracking()
            .AnyAsync(h => h.ShipmentId == seed.Shipment.Id && !allowed.Contains(h.ToStatus)));
        Assert.False(await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.ResourcePublicId == seed.Order.PublicId && a.Action != AuditActions.ShipmentMarkInTransit));
    }

    /// <summary>同一張訂單／物流單的所有副作用計數（含冪等記錄總數——測試在同一 collection 內循序執行）。</summary>
    private static async Task<(int Histories, int OrderHistories, int Audits, int Outbox, int PaymentEvents, int Idempotency)> SnapshotSideEffectsAsync(Seed seed)
    {
        await using var verify = OrderServiceFixture.CreateContext();
        return (
            await verify.ShipmentStatusHistories.AsNoTracking().CountAsync(h => h.ShipmentId == seed.Shipment.Id),
            await verify.OrderStatusHistories.AsNoTracking().CountAsync(h => h.OrderId == seed.Order.Id),
            await verify.AuditLogs.AsNoTracking().CountAsync(a => a.ResourcePublicId == seed.Order.PublicId),
            await verify.OutboxMessages.AsNoTracking().CountAsync(m => m.AggregatePublicId == seed.Order.PublicId || m.AggregatePublicId == seed.Shipment.PublicId),
            await verify.PaymentEvents.AsNoTracking().CountAsync(e => e.PaymentAttemptId == seed.CodAttemptId),
            await verify.IdempotencyRecords.AsNoTracking().CountAsync());
    }

    private static EfShipmentStatusService CreateService(DoSelectDbContext context) =>
        new(
            context,
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions { ActorScopePepper = "shipment-status-tests-pepper-0123456789abcdef" }),
                TimeProvider.System),
            new EfOutboxWriter(context, TimeProvider.System),
            new EfAuditWriter(context, TimeProvider.System),
            new EfAdminOrderService(context, new EfAuditWriter(context, TimeProvider.System), TimeProvider.System),
            new CashOnDeliveryCompletionService(),
            TimeProvider.System);

    private static Dictionary<string, (string? Before, string? After)> Changes(string changedFieldsJson)
    {
        using var envelope = JsonDocument.Parse(changedFieldsJson);
        return envelope.RootElement.GetProperty("changes").EnumerateArray()
            .ToDictionary(
                change => change.GetProperty("field").GetString()!,
                change => (change.GetProperty("beforeCode").GetString(), change.GetProperty("afterCode").GetString()));
    }

    private sealed record Seed(Order Order, Shipment Shipment, string AdminUserId, long CodAttemptId);

    /// <summary>
    /// 一張 Processing、已出貨（Shipped）的訂單與它的物流單。宅配或超取決定 Shipment 掛的方法種類；
    /// COD 時訂單等收款並帶一筆 AwaitingPayment 的 COD 嘗試，否則已付款。
    /// </summary>
    private static async Task<Seed> SeedAsync(DoSelectDbContext context, bool storePickup, bool cashOnDelivery)
    {
        var now = DateTime.UtcNow;
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var adminUserId = await SeedOrderManagerAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var method = storePickup
            ? await SeedStorePickupMethodAsync(context)
            : await OrderServiceFixture.SeedShippingMethodAsync(context);

        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.Processing, FulfillmentStatus.Shipped,
            storeCode: storePickup ? "STORE-001" : null);
        long codAttemptId = 0;
        if (cashOnDelivery)
        {
            order.ApplyPaymentProjection(PaymentStatus.AwaitingPayment, 0m, now);
            var attempt = new PaymentAttempt(
                Guid.CreateVersion7(), order.Id, PaymentMethod.CashOnDelivery, order.GrandTotal,
                "COD", $"cod-{Guid.NewGuid():N}", null, now);
            attempt.Transition(PaymentAttemptStatus.AwaitingPayment, now);
            context.PaymentAttempts.Add(attempt);
            await context.SaveChangesAsync();
            codAttemptId = attempt.Id;
        }
        else
        {
            order.ApplyPaymentProjection(PaymentStatus.Paid, order.GrandTotal, now);
            await context.SaveChangesAsync();
        }

        var shipment = new Shipment(
            Guid.CreateVersion7(), order.Id, method.Id, provider.Id, convenienceStoreId: null,
            $"SH{Guid.NewGuid():N}"[..20], order.ShippingFee, now);
        shipment.SetTrackingNumber($"TRK{Guid.NewGuid():N}"[..20], now);
        shipment.ChangeStatus(FulfillmentStatus.Preparing, now);
        shipment.ChangeStatus(FulfillmentStatus.Shipped, now);
        context.Shipments.Add(shipment);
        await context.SaveChangesAsync();
        context.ShipmentStatusHistories.Add(new ShipmentStatusHistory(Guid.CreateVersion7(), shipment.Id, FulfillmentStatus.Preparing, FulfillmentStatus.Shipped, null, now, adminUserId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var trackedOrder = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        var trackedShipment = await context.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipment.Id);
        return new Seed(trackedOrder, trackedShipment, adminUserId, codAttemptId);
    }

    private static async Task<ShippingMethod> SeedStorePickupMethodAsync(DoSelectDbContext context)
    {
        var method = new ShippingMethod(
            Guid.CreateVersion7(), $"store-pickup-{Guid.NewGuid():N}"[..24], "超商取貨", ShippingMethodKinds.StorePickup,
            baseFee: 60m, freeShippingThreshold: null, allowsCod: true, requiresPrepayment: false,
            providerCode: "STORE", createdAtUtc: DateTime.UtcNow);
        context.ShippingMethods.Add(method);
        await context.SaveChangesAsync();
        return method;
    }

    private static async Task<string> SeedOrderManagerAsync(DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        var role = await context.Roles.SingleOrDefaultAsync(candidate => candidate.Name == AuditRoleNames.OrderManager);
        if (role is null)
        {
            role = new IdentityRole(AuditRoleNames.OrderManager);
            context.Roles.Add(role);
            await context.SaveChangesAsync();
        }

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
        await context.SaveChangesAsync();
        return admin.Id;
    }

    private sealed class ThrowOnAuditInsertInterceptor : DbCommandInterceptor
    {
        public bool Engaged { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Throw(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Throw(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Throw(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("[AuditLogs]", StringComparison.OrdinalIgnoreCase))
            {
                Engaged = true;
                throw new InvalidOperationException("Injected AuditLogs insert failure.");
            }
        }
    }
}
