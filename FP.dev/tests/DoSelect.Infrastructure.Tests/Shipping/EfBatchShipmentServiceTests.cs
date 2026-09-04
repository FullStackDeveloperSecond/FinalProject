using System.Data.Common;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Inventory;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Shipping;
using DoSelect.Infrastructure.Tests.Orders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Shipping;

/// <summary>
/// UC-ADM-SHIP-02 批次出貨。對真實 SQL Server 驗證，因為這裡要證明的正是 InMemory Provider 上不
/// 存在的事：每筆訂單各自的交易真的獨立提交、RowVersion 條件真的送到資料庫、`sp_getapplock` 真的
/// 擋住同一把冪等鍵的並行請求。
/// </summary>
[Collection(nameof(OrderServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfBatchShipmentServiceTests
{
    private readonly OrderServiceFixture _fixture;

    public EfBatchShipmentServiceTests(OrderServiceFixture fixture) => _fixture = fixture;

    private static readonly AuditRequestContext TestAuditContext =
        new("batch-test-correlation", "0123456789abcdef0123456789abcdef", null);

    /// <summary>
    /// 這支是整個功能的核心不變量：「一筆失敗不回滾其他已成功出貨的訂單」。
    ///
    /// 第一筆刻意種成沒有 Active 保留（不符出貨條件），第二筆健康。第二筆必須真的出貨完成，而不是
    /// 被前一筆的失敗拖著一起消失。
    /// </summary>
    [Fact]
    public async Task OneOrderFailingDoesNotRollBackAnotherOrdersShipment()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);

        var broken = await SeedShippableOrderAsync(context, seed, withReservation: false);
        var healthy = await SeedShippableOrderAsync(context, seed, withReservation: true);

        var logger = new CapturingLogger();
        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext, logger).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, broken, healthy),
            seed.AdminUserId,
            TestAuditContext,
            DateTime.UtcNow,
            CancellationToken.None);

        // 那一筆失敗是業務拒絕（沒有可消耗的保留），不是程式錯誤——服務把例外收成錯誤碼的設計
        // 會讓真正的缺陷長得跟業務拒絕一模一樣，所以這裡明確要求整批不留下任何 Error 記錄。
        Assert.Empty(logger.Errors);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.False(result.IsReplay);

        var brokenRow = result.Items.Single(item => item.OrderPublicId == broken.PublicId);
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, brokenRow.ErrorCode);
        Assert.Equal(1, brokenRow.SourceRowNumber);

        var healthyRow = result.Items.Single(item => item.OrderPublicId == healthy.PublicId);
        Assert.Null(healthyRow.ErrorCode);
        Assert.False(string.IsNullOrEmpty(healthyRow.TrackingNumber));
        Assert.Equal(2, healthyRow.SourceRowNumber);

        await using var verify = OrderServiceFixture.CreateContext();

        // 健康那筆真的落地了。
        Assert.True(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == healthy.Id));
        var healthyOrder = await verify.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == healthy.Id);
        Assert.Equal(FulfillmentStatus.Shipped, healthyOrder.FulfillmentStatus);

        // 失敗那筆什麼都沒留下。
        Assert.False(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == broken.Id));
        var brokenOrder = await verify.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == broken.Id);
        Assert.Equal(FulfillmentStatus.Pending, brokenOrder.FulfillmentStatus);
    }

    // ---------------------------------------------------------------------------------------
    // 組長 PR #93 review item 1：冪等鍵必須真的生效。
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 回應遺失後用同一把鍵重送，要拿回**第一次那一份**結果——同一個 BatchPublicId、同一組逐筆
    /// 結果——而且不能再出一次貨。上一版只檢查鍵非空，重送會把已成功的那幾列改報失敗
    /// （訂單已有出貨），管理員會以為出貨沒成功。
    /// </summary>
    [Fact]
    public async Task ResendingTheSameKeyAndPayloadReplaysTheOriginalResultWithoutShippingAgain()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);
        var request = Request(BatchShipmentActions.MarkShipped, order);

        await using var firstContext = OrderServiceFixture.CreateContext();
        var first = await CreateService(firstContext).ShipBatchAsync(
            request, seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(1, first.Succeeded);
        Assert.False(first.IsReplay);

        await using var secondContext = OrderServiceFixture.CreateContext();
        var replay = await CreateService(secondContext).ShipBatchAsync(
            request, seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.True(replay.IsReplay);
        Assert.Equal(first.BatchPublicId, replay.BatchPublicId);
        Assert.Equal(first.Total, replay.Total);
        Assert.Equal(first.Succeeded, replay.Succeeded);
        Assert.Equal(first.Failed, replay.Failed);
        var replayed = Assert.Single(replay.Items);
        var original = Assert.Single(first.Items);
        Assert.Equal(original.SourceRowNumber, replayed.SourceRowNumber);
        Assert.Equal(original.OrderPublicId, replayed.OrderPublicId);
        Assert.Equal(original.OrderNumber, replayed.OrderNumber);
        Assert.Equal(original.Status, replayed.Status);
        Assert.Equal(original.TrackingNumber, replayed.TrackingNumber);
        Assert.Null(replayed.ErrorCode);

        // 沒有出第二次貨：一張訂單一張物流單，保留也只被消耗一次。
        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(1, await verify.Shipments.CountAsync(candidate => candidate.OrderId == order.Id));
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(candidate =>
            candidate.ReferencePublicId == order.PublicId &&
            candidate.MovementType == InventoryMovementTypes.Ship));
    }

    /// <summary>同一把鍵配不同 payload 是呼叫端的錯，必須明確衝突而不是靜靜出另一批貨。</summary>
    [Fact]
    public async Task ReusingAKeyWithADifferentPayloadConflicts()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var first = await SeedShippableOrderAsync(context, seed, withReservation: true);
        var second = await SeedShippableOrderAsync(context, seed, withReservation: true);
        var key = $"key-{Guid.NewGuid():N}";

        await using var firstContext = OrderServiceFixture.CreateContext();
        await CreateService(firstContext).ShipBatchAsync(
            new BatchShipmentRequest([Input(first)], BatchShipmentActions.MarkShipped, key),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var secondContext = OrderServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            CreateService(secondContext).ShipBatchAsync(
                new BatchShipmentRequest([Input(second)], BatchShipmentActions.MarkShipped, key),
                seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(IdempotencyErrorCodes.PayloadConflict, exception.ErrorCode);

        // 第二張訂單完全沒被碰。
        await using var verify = OrderServiceFixture.CreateContext();
        Assert.False(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == second.Id));
    }

    /// <summary>
    /// 逐筆出貨跑完、結果卻沒寫回冪等記錄（回應遺失、進程中斷）之後再重送：記錄停在 Processing，
    /// 所以回 `idempotency_request_in_progress`。重點是**不會重複出貨**——那是這條路唯一不能出錯
    /// 的地方；要收拾殘局就換一把新的鍵重送，已出貨的會被逐筆的「已有出貨」擋下來。
    /// </summary>
    [Fact]
    public async Task AResendAfterAPartialRunNeverShipsTwice()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);
        var request = Request(BatchShipmentActions.MarkShipped, order);

        // 讓「寫回結果」那一步失敗：貨已經出了，但冪等記錄停在 Processing。
        await using (var brokenContext = OrderServiceFixture.CreateContext(new FailIdempotencyCompletionInterceptor()))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => CreateService(brokenContext).ShipBatchAsync(
                request, seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));
        }

        await using (var mid = OrderServiceFixture.CreateContext())
        {
            Assert.Equal(1, await mid.Shipments.CountAsync(candidate => candidate.OrderId == order.Id));
        }

        await using var resendContext = OrderServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            CreateService(resendContext).ShipBatchAsync(
                request, seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(IdempotencyErrorCodes.RequestInProgress, exception.ErrorCode);

        // 沒有第二張物流單。
        await using var recheck = OrderServiceFixture.CreateContext();
        Assert.Equal(1, await recheck.Shipments.CountAsync(candidate => candidate.OrderId == order.Id));
    }

    // ---------------------------------------------------------------------------------------
    // 組長 PR #93 review item 2：Active 保留要在交易內完整覆蓋各 SKU 與數量。
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 多品項訂單只保留到一半就出貨，等於出了一批帳面上沒扣掉的貨。上一版只檢查「至少有一筆
    /// Active」，這種訂單會直接通過。
    /// </summary>
    [Fact]
    public async Task AnOrderWhoseReservationsCoverOnlySomeItemsFailsThatRow()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var reserved = await OrderServiceFixture.SeedSkuWithBalanceAsync(context, reservedQuantity: 1);
        var unreserved = await OrderServiceFixture.SeedSkuWithBalanceAsync(context);

        var order = await OrderServiceFixture.SeedOrderAsync(
            context, seed.MemberUserId, seed.ProviderProfileId, OrderStatus.Confirmed,
            items: [(reserved.Id, 1), (unreserved.Id, 1)]);
        order.ApplyPaymentProjection(PaymentStatus.Paid, order.GrandTotal, DateTime.UtcNow);
        await context.SaveChangesAsync();

        // 只有一個 SKU 有 Active 保留。
        await OrderServiceFixture.SeedReservationForSkuAsync(context, order, reserved.Id, 1);

        var logger = new CapturingLogger();
        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext, logger).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, result.Items.Single().ErrorCode);
        Assert.Empty(logger.Errors);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.False(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == order.Id));
        var reservation = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == order.Id);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
    }

    /// <summary>保留的數量不足以覆蓋品項數量，也算沒有覆蓋。</summary>
    [Fact]
    public async Task AnOrderWhoseReservedQuantityIsShortFailsThatRow()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var sku = await OrderServiceFixture.SeedSkuWithBalanceAsync(context, onHandQuantity: 5, reservedQuantity: 2);

        var order = await OrderServiceFixture.SeedOrderAsync(
            context, seed.MemberUserId, seed.ProviderProfileId, OrderStatus.Confirmed,
            items: [(sku.Id, 3)]);
        order.ApplyPaymentProjection(PaymentStatus.Paid, order.GrandTotal, DateTime.UtcNow);
        await context.SaveChangesAsync();
        await OrderServiceFixture.SeedReservationForSkuAsync(context, order, sku.Id, 2);

        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, result.Items.Single().ErrorCode);
    }

    /// <summary>
    /// 交易內的覆蓋檢查通過之後、消耗之前，保留被別人收走（逾時排程、人工釋放、另一個出貨請求）。
    /// 這一筆必須整體回滾——不能出一張沒有扣庫存的貨。
    /// </summary>
    [Fact]
    public async Task AReservationTakenAwayAfterTheCoverageCheckRollsThatOrderBack()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        // 攔截交易內第一次讀 InventoryReservations，之後從另一個連線把它釋放掉。
        var interceptor = new RunAfterReservationQueryInterceptor(async () =>
        {
            await using var other = OrderServiceFixture.CreateContext();
            var reservation = await other.InventoryReservations
                .SingleAsync(candidate => candidate.OrderId == order.Id);
            reservation.Release(InventoryReleaseReasonCodes.InventoryCorrection, expired: true, DateTime.UtcNow);
            await other.SaveChangesAsync();
        });

        await using var actContext = OrderServiceFixture.CreateContext(interceptor);
        var result = await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, result.Items.Single().ErrorCode);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.False(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == order.Id));
        var reloaded = await verify.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
        Assert.Equal(FulfillmentStatus.Pending, reloaded.FulfillmentStatus);
    }

    // ---------------------------------------------------------------------------------------
    // 組長 PR #93 review item 3：createLabel 之後要接得上 markShipped。
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 先 createLabel 印單、再 markShipped 完成出貨，走的必須是同一張物流單。上一版第二步會被
    /// 「履約狀態不是 Pending」與「訂單已有出貨」擋死，而且沒有別的端點可以把它推到已出貨——
    /// 印了單的訂單等於卡住。
    /// </summary>
    [Fact]
    public async Task CreateLabelThenMarkShippedCompletesTheSameShipment()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var labelContext = OrderServiceFixture.CreateContext();
        var label = await CreateService(labelContext).ShipBatchAsync(
            Request(BatchShipmentActions.CreateLabel, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(1, label.Succeeded);
        Assert.Equal(FulfillmentStatus.Preparing.ToString(), label.Items.Single().Status);

        long shipmentId;
        byte[] preparedRowVersion;
        await using (var mid = OrderServiceFixture.CreateContext())
        {
            var shipment = await mid.Shipments.AsNoTracking().SingleAsync(candidate => candidate.OrderId == order.Id);
            shipmentId = shipment.Id;
            Assert.Equal(FulfillmentStatus.Preparing, shipment.Status);
            var reloaded = await mid.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
            preparedRowVersion = reloaded.RowVersion.ToArray();
            // createLabel 不扣庫存：貨還在倉庫。
            var reservation = await mid.InventoryReservations.AsNoTracking()
                .SingleAsync(candidate => candidate.OrderId == order.Id);
            Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        }

        await using var shipContext = OrderServiceFixture.CreateContext();
        var shipped = await CreateService(shipContext).ShipBatchAsync(
            new BatchShipmentRequest(
                [new BatchShipmentOrderInput(order.PublicId, preparedRowVersion)],
                BatchShipmentActions.MarkShipped,
                $"key-{Guid.NewGuid():N}"),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, shipped.Succeeded);
        Assert.Equal(FulfillmentStatus.Shipped.ToString(), shipped.Items.Single().Status);

        await using var verify = OrderServiceFixture.CreateContext();

        // 同一張物流單，不是新開一張。
        var finalShipment = await verify.Shipments.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == order.Id);
        Assert.Equal(shipmentId, finalShipment.Id);
        Assert.Equal(FulfillmentStatus.Shipped, finalShipment.Status);
        Assert.Equal(shipped.Items.Single().TrackingNumber, finalShipment.TrackingNumber);

        var finalOrder = await verify.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
        Assert.Equal(FulfillmentStatus.Shipped, finalOrder.FulfillmentStatus);

        // 這次才扣庫存。
        var consumed = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == order.Id);
        Assert.Equal(InventoryReservationStatus.Consumed, consumed.Status);

        // 出貨歷程走完 Pending→Preparing→Shipped 三個狀態、兩段邊。
        var history = await verify.ShipmentStatusHistories.AsNoTracking()
            .Where(candidate => candidate.ShipmentId == finalShipment.Id)
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(FulfillmentStatus.Pending, history[0].FromStatus);
        Assert.Equal(FulfillmentStatus.Preparing, history[0].ToStatus);
        Assert.Equal(FulfillmentStatus.Preparing, history[1].FromStatus);
        Assert.Equal(FulfillmentStatus.Shipped, history[1].ToStatus);
    }

    /// <summary>已經印過單的訂單再 createLabel 一次是錯的，訊息要指向 markShipped。</summary>
    [Fact]
    public async Task CreateLabelOnAnOrderThatAlreadyHasAShipmentFailsThatRow()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var labelContext = OrderServiceFixture.CreateContext();
        await CreateService(labelContext).ShipBatchAsync(
            Request(BatchShipmentActions.CreateLabel, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var againContext = OrderServiceFixture.CreateContext();
        var reloaded = await againContext.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
        var again = await CreateService(againContext).ShipBatchAsync(
            new BatchShipmentRequest(
                [new BatchShipmentOrderInput(reloaded.PublicId, reloaded.RowVersion.ToArray())],
                BatchShipmentActions.CreateLabel,
                $"key-{Guid.NewGuid():N}"),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, again.Succeeded);
        var row = again.Items.Single();
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, row.ErrorCode);
        Assert.Contains("markShipped", row.Message);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(1, await verify.Shipments.CountAsync(candidate => candidate.OrderId == order.Id));
    }

    // ---------------------------------------------------------------------------------------
    // 組長 PR #93 裁定 B1：每一筆成功出貨都要在自己那筆交易內留中央 Audit。
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task EverySuccessfulRowWritesItsOwnCentralAuditEntry()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var shipped = await SeedShippableOrderAsync(context, seed, withReservation: true);
        var broken = await SeedShippableOrderAsync(context, seed, withReservation: false);

        await using var actContext = OrderServiceFixture.CreateContext();
        await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, shipped, broken),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var verify = OrderServiceFixture.CreateContext();
        var audit = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(candidate => candidate.ResourcePublicId == shipped.PublicId);
        Assert.Equal(AuditActions.ShipmentMarkShipped, audit.Action);
        Assert.Equal(AuditResourceTypes.Order, audit.ResourceType);
        Assert.Equal(TestAuditContext.CorrelationId, audit.CorrelationId);

        // 失敗那筆沒有稽核紀錄：什麼都沒發生，就不該留下「做過了」的痕跡。
        Assert.False(await verify.AuditLogs.AnyAsync(candidate => candidate.ResourcePublicId == broken.PublicId));
    }

    /// <summary>
    /// 組長 PR #93 round 2：createLabel → markShipped 兩筆稽核的前後值要對。第一筆建立物流單，
    /// 單號從 null 變成現值；第二筆只是把同一張單推到 Shipped，單號沒動，就不該再出現在
    /// ChangesJson 裡說它是這一步才建立的。直接驗 action 與 ChangedFieldsJson 的內容，不只驗有幾筆。
    /// </summary>
    [Fact]
    public async Task CreateLabelThenMarkShippedRecordsTheRealBeforeAndAfterInEachAuditEntry()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var labelContext = OrderServiceFixture.CreateContext();
        var label = await CreateService(labelContext).ShipBatchAsync(
            Request(BatchShipmentActions.CreateLabel, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);
        var trackingNumber = label.Items.Single().TrackingNumber!;

        byte[] preparedRowVersion;
        string shipmentNumber;
        await using (var mid = OrderServiceFixture.CreateContext())
        {
            preparedRowVersion = (await mid.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id))
                .RowVersion.ToArray();
            shipmentNumber = (await mid.Shipments.AsNoTracking().SingleAsync(candidate => candidate.OrderId == order.Id))
                .ShipmentNumber;
        }

        await using var shipContext = OrderServiceFixture.CreateContext();
        await CreateService(shipContext).ShipBatchAsync(
            new BatchShipmentRequest(
                [new BatchShipmentOrderInput(order.PublicId, preparedRowVersion)],
                BatchShipmentActions.MarkShipped,
                $"key-{Guid.NewGuid():N}"),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var verify = OrderServiceFixture.CreateContext();
        var audits = await verify.AuditLogs.AsNoTracking()
            .Where(candidate => candidate.ResourcePublicId == order.PublicId)
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        Assert.Equal(2, audits.Count);

        var created = audits[0];
        Assert.Equal(AuditActions.ShipmentCreateLabel, created.Action);
        var createdChanges = Changes(created.ChangedFieldsJson);
        Assert.Equal(("Pending", "Preparing"), createdChanges["fulfillmentStatus"]);
        Assert.Equal((null, shipmentNumber), createdChanges["shipmentNumber"]);
        Assert.Equal((null, trackingNumber), createdChanges["trackingNumber"]);

        var shipped = audits[1];
        Assert.Equal(AuditActions.ShipmentMarkShipped, shipped.Action);
        var shippedChanges = Changes(shipped.ChangedFieldsJson);
        Assert.Equal(("Preparing", "Shipped"), shippedChanges["fulfillmentStatus"]);
        // 單號在第二步沒有變，就不該被記成「這一步才建立」。
        Assert.DoesNotContain("shipmentNumber", shippedChanges.Keys);
        Assert.DoesNotContain("trackingNumber", shippedChanges.Keys);
    }

    /// <summary>一次走完 Pending→Shipped 的 markShipped 只有一筆稽核，單號在那一筆裡從 null 建立。</summary>
    [Fact]
    public async Task MarkShippedOnAFreshOrderRecordsTheShipmentNumbersAsCreated()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var verify = OrderServiceFixture.CreateContext();
        var audit = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(candidate => candidate.ResourcePublicId == order.PublicId);
        var changes = Changes(audit.ChangedFieldsJson);
        Assert.Equal(("Pending", "Shipped"), changes["fulfillmentStatus"]);
        Assert.Equal((null, result.Items.Single().TrackingNumber), changes["trackingNumber"]);
        Assert.Null(changes["shipmentNumber"].Before);
        Assert.NotNull(changes["shipmentNumber"].After);
    }

    /// <summary>ChangedFieldsJson 的信封是 EfAuditWriter 私有的；用不分大小寫的方式讀，不與序列化選項綁死。</summary>
    private static Dictionary<string, (string? Before, string? After)> Changes(string changedFieldsJson)
    {
        using var document = JsonDocument.Parse(changedFieldsJson);
        var changes = Property(document.RootElement, "Changes");
        var result = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (var change in changes.EnumerateArray())
        {
            var field = Property(change, "Field").GetString()!;
            var before = Property(change, "BeforeCode");
            var after = Property(change, "AfterCode");
            result[field] = (
                before.ValueKind == JsonValueKind.Null ? null : before.GetString(),
                after.ValueKind == JsonValueKind.Null ? null : after.GetString());
        }

        return result;
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject()
            .First(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            .Value;

    [Fact]
    public async Task CreateLabelWritesTheCreateLabelAuditAction()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.CreateLabel, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var verify = OrderServiceFixture.CreateContext();
        var audit = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(candidate => candidate.ResourcePublicId == order.PublicId);
        Assert.Equal(AuditActions.ShipmentCreateLabel, audit.Action);
    }

    // ---------------------------------------------------------------------------------------
    // 既有的整批與逐筆守衛。
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 「markShipped 才把 Active Reservation 轉 Consumed 並扣實體庫存」——createLabel 只是印單，
    /// 貨還在倉庫裡。提前扣庫存等於帳面上少了一批根本還沒出門的貨。
    /// </summary>
    [Fact]
    public async Task CreateLabelDoesNotConsumeInventoryButMarkShippedDoes()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var labelOnly = await SeedShippableOrderAsync(context, seed, withReservation: true);
        var shipped = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var labelContext = OrderServiceFixture.CreateContext();
        await CreateService(labelContext).ShipBatchAsync(
            Request(BatchShipmentActions.CreateLabel, labelOnly),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var shipContext = OrderServiceFixture.CreateContext();
        await CreateService(shipContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, shipped),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var verify = OrderServiceFixture.CreateContext();

        var labelReservation = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == labelOnly.Id);
        Assert.Equal(InventoryReservationStatus.Active, labelReservation.Status);

        var shippedReservation = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == shipped.Id);
        Assert.Equal(InventoryReservationStatus.Consumed, shippedReservation.Status);
    }

    /// <summary>
    /// 「超過上限時整個 Request 回傳驗證錯誤，不開始逐筆出貨」——不是「處理前 100 筆」，是一筆
    /// 都不碰。
    /// </summary>
    [Fact]
    public async Task RejectsTheWholeRequestOverTheHundredOrderLimitWithoutShippingAnything()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var real = await SeedShippableOrderAsync(context, seed, withReservation: true);

        var orders = new List<BatchShipmentOrderInput> { Input(real) };
        while (orders.Count <= 100)
        {
            orders.Add(new BatchShipmentOrderInput(Guid.CreateVersion7(), [1, 2, 3, 4, 5, 6, 7, 8]));
        }

        await using var actContext = OrderServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(actContext).ShipBatchAsync(
                new BatchShipmentRequest(orders, BatchShipmentActions.MarkShipped, "key-1"),
                seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingErrorCodes.ShippingBatchLimitExceeded, exception.Code);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.False(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == real.Id));
    }

    /// <summary>訂單已經出貨完成時只有那一筆失敗，錯誤碼要是穩定的 `shipping_order_not_ready`。</summary>
    [Fact]
    public async Task AnOrderThatAlreadyShippedFailsThatRowOnly()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var firstContext = OrderServiceFixture.CreateContext();
        var first = await CreateService(firstContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(1, first.Succeeded);

        await using var secondContext = OrderServiceFixture.CreateContext();
        var reloaded = await secondContext.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == order.Id);
        var second = await CreateService(secondContext).ShipBatchAsync(
            new BatchShipmentRequest(
                [new BatchShipmentOrderInput(reloaded.PublicId, reloaded.RowVersion.ToArray())],
                BatchShipmentActions.MarkShipped,
                $"key-{Guid.NewGuid():N}"),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, second.Succeeded);
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, second.Items.Single().ErrorCode);
    }

    /// <summary>
    /// 超商取貨的門市被停用時，那一筆不能出貨。這是業務拒絕不是伺服器錯誤——如果它是以例外
    /// 表達的，就會多留一筆 Error 記錄，把真正的缺陷淹沒在正常的拒絕裡。
    /// </summary>
    [Fact]
    public async Task AnOrderWhosePickupStoreIsDeactivatedFailsThatRowWithoutLoggingAnError()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);

        var storeCode = $"ST-{Guid.NewGuid():N}"[..12];
        var store = new DoSelect.Domain.Shipping.ConvenienceStore(
            Guid.CreateVersion7(), "StorePickup", storeCode, "測試門市",
            "台北市中正區測試路 1 號", "台北市", "中正區", isDemoData: true, DateTime.UtcNow);
        store.SetActive(false, DateTime.UtcNow);
        context.ConvenienceStores.Add(store);
        await context.SaveChangesAsync();

        var order = await SeedShippableOrderAsync(context, seed, withReservation: true, storeCode: storeCode);

        var logger = new CapturingLogger();
        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext, logger).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, result.Items.Single().ErrorCode);
        Assert.Empty(logger.Errors);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.False(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == order.Id));
    }

    /// <summary>RowVersion 過期只讓那一筆失敗，不是整批爆掉。</summary>
    [Fact]
    public async Task AStaleRowVersionFailsOnlyThatRow()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var stale = await SeedShippableOrderAsync(context, seed, withReservation: true);
        var staleRowVersion = stale.RowVersion.ToArray();
        var healthy = await SeedShippableOrderAsync(context, seed, withReservation: true);

        // 讓第一筆的 RowVersion 過期。
        await using (var other = OrderServiceFixture.CreateContext())
        {
            var concurrent = await other.Orders.SingleAsync(candidate => candidate.Id == stale.Id);
            concurrent.ChangeOrderStatus(OrderStatus.Processing, DateTime.UtcNow);
            await other.SaveChangesAsync();
        }

        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext).ShipBatchAsync(
            new BatchShipmentRequest(
                [
                    new BatchShipmentOrderInput(stale.PublicId, staleRowVersion),
                    Input(healthy),
                ],
                BatchShipmentActions.MarkShipped,
                $"key-{Guid.NewGuid():N}"),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(DomainErrorCodes.ConcurrencyConflict,
            result.Items.Single(item => item.OrderPublicId == stale.PublicId).ErrorCode);
        Assert.Null(result.Items.Single(item => item.OrderPublicId == healthy.PublicId).ErrorCode);
    }

    [Fact]
    public async Task RejectsTheSameOrderTwiceInOneBatch()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(actContext).ShipBatchAsync(
                Request(BatchShipmentActions.MarkShipped, order, order),
                seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));
    }

    [Theory]
    [InlineData("shipItAll")]
    [InlineData("")]
    public async Task RejectsAnUnknownShippingAction(string action)
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(actContext).ShipBatchAsync(
                Request(action, order),
                seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None));
    }

    /// <summary>出貨完成要留下狀態歷程與通知事件，那是「同一筆交易中寫入狀態歷程及 Outbox」。</summary>
    [Fact]
    public async Task ShippingWritesTheStatusHistoryAndNotificationInTheSameTransaction()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var seed = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, seed, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            seed.AdminUserId, TestAuditContext, DateTime.UtcNow, CancellationToken.None);

        await using var verify = OrderServiceFixture.CreateContext();
        var shipment = await verify.Shipments.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == order.Id);
        Assert.True(await verify.ShipmentStatusHistories.AsNoTracking()
            .AnyAsync(candidate => candidate.ShipmentId == shipment.Id));
        Assert.True(await verify.OutboxMessages.AsNoTracking()
            .AnyAsync(candidate => candidate.AggregatePublicId == order.PublicId));
    }

    // ---------------------------------------------------------------------------------------
    // 種子與測試替身。
    // ---------------------------------------------------------------------------------------

    private sealed record SeedContext(string MemberUserId, string AdminUserId, long ProviderProfileId);

    private static BatchShipmentOrderInput Input(Order order) =>
        new(order.PublicId, order.RowVersion.ToArray());

    private static BatchShipmentRequest Request(string action, params Order[] orders) =>
        new(orders.Select(Input).ToArray(), action, $"key-{Guid.NewGuid():N}");

    private static async Task<SeedContext> SeedBaseAsync(DoSelectDbContext context)
    {
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var adminUserId = await SeedOrderManagerAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        await OrderServiceFixture.SeedShippingMethodAsync(context);
        return new SeedContext(memberUserId, adminUserId, provider.Id);
    }

    /// <summary>
    /// 出貨的稽核 Actor 與冪等的 Actor Scope 都來自這個身分，所以必須是真的持有 OrderManager 的
    /// 管理員帳號——`OrderStatusHistories.ActorUserId` 對 AspNetUsers 也有外鍵。
    /// </summary>
    private static async Task<string> SeedOrderManagerAsync(DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        var role = await context.Roles.SingleOrDefaultAsync(
            candidate => candidate.Name == AuditRoleNames.OrderManager);
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

    /// <summary>
    /// 一張可以出貨的訂單：Confirmed、已付款、無需組裝，並依需要帶一筆覆蓋得剛好的 Active 保留。
    /// `withReservation: false` 就是「不符出貨條件」的那一種——服務會以 shipping_order_not_ready 拒絕。
    /// </summary>
    private static async Task<Order> SeedShippableOrderAsync(
        DoSelectDbContext context,
        SeedContext seed,
        bool withReservation,
        string? storeCode = null)
    {
        var sku = await OrderServiceFixture.SeedSkuWithBalanceAsync(
            context, onHandQuantity: 5, reservedQuantity: withReservation ? 1 : 0);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, seed.MemberUserId, seed.ProviderProfileId, OrderStatus.Confirmed,
            storeCode: storeCode, items: [(sku.Id, 1)]);
        order.ApplyPaymentProjection(PaymentStatus.Paid, order.GrandTotal, DateTime.UtcNow);
        await context.SaveChangesAsync();

        if (withReservation)
        {
            await OrderServiceFixture.SeedReservationForSkuAsync(context, order, sku.Id, 1);
        }

        return order;
    }

    private static EfBatchShipmentService CreateService(
        DoSelectDbContext context,
        ILogger<EfBatchShipmentService>? logger = null) =>
        new(
            context,
            new EfInventoryReservationService(context, new EfAuditWriter(context, TimeProvider.System)),
            new EfOutboxWriter(context, TimeProvider.System),
            new EfAuditWriter(context, TimeProvider.System),
            new BatchShipmentIdempotency(context, Options.Create(new IdempotencyOptions
            {
                ActorScopePepper = "batch-shipment-tests-pepper-0123456789abcdef",
            })),
            logger ?? NullLogger<EfBatchShipmentService>.Instance);

    /// <summary>
    /// 服務把「這一筆的例外」收成一列錯誤碼，好讓其他訂單繼續出貨。代價是程式錯誤會偽裝成業務拒絕
    /// ——`shipping_order_not_ready` 同時是「訂單還沒付款」和「我把 Shipment 狀態機走錯了」。
    /// 因此測試要盯著 Error 這一層：業務拒絕不該留下 Error 記錄，留下的就是缺陷。
    /// </summary>
    private sealed class CapturingLogger : ILogger<EfBatchShipmentService>
    {
        public List<(Exception? Exception, string Message)> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add((exception, formatter(state, exception)));
            }
        }
    }

    /// <summary>「貨出了，但結果沒寫回冪等記錄」——讓寫回那一步的資料庫命令失敗。</summary>
    private sealed class FailIdempotencyCompletionInterceptor : DbCommandInterceptor
    {
        private static bool IsCompletionWrite(DbCommand command) =>
            command.CommandText.Contains("IdempotencyRecords", StringComparison.Ordinal) &&
            command.CommandText.Contains("UPDATE", StringComparison.Ordinal);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsCompletionWrite(command))
            {
                throw new InvalidOperationException("Simulated failure while storing the batch result.");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsCompletionWrite(command))
            {
                throw new InvalidOperationException("Simulated failure while storing the batch result.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>交易內第一次讀 InventoryReservations 之後執行一次外部動作，用來製造競態。</summary>
    private sealed class RunAfterReservationQueryInterceptor(Func<Task> action) : DbCommandInterceptor
    {
        private bool _fired;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!_fired &&
                command.CommandText.Contains("InventoryReservations", StringComparison.Ordinal) &&
                command.CommandText.Contains("SELECT", StringComparison.Ordinal))
            {
                _fired = true;
                await action();
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
