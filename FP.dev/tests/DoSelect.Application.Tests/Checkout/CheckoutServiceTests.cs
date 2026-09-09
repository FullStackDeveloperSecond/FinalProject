using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Checkout;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests.Checkout;

public sealed class CheckoutServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_UsesCentralIdempotencyAndAtomicGateway()
    {
        var created = new OrderDto(
            Guid.NewGuid(),
            "DS202608270001",
            OrderStatus.PendingPayment,
            PaymentStatus.AwaitingPayment,
            FulfillmentStatus.Pending,
            AssemblyStatus.NotRequired,
            OrderRefundStatus.None,
            [],
            new OrderRecipientSummaryDto("Buyer", "CVS_PICKUP", "Store"),
            new OrderAmountsDto(1_000m, 0m, 0m, 0m, 1_000m, 0m, 0m, "TWD"),
            DateTime.UtcNow.AddDays(3),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ["cancel"],
            [1]);
        var gateway = new FakeGateway(created);
        var executor = new CapturingIdempotencyExecutor();
        var service = new CheckoutService(
            executor,
            gateway,
            new StaticPolicyProvider(new CheckoutPolicySnapshot(1, 1, 1, 1)),
            new StubGuestOrderAccessHasher());
        var memberPublicId = Guid.NewGuid();

        var result = await service.CreateOrderAsync(
            CheckoutActor.ForMember("identity-id", memberPublicId),
            Request(Guid.NewGuid()),
            "checkout-key",
            CancellationToken.None);

        Assert.Equal(201, result.StatusCode);
        Assert.False(result.IsReplay);
        Assert.Equal(created, result.Body);
        Assert.Equal("order.create", executor.Command?.Operation);
        Assert.Equal("checkout-key", executor.Command?.Key);
        Assert.Equal(IsolationLevel.ReadCommitted, executor.CapturedIsolationLevel);
        Assert.Equal(1, gateway.ExecuteCount);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenReplayed_ReturnsTheSameFullOrderProjectionWithoutExecutingAgain()
    {
        var created = CreateOrderDto();
        var gateway = new FakeGateway(created);
        var service = new CheckoutService(
            new ReplayOnlyIdempotencyExecutor(created.PublicId),
            gateway,
            new StaticPolicyProvider(new CheckoutPolicySnapshot(1, 1, 1, 1)),
            new StubGuestOrderAccessHasher());

        var result = await service.CreateOrderAsync(
            CheckoutActor.ForGuest("guest-cart-secret"),
            Request(Guid.NewGuid()),
            "checkout-key",
            CancellationToken.None);

        Assert.True(result.IsReplay);
        Assert.Equal(created, result.Body);
        Assert.Equal(0, gateway.ExecuteCount);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenGuestReplayReceiptHasVerification_ReissuesDeterministicAccessToken()
    {
        var created = CreateOrderDto();
        var verificationPublicId = Guid.NewGuid();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(15);
        var gateway = new FakeGateway(created);
        var hasher = new StubGuestOrderAccessHasher();
        var service = new CheckoutService(
            new ReplayOnlyIdempotencyExecutor(
                created.PublicId,
                verificationPublicId,
                expiresAtUtc),
            gateway,
            new StaticPolicyProvider(new CheckoutPolicySnapshot(1, 1, 1, 1)),
            hasher);

        var result = await service.CreateOrderAsync(
            CheckoutActor.ForGuest("guest-cart-secret", "proof"),
            Request(Guid.NewGuid()),
            "checkout-key",
            CancellationToken.None);

        Assert.True(result.IsReplay);
        Assert.Equal(
            hasher.DeriveOrderAccessToken(created.PublicId, verificationPublicId),
            result.Body.GuestOrderAccessToken);
        Assert.Equal(verificationPublicId, result.Body.GuestEmailVerificationPublicId);
        Assert.Equal(expiresAtUtc, result.Body.GuestOrderAccessExpiresAtUtc);
    }

    private static OrderDto CreateOrderDto() => new(
        Guid.NewGuid(),
        "DS202608270001",
        OrderStatus.PendingPayment,
        PaymentStatus.AwaitingPayment,
        FulfillmentStatus.Pending,
        AssemblyStatus.NotRequired,
        OrderRefundStatus.None,
        [],
        new OrderRecipientSummaryDto("Buyer", "CVS_PICKUP", "Store"),
        new OrderAmountsDto(1_000m, 0m, 0m, 0m, 1_000m, 0m, 0m, "TWD"),
        DateTime.UtcNow.AddDays(3),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        ["cancel"],
        [1]);

    private static CreateOrderRequest Request(Guid cartPublicId) =>
        new(
            cartPublicId,
            [1],
            new CheckoutBuyerInput("buyer@example.com", "Buyer", "0912345678"),
            new CheckoutShippingInput("CVS_PICKUP", null, Guid.NewGuid()),
            PaymentMethod.CreditCard,
            null,
            new CheckoutInvoiceInput(
                CheckoutInvoiceType.Simulated,
                CheckoutInvoiceBuyerType.Personal,
                null,
                null,
                null,
                null),
            new AcceptedPolicyVersions(1, 1, 1));

    private sealed class CapturingIdempotencyExecutor : IIdempotencyExecutor
    {
        public IdempotencyCommand? Command { get; private set; }
        public IsolationLevel? CapturedIsolationLevel { get; private set; }

        public async Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
            IdempotencyCommand command,
            Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
            Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
            CancellationToken cancellationToken,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            Command = command;
            CapturedIsolationLevel = isolationLevel;
            var response = await handler(cancellationToken);
            return new IdempotencyExecutionResult<T>(
                response.StatusCode,
                response.Body,
                response.ResponseHeadersJson,
                IsReplay: false);
        }
    }

    private sealed class ReplayOnlyIdempotencyExecutor(
        Guid orderPublicId,
        Guid? verificationPublicId = null,
        DateTime? accessExpiresAtUtc = null) : IIdempotencyExecutor
    {
        public async Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
            IdempotencyCommand command,
            Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
            Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
            CancellationToken cancellationToken,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            var body = await replayFactory(
                new StoredIdempotencyResponse(
                    201,
                    "{}",
                    JsonSerializer.Serialize(new
                    {
                        OrderPublicId = orderPublicId,
                        GuestEmailVerificationPublicId = verificationPublicId,
                        GuestOrderAccessExpiresAtUtc = accessExpiresAtUtc,
                    })),
                cancellationToken);
            return new IdempotencyExecutionResult<T>(201, body, "{}", IsReplay: true);
        }
    }

    private sealed class FakeGateway(OrderDto created) : ICheckoutTransactionGateway
    {
        public Task ValidateCartOwnershipAsync(CheckoutActor actor, Guid cartPublicId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public int ExecuteCount { get; private set; }

        public Task<OrderDto> ExecuteAsync(
            CheckoutCommand command,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult(created);
        }

        public Task<OrderDto?> FindCreatedOrderAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderDto?>(
                orderPublicId == created.PublicId ? created : null);
    }

    private sealed class StaticPolicyProvider(CheckoutPolicySnapshot current)
        : ICheckoutPolicyProvider
    {
        public CheckoutPolicySnapshot Current => current;
    }

    private sealed class StubGuestOrderAccessHasher : IGuestOrderAccessHasher
    {
        public byte[] HashIp(string ipAddress) => Hash(ipAddress);
        public byte[] HashEmail(string emailNormalized) => Hash(emailNormalized);
        public byte[] HashOrderLookup(string orderNumber, string emailNormalized) =>
            Hash($"{orderNumber}:{emailNormalized}");
        public byte[] HashCode(string sixDigitCode) => Hash(sixDigitCode);
        public string DeriveVerificationCode(Guid requestPublicId, int sendNumber) => "123456";
        public byte[] HashToken(string rawToken) => Hash(rawToken);
        public string DeriveOrderAccessToken(Guid orderPublicId, Guid verificationPublicId) =>
            Convert.ToHexStringLower(Hash($"{orderPublicId:N}:{verificationPublicId:N}"));

        private static byte[] Hash(string value) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
