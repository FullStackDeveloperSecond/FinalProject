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
        var service = CreateService(new FakePaymentAttemptReader(Snapshot()));

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
    [InlineData(PaymentMethod.ATM, 1000)]
    [InlineData(PaymentMethod.CreditCard, 2000)]
    public async Task StartAsync_ConflictsOnTheSameKeyWithADifferentPayload(
        PaymentMethod method,
        int amount)
    {
        var reader = new FakePaymentAttemptReader(Snapshot(), Existing());
        var service = CreateService(reader);

        var result = await service.StartAsync(Request(method, amount));

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

    private static StartPaymentAttemptService CreateService(IPaymentAttemptReader reader) =>
        new(reader, new FixedTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private static StartPaymentAttemptRequest Request(
        PaymentMethod method = PaymentMethod.CreditCard,
        decimal amount = 1000m,
        string idempotencyKey = "pay-1") =>
        new(OrderPublicId, method, amount, idempotencyKey);

    private static OrderPaymentSnapshot Snapshot(
        PaymentAttemptStatus? latestAttemptStatus = null,
        DateTime? paymentDueAtUtc = null) =>
        new(
            OrderId: 7L,
            new OrderPaymentContext(
                IsPaid: false,
                latestAttemptStatus,
                paymentDueAtUtc ?? NowUtc.AddMinutes(30),
                new CashOnDeliveryEligibility(
                    ShippingMethodAllowsCashOnDelivery: true,
                    ContainsAssemblyBuild: false,
                    ContainsPrepaymentOnlySku: false,
                    FinalPayableAmount: 1000m)));

    private static ExistingPaymentAttempt Existing(
        long orderId = 7L,
        PaymentAttemptStatus status = PaymentAttemptStatus.AwaitingPayment) =>
        new(ExistingAttemptPublicId, orderId, PaymentMethod.CreditCard, 1000m, status);

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
