using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests;

public sealed class StartPaymentAttemptServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderPublicId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly byte[] CurrentRowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] StaleRowVersion = [1, 2, 3, 4, 5, 6, 7, 9];

    [Fact]
    public void PublicCreateRequestContainsOnlyMethodAndOrderRowVersion()
    {
        var properties = typeof(CreatePaymentAttemptRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Method", "OrderRowVersion"], properties);
    }

    [Fact]
    public async Task StartAsync_ApprovesAFirstRealtimeAttempt()
    {
        var service = CreateService(new FakePaymentAttemptReader(Snapshot()));

        var result = await service.StartAsync(Request());

        Assert.True(result.IsSuccess);
        var plan = Assert.IsType<PaymentAttemptPlan>(result.Plan);
        Assert.Equal(7L, plan.OrderId);
        Assert.Equal(PaymentSettlementKind.Realtime, plan.SettlementKind);
        Assert.Equal(NowUtc.AddMinutes(15), plan.InstructionExpiresAtUtc);
    }

    [Fact]
    public async Task StartAsync_PlansAThreeDayWindowForConvenienceCode()
    {
        var service = CreateService(
            new FakePaymentAttemptReader(Snapshot(paymentDueAtUtc: NowUtc.AddDays(3))));

        var result = await service.StartAsync(Request(PaymentMethod.ConvenienceCode));

        Assert.Equal(NowUtc.AddDays(3), result.Plan!.InstructionExpiresAtUtc);
    }

    [Fact]
    public async Task StartAsync_PlansNoWindowForCashOnDelivery()
    {
        // COD 只在 Confirmed 的訂單上建立 —— Checkout 在同一交易內建立
        // Order(Confirmed) 與庫存保留後才呼叫本服務（與 haru 確認為方案 1）。
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(orderStatus: OrderStatus.Confirmed)));

        var result = await service.StartAsync(Request(PaymentMethod.CashOnDelivery));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentSettlementKind.CashOnDelivery, result.Plan!.SettlementKind);
        Assert.Null(result.Plan.InstructionExpiresAtUtc);
    }

    [Fact]
    public async Task StartAsync_ReturnsNotFoundForAnUnknownOrder()
    {
        var service = CreateService(new FakePaymentAttemptReader(snapshot: null));

        var result = await service.StartAsync(Request());

        Assert.Equal(PaymentErrorCodes.ResourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task StartAsync_SurfacesThePolicyRejection()
    {
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(latestAttemptStatus: PaymentAttemptStatus.AwaitingPayment)));

        var result = await service.StartAsync(Request());

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task StartAsync_TrimsTheIdempotencyKeyIntoThePlan()
    {
        // 金鑰本身仍由 Writer 存進 PaymentAttempt，唯一索引是資料庫最後防線。
        var service = CreateService(new FakePaymentAttemptReader(Snapshot()));

        var result = await service.StartAsync(Request(idempotencyKey: "  pay-1  "));

        Assert.Equal("pay-1", result.Plan!.IdempotencyKey);
    }

    [Fact]
    public async Task StartAsync_RejectsABlankIdempotencyKey()
    {
        var service = CreateService(new FakePaymentAttemptReader(Snapshot()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.StartAsync(Request(idempotencyKey: "   ")));
    }

    [Fact]
    public void TheRequestHasNoAmountFieldToTamperWith()
    {
        // 契約層級的保證：呼叫端連指定金額的欄位都沒有，
        // 因此不可能要求建立與訂單總額不同的付款嘗試。
        var amountProperties = typeof(StartPaymentAttemptRequest)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(decimal) ||
                               property.PropertyType == typeof(decimal?))
            .ToArray();

        Assert.Empty(amountProperties);
    }

    [Fact]
    public async Task ThePlanAmountAlwaysComesFromTheOrderNotTheCaller()
    {
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(payableAmount: 4321m)));

        var result = await service.StartAsync(Request());

        Assert.Equal(4321m, result.Plan!.Amount);
    }

    [Fact]
    public async Task AStaleOrderRowVersionIsRejected()
    {
        // 呼叫端看到的訂單金額或狀態已經過期，不能據此建立付款嘗試。
        var service = CreateService(new FakePaymentAttemptReader(Snapshot()));

        var result = await service.StartAsync(Request(orderRowVersion: StaleRowVersion));

        Assert.Equal(PaymentErrorCodes.ConcurrencyConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task ACancelledOrderIsRejectedEvenThoughItIsNotPaid()
    {
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(orderStatus: OrderStatus.Cancelled)));

        var result = await service.StartAsync(Request());

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task ThePlanCarriesTheVersionTheDecisionWasMadeOn()
    {
        // Writer 必須能在同一交易內再次比對這個版本；服務讀取當下的比對不足以
        // 封住「讀取完成到寫入之間」訂單被改變的競態。
        var service = CreateService(new FakePaymentAttemptReader(Snapshot()));

        var result = await service.StartAsync(Request());

        Assert.Equal(CurrentRowVersion, result.Plan!.ExpectedOrderRowVersion);
    }

    [Fact]
    public void TheServiceDoesNotOwnIdempotency()
    {
        // 重播與 Payload 衝突屬呼叫端外層的共用 IIdempotencyExecutor。
        // 本服務曾自行比對既有付款嘗試，但那需要保存「建立當下的原始 Request」，
        // 而 PaymentAttempts 沒有這個欄位 —— 用目前訂單快照代替會把
        // 「版本改變後真正的重播被誤判成衝突」與「同 Key 換新版本被誤判成重播」
        // 兩個錯誤放回來。這條測試守住那套比對不會被重新加回服務層。
        var readerMethods = typeof(IPaymentAttemptReader)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain("FindByIdempotencyKeyAsync", readerMethods);

        var resultMembers = typeof(StartPaymentAttemptResult)
            .GetMembers()
            .Select(member => member.Name)
            .ToArray();

        Assert.DoesNotContain("IsReplay", resultMembers);
        Assert.DoesNotContain("ExistingAttemptPublicId", resultMembers);
    }

    private static StartPaymentAttemptService CreateService(IPaymentAttemptReader reader) =>
        new(reader, new FixedTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private static StartPaymentAttemptRequest Request(
        PaymentMethod method = PaymentMethod.CreditCard,
        byte[]? orderRowVersion = null,
        string idempotencyKey = "pay-1") =>
        new(OrderPublicId, method, orderRowVersion ?? CurrentRowVersion, idempotencyKey);

    private static OrderPaymentSnapshot Snapshot(
        PaymentAttemptStatus? latestAttemptStatus = null,
        DateTime? paymentDueAtUtc = null,
        OrderStatus orderStatus = OrderStatus.PendingPayment,
        decimal payableAmount = 1000m,
        byte[]? rowVersion = null) =>
        new(
            OrderId: 7L,
            rowVersion ?? CurrentRowVersion,
            new OrderPaymentContext(
                orderStatus,
                payableAmount,
                IsPaid: false,
                latestAttemptStatus,
                paymentDueAtUtc ?? NowUtc.AddMinutes(30),
                new CashOnDeliveryEligibility(
                    ShippingMethodAllowsCashOnDelivery: true,
                    ContainsAssemblyBuild: false,
                    ContainsPrepaymentOnlySku: false)));

    private sealed class FakePaymentAttemptReader : IPaymentAttemptReader
    {
        private readonly OrderPaymentSnapshot? _snapshot;

        public FakePaymentAttemptReader(OrderPaymentSnapshot? snapshot) => _snapshot = snapshot;

        public Task<OrderPaymentSnapshot?> FindOrderPaymentSnapshotAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
