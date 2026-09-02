using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Inventory;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;
using DoSelect.Infrastructure.Tests.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DoSelect.Infrastructure.Tests.Shipping;

/// <summary>
/// UC-ADM-SHIP-02 批次出貨。對真實 SQL Server 驗證，因為這裡要證明的正是 InMemory Provider 上不
/// 存在的事：每筆訂單各自的交易真的獨立提交，一筆回滾不會把另一筆已成功的出貨帶走。
/// </summary>
[Collection(nameof(OrderServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfBatchShipmentServiceTests
{
    private readonly OrderServiceFixture _fixture;

    public EfBatchShipmentServiceTests(OrderServiceFixture fixture) => _fixture = fixture;

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
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);

        var broken = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: false);
        var healthy = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        var logger = new CapturingLogger();
        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext, logger).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, broken, healthy),
            adminUserId,
            correlationId: "batch-test",
            DateTime.UtcNow,
            CancellationToken.None);

        // 那一筆失敗是業務拒絕（沒有可消耗的保留），不是程式錯誤——服務把例外收成錯誤碼的設計
        // 會讓真正的缺陷長得跟業務拒絕一模一樣，所以這裡明確要求整批不留下任何 Error 記錄。
        Assert.Empty(logger.Errors);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);

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

    /// <summary>
    /// 「markShipped 才把 Active Reservation 轉 Consumed 並扣實體庫存」——createLabel 只是印單，
    /// 貨還在倉庫裡。提前扣庫存等於帳面上少了一批根本還沒出門的貨。
    /// </summary>
    [Fact]
    public async Task CreateLabelDoesNotConsumeInventoryButMarkShippedDoes()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var labelOnly = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);
        var shipped = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        await using var labelContext = OrderServiceFixture.CreateContext();
        await CreateService(labelContext).ShipBatchAsync(
            Request(BatchShipmentActions.CreateLabel, labelOnly),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);

        await using var shipContext = OrderServiceFixture.CreateContext();
        await CreateService(shipContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, shipped),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);

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
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var real = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        var orders = new List<BatchShipmentOrderInput>
        {
            new(real.PublicId, real.RowVersion.ToArray()),
        };
        while (orders.Count <= 100)
        {
            orders.Add(new BatchShipmentOrderInput(Guid.CreateVersion7(), [1, 2, 3, 4, 5, 6, 7, 8]));
        }

        await using var actContext = OrderServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(actContext).ShipBatchAsync(
                new BatchShipmentRequest(orders, BatchShipmentActions.MarkShipped, "key-1"),
                adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingErrorCodes.ShippingBatchLimitExceeded, exception.Code);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.False(await verify.Shipments.AnyAsync(candidate => candidate.OrderId == real.Id));
    }

    /// <summary>訂單已經有出貨時只有那一筆失敗，錯誤碼要是穩定的 `shipping_order_not_ready`。</summary>
    [Fact]
    public async Task AnOrderThatAlreadyShippedFailsThatRowOnly()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        await using var firstContext = OrderServiceFixture.CreateContext();
        var first = await CreateService(firstContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(1, first.Succeeded);

        await using var secondContext = OrderServiceFixture.CreateContext();
        var reloaded = await secondContext.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == order.Id);
        var second = await CreateService(secondContext).ShipBatchAsync(
            new BatchShipmentRequest(
                [new BatchShipmentOrderInput(reloaded.PublicId, reloaded.RowVersion.ToArray())],
                BatchShipmentActions.MarkShipped,
                "key-2"),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, second.Succeeded);
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, second.Items.Single().ErrorCode);
    }

    /// <summary>
    /// 「一張訂單只出一次貨」這道守衛要自己站得住。上一支測試其實是被 FulfillmentStatus 攔下來的
    /// ——反向驗證時把出貨存在性檢查拿掉，它照樣綠燈。這裡直接種一列出貨、訂單仍停在 Pending，
    /// 逼那道檢查單獨表態，免得它變成沒人守著的裝飾。
    /// </summary>
    [Fact]
    public async Task AnExistingShipmentBlocksASecondOneEvenWhenTheOrderStillLooksPending()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        var method = await OrderServiceFixture.SeedShippingMethodAsync(context);
        var existing = new DoSelect.Domain.Shipping.Shipment(
            Guid.CreateVersion7(),
            order.Id,
            method.Id,
            order.ShippingProviderProfileVersionId,
            convenienceStoreId: null,
            shipmentNumber: $"SH-EXISTING-{Guid.NewGuid():N}"[..24],
            feeSnapshot: 0m,
            createdAtUtc: DateTime.UtcNow);
        existing.SetTrackingNumber($"EXIST{Guid.NewGuid():N}"[..20], DateTime.UtcNow);
        context.Shipments.Add(existing);
        await context.SaveChangesAsync();

        Assert.Equal(FulfillmentStatus.Pending, order.FulfillmentStatus);

        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(ShippingErrorCodes.ShippingOrderNotReady, result.Items.Single().ErrorCode);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(1, await verify.Shipments.CountAsync(candidate => candidate.OrderId == order.Id));
    }

    /// <summary>
    /// 超商取貨的門市被停用時，那一筆不能出貨。這是業務拒絕不是伺服器錯誤——如果它是以例外
    /// 表達的，就會多留一筆 Error 記錄，把真正的缺陷淹沒在正常的拒絕裡。
    /// </summary>
    [Fact]
    public async Task AnOrderWhosePickupStoreIsDeactivatedFailsThatRowWithoutLoggingAnError()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);

        var storeCode = $"ST-{Guid.NewGuid():N}"[..12];
        var store = new DoSelect.Domain.Shipping.ConvenienceStore(
            Guid.CreateVersion7(), "StorePickup", storeCode, "測試門市",
            "台北市中正區測試路 1 號", "台北市", "中正區", isDemoData: true, DateTime.UtcNow);
        store.SetActive(false, DateTime.UtcNow);
        context.ConvenienceStores.Add(store);
        await context.SaveChangesAsync();

        var order = await SeedShippableOrderAsync(
            context, memberUserId, provider.Id, withReservation: true, storeCode: storeCode);

        var logger = new CapturingLogger();
        await using var actContext = OrderServiceFixture.CreateContext();
        var result = await CreateService(actContext, logger).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);

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
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var stale = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);
        var staleRowVersion = stale.RowVersion.ToArray();
        var healthy = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

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
                    new BatchShipmentOrderInput(healthy.PublicId, healthy.RowVersion.ToArray()),
                ],
                BatchShipmentActions.MarkShipped,
                "key-3"),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(DomainErrorCodes.ConcurrencyConflict,
            result.Items.Single(item => item.OrderPublicId == stale.PublicId).ErrorCode);
        Assert.Null(result.Items.Single(item => item.OrderPublicId == healthy.PublicId).ErrorCode);
    }

    [Fact]
    public async Task RejectsTheSameOrderTwiceInOneBatch()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(actContext).ShipBatchAsync(
                Request(BatchShipmentActions.MarkShipped, order, order),
                adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None));
    }

    [Theory]
    [InlineData("shipItAll")]
    [InlineData("")]
    public async Task RejectsAnUnknownShippingAction(string action)
    {
        await using var context = OrderServiceFixture.CreateContext();
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(actContext).ShipBatchAsync(
                Request(action, order),
                adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None));
    }

    /// <summary>出貨完成要留下狀態歷程與通知事件，那是「同一筆交易中寫入狀態歷程及 Outbox」。</summary>
    [Fact]
    public async Task ShippingWritesTheStatusHistoryAndNotificationInTheSameTransaction()
    {
        await using var context = OrderServiceFixture.CreateContext();
        var (memberUserId, adminUserId, provider) = await SeedBaseAsync(context);
        var order = await SeedShippableOrderAsync(context, memberUserId, provider.Id, withReservation: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        await CreateService(actContext).ShipBatchAsync(
            Request(BatchShipmentActions.MarkShipped, order),
            adminUserId, "batch-test", DateTime.UtcNow, CancellationToken.None);

        await using var verify = OrderServiceFixture.CreateContext();
        var shipment = await verify.Shipments.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == order.Id);
        Assert.True(await verify.ShipmentStatusHistories.AsNoTracking()
            .AnyAsync(candidate => candidate.ShipmentId == shipment.Id));
        Assert.True(await verify.OutboxMessages.AsNoTracking()
            .AnyAsync(candidate => candidate.AggregatePublicId == order.PublicId));
    }

    private static BatchShipmentRequest Request(string action, params Order[] orders) =>
        new(
            orders.Select(order =>
                new BatchShipmentOrderInput(order.PublicId, order.RowVersion.ToArray())).ToArray(),
            action,
            $"key-{Guid.NewGuid():N}");

    /// <summary>
    /// `OrderStatusHistory.ActorUserId` 對 AspNetUsers 有外鍵，所以管理員必須是真的存在的使用者，
    /// 不能拿一個字面值敷衍過去——那會在寫歷程時炸成 FK 衝突，然後被服務收成一列業務錯誤碼。
    /// </summary>
    private static async Task<(string MemberUserId, string AdminUserId,
        DoSelect.Domain.Shipping.ShippingProviderProfile Provider)>
        SeedBaseAsync(DoSelectDbContext context)
    {
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var adminUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        await OrderServiceFixture.SeedShippingMethodAsync(context);
        return (memberUserId, adminUserId, provider);
    }

    /// <summary>
    /// 一張可以出貨的訂單：Confirmed、已付款、無需組裝，並依需要帶一筆 Active 保留。
    /// `withReservation: false` 就是「不符出貨條件」的那一種——服務會以 shipping_order_not_ready 拒絕。
    /// </summary>
    private static async Task<Order> SeedShippableOrderAsync(
        DoSelectDbContext context,
        string memberUserId,
        long providerProfileId,
        bool withReservation,
        string? storeCode = null)
    {
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, providerProfileId, OrderStatus.Confirmed, storeCode: storeCode);
        order.ApplyPaymentProjection(PaymentStatus.Paid, order.GrandTotal, DateTime.UtcNow);
        await context.SaveChangesAsync();

        if (withReservation)
        {
            await OrderServiceFixture.SeedInventoryReservationAsync(context, order);
        }

        return order;
    }

    private static EfBatchShipmentService CreateService(
        DoSelectDbContext context,
        ILogger<EfBatchShipmentService>? logger = null) =>
        new(
            context,
            new EfInventoryReservationService(context),
            new EfOutboxWriter(context, TimeProvider.System),
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
}
