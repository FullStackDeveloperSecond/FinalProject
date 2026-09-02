using DoSelect.Application.Orders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Orders;

/// <summary>
/// M-10 逾時取消（庫存規則.md）。組長 PR #85 round-1 review [P1]／[P2] 的回歸測試。
///
/// 全部對真實 SQL Server 驗證：這裡要證明的正是 InMemory Provider 上不存在的東西——RowVersion
/// 樂觀鎖真的會讓付款與排程只有一個成功，以及整批回滾真的沒有留下半套資源。
/// </summary>
[Collection(nameof(OrderServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class OrderTimeoutCancellationServiceTests
{
    private readonly OrderServiceFixture _fixture;

    public OrderTimeoutCancellationServiceTests(OrderServiceFixture fixture) => _fixture = fixture;

    /// <summary>
    /// [P1] 的核心：逾時處理的單位是訂單，不是保留。訂單要進 Cancelled，庫存保留、Balance 的
    /// ReservedQuantity 與優惠券座位都要在同一次提交裡回收。
    /// </summary>
    [Fact]
    public async Task CancelsAnOverdueOrderAndReleasesEveryReservedResource()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);
        var (balance, reservation) = await OrderServiceFixture.SeedInventoryReservationAsync(context, order);
        var (coupon, redemption) = await OrderServiceFixture.SeedCouponReservationAsync(
            context, order, memberUserId, markExhausted: true);

        await using var actContext = OrderServiceFixture.CreateContext();
        var sweep = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, after: null, CancellationToken.None);

        Assert.Equal(1, sweep.Cancelled);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(OrderStatus.Cancelled, await StatusOf(verify, order.PublicId));

        var reloadedReservation = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == reservation.Id);
        Assert.Equal(InventoryReservationStatus.Released, reloadedReservation.Status);

        var reloadedBalance = await verify.InventoryBalances.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == balance.Id);
        Assert.Equal(0, reloadedBalance.ReservedQuantity);

        var reloadedRedemption = await verify.CouponRedemptions.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == redemption.Id);
        Assert.Equal(CouponRedemptionStatus.Released, reloadedRedemption.Status);

        // 座位還回去之後，原本因為額度用盡而 Exhausted 的券要重新可用。
        var reloadedCoupon = await verify.Coupons.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == coupon.Id);
        Assert.NotEqual(CouponStatus.Exhausted, reloadedCoupon.Status);

        // 系統動作也要留稽核，不能因為沒有操作者就不記。
        var audit = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.ResourcePublicId == order.PublicId && log.Action == AuditActions.OrderCancel);
        Assert.Equal(AuditActorType.System, audit.ActorType);
        Assert.Equal(EfOrderTimeoutCancellationService.ReasonCode, audit.Reason);
    }

    /// <summary>付款期限還沒到的訂單一筆都不能碰。</summary>
    [Fact]
    public async Task LeavesOrdersInsideTheirPaymentWindowAlone()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);

        await using var actContext = OrderServiceFixture.CreateContext();
        var sweep = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            DateTime.UtcNow, batchSize: 100, after: null, CancellationToken.None);

        Assert.Equal(0, sweep.Cancelled);
        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(OrderStatus.PendingPayment, await StatusOf(verify, order.PublicId));
    }

    /// <summary>
    /// 期限過了、但付款趕在最後一刻成功的訂單（已經是 Confirmed）絕不能被掃到——那才是
    /// [P1] 所說壞結局的穩態版本：訂單已付款，庫存保留必須留著。
    ///
    /// 這支是狀態過濾那道條件的守門測試；`LeavesOrdersInsideTheirPaymentWindowAlone` 靠的是期限
    /// 而不是狀態，所以擋不住這個情境（反向驗證時發現的）。
    /// </summary>
    [Fact]
    public async Task LeavesAlreadyConfirmedOrdersAloneEvenPastTheDeadline()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.Confirmed);
        var (_, reservation) = await OrderServiceFixture.SeedInventoryReservationAsync(context, order);

        await using var actContext = OrderServiceFixture.CreateContext();
        var sweep = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, after: null, CancellationToken.None);

        Assert.Equal(0, sweep.Cancelled);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(OrderStatus.Confirmed, await StatusOf(verify, order.PublicId));
        var reloaded = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == reservation.Id);
        Assert.Equal(InventoryReservationStatus.Active, reloaded.Status);
    }

    /// <summary>
    /// 貨到付款沒有付款期限（PaymentDueAtUtc 為 null，見 EfCheckoutTransactionGateway），永遠不該被
    /// 逾時取消。SeedOrderAsync 一律給 15 分鐘，所以這裡直接把欄位改成 NULL 來重現那個情境。
    /// </summary>
    [Fact]
    public async Task LeavesOrdersWithNoPaymentDeadlineAlone()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);
        await context.Database.ExecuteSqlAsync(
            $"UPDATE [Orders] SET [PaymentDueAtUtc] = NULL WHERE [Id] = {order.Id}");

        await using var actContext = OrderServiceFixture.CreateContext();
        var sweep = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, after: null, CancellationToken.None);

        Assert.Equal(0, sweep.Cancelled);
        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(OrderStatus.PendingPayment, await StatusOf(verify, order.PublicId));
    }

    /// <summary>
    /// [P1] 的競態：付款先讀到 PendingPayment、排程接著動手、付款最後提交。組長描述的壞結局是
    /// 「已付款卻沒有有效庫存保留的訂單」。
    ///
    /// 這裡把付款端取樣成「另一個 DbContext 把同一筆訂單轉成 Confirmed 並提交」——那正是
    /// SimulatedPaymentWriter 對訂單列做的事（`order.ChangeOrderStatus(nextOrderStatus, now)` 後
    /// 一次 SaveChangesAsync）。仲裁者是訂單列的 RowVersion，兩條路徑都寫它，所以在這個接縫上取樣
    /// 與跑完整付款管線得到的結論相同，而不必把付款嘗試、planner、冪等執行器與 Outbox 整套搬進
    /// 這個 fixture。
    ///
    /// 斷言寫成不變量而不是「誰贏」：訂單是 Confirmed 就必須還有 Active 保留，是 Cancelled 就必須
    /// 已經釋放。兩種結局都可以接受，唯獨不能出現 Confirmed 配上已釋放的保留。
    /// </summary>
    [Fact]
    public async Task WhenPaymentConfirmsTheOrderConcurrently_NeverLeavesAPaidOrderWithoutItsReservation()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);
        var (_, reservation) = await OrderServiceFixture.SeedInventoryReservationAsync(context, order);

        await using var sweepContext = OrderServiceFixture.CreateContext();
        await using var paymentContext = OrderServiceFixture.CreateContext();

        // 兩邊都在對方提交之前讀到 PendingPayment——這就是期限邊界上的實際情形。
        var paymentOrder = await paymentContext.Orders.SingleAsync(candidate => candidate.Id == order.Id);
        Assert.Equal(OrderStatus.PendingPayment, paymentOrder.OrderStatus);

        var sweep = CreateService(sweepContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, after: null, CancellationToken.None);
        var payment = ConfirmLikeThePaymentWriterAsync(paymentContext, paymentOrder);

        await Task.WhenAll(sweep, payment);
        var paymentWon = await payment == 1;

        await using var verify = OrderServiceFixture.CreateContext();
        var finalStatus = await StatusOf(verify, order.PublicId);
        var finalReservation = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == reservation.Id);

        // 勝負以這一筆訂單的最終狀態判斷，不用掃描的彙總計數——這個 fixture 的資料庫在整個
        // collection 內共用，計數會把別的測試留下的訂單算進來（第一版就是這樣誤判成「兩邊都贏」）。
        if (paymentWon)
        {
            Assert.Equal(OrderStatus.Confirmed, finalStatus);
            // 這就是 [P1] 要防的壞結局：已付款卻沒有有效保留。
            Assert.Equal(InventoryReservationStatus.Active, finalReservation.Status);
        }
        else
        {
            Assert.Equal(OrderStatus.Cancelled, finalStatus);
            Assert.Equal(InventoryReservationStatus.Released, finalReservation.Status);
        }
    }

    /// <summary>
    /// [P2]：停機累積的 backlog 要能分批清完，而不是一次全載入。批次大小 2、五筆逾時訂單——呼叫端
    /// 反覆呼叫直到回傳值小於批次大小，最後五筆都要處理完，而且每一輪最多只碰 2 筆。
    /// </summary>
    [Fact]
    public async Task DrainsABacklogAcrossBatchesWithoutLoadingItAllAtOnce()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var orders = new List<Order>();
        for (var index = 0; index < 5; index++)
        {
            orders.Add(await OrderServiceFixture.SeedOrderAsync(
                context, memberUserId, provider.Id, OrderStatus.PendingPayment));
        }

        var now = PastTheDeadline();
        var rounds = new List<int>();
        OrderTimeoutCursor? cursor = null;
        int cancelledThisRound;
        do
        {
            await using var roundContext = OrderServiceFixture.CreateContext();
            var round = await CreateService(roundContext).CancelOverduePendingPaymentOrdersAsync(
                now, batchSize: 2, cursor, CancellationToken.None);
            cursor = round.NextCursor;
            cancelledThisRound = round.Cancelled;
            rounds.Add(round.Examined);
        }
        while (cancelledThisRound == 2 && rounds.Count < 10);

        // 一輪最多 2 筆——沒有任何一輪把整個 backlog 吃進來。
        Assert.All(rounds, count => Assert.True(count <= 2, $"a batch of 2 returned {count}"));
        Assert.True(rounds.Count >= 3, $"five orders at two per batch needs at least three rounds, took {rounds.Count}");

        await using var verify = OrderServiceFixture.CreateContext();
        foreach (var seeded in orders)
        {
            Assert.Equal(OrderStatus.Cancelled, await StatusOf(verify, seeded.PublicId));
        }
    }

    /// <summary>
    /// 組長 PR #85 round-2 review [P1]：一次把整批 Order 當成 tracked entity 載入，而清理路徑呼叫
    /// 的 ChangeTracker.Clear() 會連帶 detach 批次裡尚未處理的訂單。後面那些訂單照樣跑完流程——
    /// Releaser 查出的 Reservation／Balance／Coupon 與新增的 History／Audit 都是這一輪才追蹤的，
    /// 會被提交；唯獨 detached 的 order.ChangeOrderStatus(Cancelled) 不會。結果就是資源釋放了、
    /// 訂單卻還停在 PendingPayment。
    ///
    /// 這支測試不靠時序：第一筆刻意種成庫存不一致（Balance 的 Reserved 少於該訂單的保留量），
    /// Releaser 會丟 InvalidOperationException 走清理路徑；第二筆是健康的，必須完整取消——
    /// 訂單進 Cancelled **而且** 保留被釋放，兩件事在同一次 SaveChanges 裡。
    /// </summary>
    [Fact]
    public async Task WhenAnEarlierOrderHitsCleanup_TheNextOrderStillCancelsAndReleasesTogether()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);

        // 先種的 Id 較小，所以在 (PaymentDueAtUtc, Id) 排序下一定先被處理。
        var broken = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);
        var (brokenBalance, brokenReservation) =
            await OrderServiceFixture.SeedInventoryReservationAsync(context, broken);

        // 讓 Balance 的 Reserved 少於這筆訂單的保留量：Releaser 認定庫存狀態不一致而拋出。
        await context.Database.ExecuteSqlAsync(
            $"UPDATE [InventoryBalances] SET [ReservedQuantity] = 0 WHERE [Id] = {brokenBalance.Id}");

        var healthy = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);
        var (_, healthyReservation) =
            await OrderServiceFixture.SeedInventoryReservationAsync(context, healthy);

        await using var actContext = OrderServiceFixture.CreateContext();
        var sweep = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, after: null, CancellationToken.None);

        Assert.Equal(1, sweep.Cancelled);

        await using var verify = OrderServiceFixture.CreateContext();

        // 壞掉的那筆完全沒動——不能只釋放資源卻留下 PendingPayment 的訂單。
        Assert.Equal(OrderStatus.PendingPayment, await StatusOf(verify, broken.PublicId));
        var brokenReloaded = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == brokenReservation.Id);
        Assert.Equal(InventoryReservationStatus.Active, brokenReloaded.Status);

        // 第二筆必須兩件事都成立，而不是只提交了資源變更。
        Assert.Equal(OrderStatus.Cancelled, await StatusOf(verify, healthy.PublicId));
        var healthyReloaded = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == healthyReservation.Id);
        Assert.Equal(InventoryReservationStatus.Released, healthyReloaded.Status);
    }

    /// <summary>
    /// 組長 PR #85 round-3 review：上一版這支測試在呼叫服務「之前」就把訂單改成 Confirmed，於是
    /// 第一段 ID 查詢根本查不到它——拿掉逐筆重新載入的新鮮度檢查照樣是綠的，等於什麼都沒保護到。
    ///
    /// 改用 interceptor 當接縫：ID 查詢跑完的那一刻，由另一個 DbContext 把訂單推進 Confirmed。
    /// 這樣服務手上拿到的是一個「查詢時還是 PendingPayment、重新載入時已經不是」的 ID，正是這個
    /// 檢查存在的理由。
    /// </summary>
    [Fact]
    public async Task WhenAnOrderLeavesPendingPaymentBetweenTheQueryAndTheReload_ItIsSkipped()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);
        var order = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);
        var (_, reservation) = await OrderServiceFixture.SeedInventoryReservationAsync(context, order);

        // ID 查詢是這個服務對 Orders 下的第一道 SELECT；攔到它之後才動手改狀態。
        var interceptor = new RunAfterFirstOrderQueryInterceptor(async () =>
        {
            await using var other = OrderServiceFixture.CreateContext();
            var concurrent = await other.Orders.SingleAsync(candidate => candidate.Id == order.Id);
            concurrent.ChangeOrderStatus(OrderStatus.Confirmed, DateTime.UtcNow);
            await other.SaveChangesAsync();
        });

        await using var actContext = OrderServiceFixture.CreateContext(interceptor);
        var sweep = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, after: null, CancellationToken.None);

        Assert.True(interceptor.Fired, "the seam never fired; the test proves nothing");
        Assert.Equal(1, sweep.Examined);
        Assert.Equal(0, sweep.Cancelled);

        await using var verify = OrderServiceFixture.CreateContext();
        Assert.Equal(OrderStatus.Confirmed, await StatusOf(verify, order.PublicId));
        var reloaded = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == reservation.Id);
        Assert.Equal(InventoryReservationStatus.Active, reloaded.Status);
    }

    /// <summary>
    /// 組長 PR #85 round-3 review [P2]：最舊的一整批都是庫存不一致的資料時，排在它們後面的健康
    /// 逾時訂單必須在**同一個排程 cycle** 內被處理掉。先前的實作用「取消數 &lt; BatchSize」判斷
    /// backlog 已清空，於是一批全壞就整輪提早收工；而那些壞資料每分鐘都會再被撈到最前面，後面的
    /// 訂單永遠餓死。
    ///
    /// 這支測試直接模擬排程 cycle 的迴圈（每批 2 筆、帶游標往後推），並斷言失敗那幾筆有留下帶
    /// 訂單識別的 Warning——「留給人工處理」要成立，人工得先找得到是哪一筆。
    /// </summary>
    [Fact]
    public async Task AFullBatchOfInconsistentOrdersDoesNotStarveTheHealthyOnesBehindThem()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await NeutraliseExistingDeadlinesAsync(context);
        var memberUserId = await OrderServiceFixture.SeedMemberUserIdAsync(context);
        var provider = await OrderServiceFixture.SeedShippingProviderProfileAsync(context);

        // 先種的 Id 較小，在 (PaymentDueAtUtc, Id) 排序下一定排在前面。
        var broken = new List<Order>();
        for (var index = 0; index < 2; index++)
        {
            var order = await OrderServiceFixture.SeedOrderAsync(
                context, memberUserId, provider.Id, OrderStatus.PendingPayment);
            var (balance, _) = await OrderServiceFixture.SeedInventoryReservationAsync(context, order);
            await context.Database.ExecuteSqlAsync(
                $"UPDATE [InventoryBalances] SET [ReservedQuantity] = 0 WHERE [Id] = {balance.Id}");
            broken.Add(order);
        }

        var healthy = await OrderServiceFixture.SeedOrderAsync(
            context, memberUserId, provider.Id, OrderStatus.PendingPayment);
        var (_, healthyReservation) =
            await OrderServiceFixture.SeedInventoryReservationAsync(context, healthy);

        var logger = new CapturingLogger();
        var now = PastTheDeadline();

        // 排程 cycle：每批 2 筆，帶著游標往後推，取滿就繼續。
        OrderTimeoutCursor? cursor = null;
        var cancelledTotal = 0;
        var failedTotal = 0;
        for (var batch = 0; batch < 10; batch++)
        {
            await using var roundContext = OrderServiceFixture.CreateContext();
            var round = await CreateService(roundContext, logger).CancelOverduePendingPaymentOrdersAsync(
                now, batchSize: 2, cursor, CancellationToken.None);
            cancelledTotal += round.Cancelled;
            failedTotal += round.Failed;
            cursor = round.NextCursor;
            if (round.Examined < 2)
            {
                break;
            }
        }

        Assert.Equal(2, failedTotal);
        Assert.Equal(1, cancelledTotal);

        await using var verify = OrderServiceFixture.CreateContext();

        // 壞掉的兩筆完全沒動。
        foreach (var order in broken)
        {
            Assert.Equal(OrderStatus.PendingPayment, await StatusOf(verify, order.PublicId));
        }

        // 排在它們後面的健康訂單在同一個 cycle 內被完整處理。
        Assert.Equal(OrderStatus.Cancelled, await StatusOf(verify, healthy.PublicId));
        var reloaded = await verify.InventoryReservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == healthyReservation.Id);
        Assert.Equal(InventoryReservationStatus.Released, reloaded.Status);

        // 失敗的資料留下了找得到它的訊號。
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(2, warnings.Length);
        foreach (var order in broken)
        {
            Assert.Contains(warnings, entry => entry.Message.Contains(order.PublicId.ToString(), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RejectsANonPositiveBatchSize()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateService(context).CancelOverduePendingPaymentOrdersAsync(
                DateTime.UtcNow, batchSize: 0, after: null, CancellationToken.None));
    }

    /// <summary>
    /// SimulatedPaymentWriter 對訂單列做的事：轉狀態後一次 SaveChangesAsync。回傳是否提交成功——
    /// 輸掉 RowVersion 仲裁時回 0，與掃描的回傳值相加必須恰好是 1。
    /// </summary>
    private static async Task<int> ConfirmLikeThePaymentWriterAsync(DoSelectDbContext context, Order order)
    {
        try
        {
            order.ChangeOrderStatus(OrderStatus.Confirmed, DateTime.UtcNow);
            await context.SaveChangesAsync();
            return 1;
        }
        catch (DbUpdateConcurrencyException)
        {
            return 0;
        }
    }

    /// <summary>SeedOrderAsync 給的付款期限是「現在 + 15 分鐘」，所以往後推得夠遠就是逾時。</summary>
    private static DateTime PastTheDeadline() => DateTime.UtcNow.AddHours(1);

    /// <summary>
    /// 這個 fixture 的資料庫在整個 collection 內共用，而掃描是「找出所有逾時的 PendingPayment
    /// 訂單」——別的測試留下的訂單會一起被掃到。把既有訂單的付款期限推到一年後，每支測試就只會
    /// 碰到自己接著種的資料。用 SQL 是因為 PaymentDueAtUtc 是 private set（本來就不該讓程式隨意
    /// 改動付款期限）。
    /// </summary>
    private static async Task NeutraliseExistingDeadlinesAsync(DoSelectDbContext context) =>
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE [Orders] SET [PaymentDueAtUtc] = DATEADD(year, 1, GETUTCDATE())");

    private static EfOrderTimeoutCancellationService CreateService(
        DoSelectDbContext context,
        ILogger<EfOrderTimeoutCancellationService>? logger = null) =>
        new(context, new EfAuditWriter(context, TimeProvider.System),
            logger ?? NullLogger<EfOrderTimeoutCancellationService>.Instance);

    private static async Task<OrderStatus> StatusOf(DoSelectDbContext context, Guid publicId) =>
        await context.Orders.AsNoTracking()
            .Where(order => order.PublicId == publicId)
            .Select(order => order.OrderStatus)
            .SingleAsync();

    /// <summary>
    /// 在 Orders 的第一道 SELECT 讀取完成之後執行一次外部動作——服務的 ID 查詢與逐筆重新載入之間
    /// 的接縫。<see cref="Fired"/> 讓測試能確認接縫真的觸發過，而不是無聲地沒發生。
    /// </summary>
    private sealed class RunAfterFirstOrderQueryInterceptor(Func<Task> action) : DbCommandInterceptor
    {
        private int _fired;

        public bool Fired => _fired > 0;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[Orders]", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await action();
            }

            return result;
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<EfOrderTimeoutCancellationService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
