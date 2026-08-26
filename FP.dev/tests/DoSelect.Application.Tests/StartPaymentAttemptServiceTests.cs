using DoSelect.Domain.Orders;
using DoSelect.Application.Payments;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests;

public sealed class StartPaymentAttemptServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderPublicId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ExistingAttemptPublicId =
        new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task StartAsync_ApprovesAFirstRealtimeAttempt()
    {
        var service = CreateService(new FakePaymentAttemptReader(Snapshot()));

        var result = await service.StartAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.False(result.IsReplay);
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
        // COD 只在 Confirmed 的訂單上建立（訂單建立時同時產生付款紀錄）。
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(orderStatus: OrderStatus.Confirmed)));

        var result = await service.StartAsync(Request(PaymentMethod.CashOnDelivery));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentSettlementKind.CashOnDelivery, result.Plan!.SettlementKind);
        Assert.Null(result.Plan.InstructionExpiresAtUtc);
    }

    [Fact]
    public async Task StartAsync_ReplaysTheSameKeyWithTheSamePayload()
    {
        var reader = new FakePaymentAttemptReader(Snapshot(), Existing());
        var service = CreateService(reader);

        var result = await service.StartAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.True(result.IsReplay);
        Assert.Equal(ExistingAttemptPublicId, result.ExistingAttemptPublicId);
        Assert.Null(result.Plan);
    }

    [Theory]
    [InlineData(PaymentMethod.ATM)]
    [InlineData(PaymentMethod.ConvenienceCode)]
    [InlineData(PaymentMethod.CashOnDelivery)]
    public async Task StartAsync_ConflictsOnTheSameKeyWithADifferentPayload(PaymentMethod method)
    {
        // 既有嘗試是 CreditCard。付款方式不同就不是同一個命令。
        // 金額不再是 Payload 的一部分，改由 TheIdempotencyPayloadIsComparedAgainstTheOrderAmount
        // 驗證「訂單金額變動時也視為不同命令」。
        var reader = new FakePaymentAttemptReader(Snapshot(), Existing());
        var service = CreateService(reader);

        var result = await service.StartAsync(Request(method));

        Assert.Equal(PaymentErrorCodes.IdempotencyPayloadConflict, result.ErrorCode);
    }

    [Fact]
    public async Task StartAsync_ConflictsWhenTheKeyBelongsToAnotherOrder()
    {
        var reader = new FakePaymentAttemptReader(Snapshot(), Existing(orderId: 99L));
        var service = CreateService(reader);

        var result = await service.StartAsync(Request());

        Assert.Equal(PaymentErrorCodes.IdempotencyPayloadConflict, result.ErrorCode);
    }

    [Fact]
    public async Task StartAsync_ReplaysBeforeEvaluatingTheOrderState()
    {
        // 既有嘗試已是終態，但相同 Key 相同 Payload 仍必須回同一筆，不得建立第二筆。
        var reader = new FakePaymentAttemptReader(
            Snapshot(latestAttemptStatus: PaymentAttemptStatus.Failed),
            Existing(status: PaymentAttemptStatus.Failed));
        var service = CreateService(reader);

        var result = await service.StartAsync(Request());

        Assert.True(result.IsReplay);
        Assert.Equal(ExistingAttemptPublicId, result.ExistingAttemptPublicId);
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
    public async Task StartAsync_TrimsTheIdempotencyKeyBeforeLookup()
    {
        var reader = new FakePaymentAttemptReader(Snapshot());
        var service = CreateService(reader);

        var result = await service.StartAsync(Request(idempotencyKey: "  pay-1  "));

        Assert.Equal("pay-1", reader.RequestedIdempotencyKey);
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
    public async Task AReplayStillWorksAfterTheOrderVersionChanged()
    {
        // 第一次建立成功但回應遺失，之後訂單版本又改變。呼叫端以原 Key 與原 Request
        // 重送時必須拿回原本那筆 —— 那次建立已經發生了，不能回 concurrency_conflict。
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(rowVersion: NewerRowVersion),
            Existing(orderRowVersion: CurrentRowVersion)));

        var result = await service.StartAsync(Request(orderRowVersion: CurrentRowVersion));

        Assert.True(result.IsReplay);
        Assert.Equal(ExistingAttemptPublicId, result.ExistingAttemptPublicId);
    }

    [Fact]
    public async Task TheSameKeyWithADifferentRowVersionIsAPayloadConflict()
    {
        // 換上新的 orderRowVersion 就是不同的 Request，即使目前訂單金額與付款方式相同。
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(rowVersion: NewerRowVersion),
            Existing(orderRowVersion: CurrentRowVersion)));

        var result = await service.StartAsync(Request(orderRowVersion: NewerRowVersion));

        Assert.Equal(PaymentErrorCodes.IdempotencyPayloadConflict, result.ErrorCode);
    }

    [Fact]
    public async Task TheSameKeyWithADifferentMethodIsAPayloadConflict()
    {
        var service = CreateService(new FakePaymentAttemptReader(
            Snapshot(),
            Existing(method: PaymentMethod.CreditCard)));

        var result = await service.StartAsync(Request(PaymentMethod.ATM));

        Assert.Equal(PaymentErrorCodes.IdempotencyPayloadConflict, result.ErrorCode);
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

    private static StartPaymentAttemptService CreateService(IPaymentAttemptReader reader) =>
        new(reader, new FixedTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private static readonly byte[] CurrentRowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] StaleRowVersion = [1, 2, 3, 4, 5, 6, 7, 9];
    private static readonly byte[] NewerRowVersion = [9, 9, 9, 9, 9, 9, 9, 9];

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

    private static ExistingPaymentAttempt Existing(
        long orderId = 7L,
        PaymentAttemptStatus status = PaymentAttemptStatus.AwaitingPayment,
        byte[]? orderRowVersion = null,
        PaymentMethod method = PaymentMethod.CreditCard) =>
        new(
            ExistingAttemptPublicId,
            orderId,
            method,
            1000m,
            orderRowVersion ?? CurrentRowVersion,
            status);

    private sealed class FakePaymentAttemptReader : IPaymentAttemptReader
    {
        private readonly OrderPaymentSnapshot? _snapshot;
        private readonly ExistingPaymentAttempt? _existing;

        public FakePaymentAttemptReader(
            OrderPaymentSnapshot? snapshot,
            ExistingPaymentAttempt? existing = null)
        {
            _snapshot = snapshot;
            _existing = existing;
        }

        public string? RequestedIdempotencyKey { get; private set; }

        public Task<ExistingPaymentAttempt?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            RequestedIdempotencyKey = idempotencyKey;
            return Task.FromResult(_existing);
        }

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
