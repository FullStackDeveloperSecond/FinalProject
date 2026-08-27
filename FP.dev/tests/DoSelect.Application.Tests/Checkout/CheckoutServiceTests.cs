using System.Data;
using System.Text.Json;
using DoSelect.Application.Checkout;
using DoSelect.Application.Idempotency;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests.Checkout;

public sealed class CheckoutServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_UsesCentralIdempotencyAndAtomicGateway()
    {
        var created = new CheckoutCreatedOrder(
            Guid.NewGuid(),
            "DS202608270001",
            1_000m,
            "TWD",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(3));
        var gateway = new FakeGateway(created);
        var executor = new CapturingIdempotencyExecutor();
        var service = new CheckoutService(
            executor,
            gateway,
            new StaticPolicyProvider(new CheckoutPolicySnapshot(1, 1, 1, 1)));
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

    private sealed class FakeGateway(CheckoutCreatedOrder created) : ICheckoutTransactionGateway
    {
        public int ExecuteCount { get; private set; }

        public Task<CheckoutCreatedOrder> ExecuteAsync(
            CheckoutCommand command,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult(created);
        }

        public Task<CheckoutCreatedOrder?> FindCreatedOrderAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CheckoutCreatedOrder?>(
                orderPublicId == created.PublicId ? created : null);
    }

    private sealed class StaticPolicyProvider(CheckoutPolicySnapshot current)
        : ICheckoutPolicyProvider
    {
        public CheckoutPolicySnapshot Current => current;
    }
}
