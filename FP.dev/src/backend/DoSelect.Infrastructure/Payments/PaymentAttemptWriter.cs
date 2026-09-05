using System.Data;
using System.Text.Json;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;
using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Payments;

/// <summary>
/// Creates one immutable payment attempt inside the central idempotency transaction. Every retry
/// is a new row; terminal attempts are never moved backwards.
/// </summary>
public sealed class PaymentAttemptWriter : IPaymentAttemptWriter
{
    private const string PaymentProviderCode = "SIMULATED";
    private readonly DoSelectDbContext _context;
    private readonly StartPaymentAttemptService _planner;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly TimeProvider _timeProvider;

    public PaymentAttemptWriter(
        DoSelectDbContext context,
        StartPaymentAttemptService planner,
        IIdempotencyExecutor idempotencyExecutor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _planner = planner;
        _idempotencyExecutor = idempotencyExecutor;
        _timeProvider = timeProvider;
    }

    public async Task<IdempotencyExecutionResult<PaymentAttemptDto>> CreateAsync(
        CreatePaymentAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = await ResolveIdempotencyScopeAsync(command.Actor, cancellationToken);
        var idempotencyCommand = IdempotencyCommand.Create(
            scope,
            PaymentAttemptWriteConstants.Operation,
            command.IdempotencyKey,
            new
            {
                command.OrderPublicId,
                Method = command.Method.ToString(),
                command.OrderRowVersion,
            });

        try
        {
            return await _idempotencyExecutor.ExecuteAsync(
                idempotencyCommand,
                handler: ct => CreateOnceAsync(command, ct),
                replayFactory: ReplayAsync,
                cancellationToken,
                IsolationLevel.Serializable);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainProblemException.Conflict(
                PaymentErrorCodes.ConcurrencyConflict,
                "The order changed while the payment attempt was being created. Reload and retry.");
        }
    }

    private async Task<IdempotencyResponse<PaymentAttemptDto>> CreateOnceAsync(
        CreatePaymentAttemptCommand command,
        CancellationToken cancellationToken)
    {
        var orderQuery = _context.Orders
            .Where(order => order.PublicId == command.OrderPublicId);
        orderQuery = command.Actor switch
        {
            OrderActor.Member member => orderQuery.Where(order => order.MemberUserId == member.UserId),
            OrderActor.Guest => orderQuery.Where(order => order.MemberUserId == null),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        var order = await orderQuery.SingleOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.NotFound("The order was not found.");

        var decision = await _planner.StartAsync(
            new StartPaymentAttemptRequest(
                command.OrderPublicId,
                command.Method,
                command.OrderRowVersion,
                command.IdempotencyKey),
            cancellationToken);
        if (decision.Plan is not { } plan)
        {
            throw decision.ErrorCode == PaymentErrorCodes.ResourceNotFound
                ? DomainProblemException.NotFound("The order was not found.")
                : DomainProblemException.Conflict(
                    decision.ErrorCode!,
                    DescribeRejection(decision.ErrorCode!));
        }

        if (order.Id != plan.OrderId ||
            !order.RowVersion.AsSpan().SequenceEqual(plan.ExpectedOrderRowVersion))
        {
            throw DomainProblemException.Conflict(
                PaymentErrorCodes.ConcurrencyConflict,
                "The order changed while the payment attempt was being created. Reload and retry.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var attempt = new PaymentAttempt(
            Guid.CreateVersion7(),
            plan.OrderId,
            plan.Method,
            plan.Amount,
            PaymentProviderCode,
            plan.IdempotencyKey,
            plan.InstructionExpiresAtUtc,
            nowUtc);
        attempt.SetPaymentInstruction("SIM-" + attempt.PublicId.ToString("N"), nowUtc);
        _context.PaymentAttempts.Add(attempt);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = PaymentAttemptDtoMapper.Map(attempt);
        return new IdempotencyResponse<PaymentAttemptDto>(
            201,
            dto,
            JsonSerializer.Serialize(new PaymentAttemptReceipt(attempt.PublicId)));
    }

    private async Task<PaymentAttemptDto> ReplayAsync(
        StoredIdempotencyResponse stored,
        CancellationToken cancellationToken)
    {
        var receipt = JsonSerializer.Deserialize<PaymentAttemptReceipt>(stored.ResponseSummary)
            ?? throw new InvalidOperationException("The stored payment-attempt receipt is invalid.");
        var attempt = await _context.PaymentAttempts.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.PublicId == receipt.PaymentAttemptPublicId,
                cancellationToken)
            ?? throw new InvalidOperationException("The stored payment attempt no longer exists.");
        return PaymentAttemptDtoMapper.Map(attempt);
    }

    private async Task<IdempotencyActorScope> ResolveIdempotencyScopeAsync(
        OrderActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor is OrderActor.Guest guest)
        {
            return IdempotencyActorScope.ForGuestOrderAccess(guest.TokenPublicId);
        }

        var member = (OrderActor.Member)actor;
        var publicId = await _context.Users.AsNoTracking()
            .Where(user => user.Id == member.UserId)
            .Select(user => user.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
        if (publicId == Guid.Empty)
        {
            throw DomainProblemException.NotFound("The order was not found.");
        }

        return IdempotencyActorScope.ForUser(publicId);
    }

    private static string DescribeRejection(string errorCode) => errorCode switch
    {
        PaymentErrorCodes.OrderPaymentDeadlineExpired =>
            "The order payment deadline passed.",
        PaymentErrorCodes.PaymentMethodNotAllowed =>
            "The payment method is not allowed for this order.",
        PaymentErrorCodes.PaymentCodAmountExceeded =>
            "The order exceeds the cash-on-delivery amount limit.",
        PaymentErrorCodes.PaymentCodRestrictedItem =>
            "The order contains items that require prepayment.",
        PaymentErrorCodes.ConcurrencyConflict =>
            "The order changed. Reload it and retry.",
        _ => "A new payment attempt cannot be created from the current order state.",
    };

    private sealed record PaymentAttemptReceipt(Guid PaymentAttemptPublicId);
}
