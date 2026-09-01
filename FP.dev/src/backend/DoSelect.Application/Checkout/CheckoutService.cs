using System.Text.Json;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;

namespace DoSelect.Application.Checkout;

/// <summary>
/// Owns Checkout command normalization and the central idempotency boundary. The injected gateway
/// performs all trusted revalidation and writes while participating in the executor-owned SQL
/// transaction; this service never reaches into another module's Repository or DbContext.
/// </summary>
public sealed class CheckoutService
{
    public const string Operation = "order.create";

    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly ICheckoutTransactionGateway _transactionGateway;
    private readonly ICheckoutPolicyProvider _policyProvider;

    public CheckoutService(
        IIdempotencyExecutor idempotencyExecutor,
        ICheckoutTransactionGateway transactionGateway,
        ICheckoutPolicyProvider policyProvider)
    {
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(transactionGateway);
        ArgumentNullException.ThrowIfNull(policyProvider);

        _idempotencyExecutor = idempotencyExecutor;
        _transactionGateway = transactionGateway;
        _policyProvider = policyProvider;
    }

    public Task<IdempotencyExecutionResult<OrderDto>> CreateOrderAsync(
        CheckoutActor actor,
        CreateOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var command = CheckoutCommandFactory.Create(
            actor,
            request,
            idempotencyKey,
            _policyProvider.Current);
        var actorScope = actor.IsMember
            ? IdempotencyActorScope.ForUser(actor.MemberPublicId!.Value)
            : IdempotencyActorScope.ForGuestCart(request.CartPublicId);
        var idempotencyCommand = IdempotencyCommand.Create(
            actorScope,
            Operation,
            idempotencyKey,
            request);

        return _idempotencyExecutor.ExecuteAsync(
            idempotencyCommand,
            handler: ct => ExecuteAsync(command, ct),
            replayFactory: ReplayAsync,
            cancellationToken);
    }

    private async Task<IdempotencyResponse<OrderDto>> ExecuteAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _transactionGateway.ExecuteAsync(command, cancellationToken);
        var receipt = JsonSerializer.Serialize(new CheckoutReplayReceipt(created.PublicId));
        return new IdempotencyResponse<OrderDto>(201, created, receipt);
    }

    private async Task<OrderDto> ReplayAsync(
        StoredIdempotencyResponse stored,
        CancellationToken cancellationToken)
    {
        var receipt = JsonSerializer.Deserialize<CheckoutReplayReceipt>(stored.ResponseSummary)
            ?? throw new InvalidOperationException("The Checkout replay receipt is invalid.");

        return await _transactionGateway.FindCreatedOrderAsync(receipt.OrderPublicId, cancellationToken)
            ?? throw new InvalidOperationException("The idempotent Checkout order no longer exists.");
    }

    private sealed record CheckoutReplayReceipt(Guid OrderPublicId);
}
