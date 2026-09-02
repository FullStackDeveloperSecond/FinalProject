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
        var cancelled = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, CancellationToken.None);

        Assert.Equal(1, cancelled);

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
        var cancelled = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            DateTime.UtcNow, batchSize: 100, CancellationToken.None);

        Assert.Equal(0, cancelled);
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
        var cancelled = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, CancellationToken.None);

        Assert.Equal(0, cancelled);

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
        var cancelled = await CreateService(actContext).CancelOverduePendingPaymentOrdersAsync(
            PastTheDeadline(), batchSize: 100, CancellationToken.None);

        Assert.Equal(0, cancelled);
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
            PastTheDeadline(), batchSize: 100, CancellationToken.None);
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
        int cancelledThisRound;
        do
        {
            await using var roundContext = OrderServiceFixture.CreateContext();
            cancelledThisRound = await CreateService(roundContext).CancelOverduePendingPaymentOrdersAsync(
                now, batchSize: 2, CancellationToken.None);
            rounds.Add(cancelledThisRound);
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

    [Fact]
    public async Task RejectsANonPositiveBatchSize()
    {
        await using var context = OrderServiceFixture.CreateContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateService(context).CancelOverduePendingPaymentOrdersAsync(
                DateTime.UtcNow, batchSize: 0, CancellationToken.None));
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

    private static EfOrderTimeoutCancellationService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));

    private static async Task<OrderStatus> StatusOf(DoSelectDbContext context, Guid publicId) =>
        await context.Orders.AsNoTracking()
            .Where(order => order.PublicId == publicId)
            .Select(order => order.OrderStatus)
            .SingleAsync();
}
