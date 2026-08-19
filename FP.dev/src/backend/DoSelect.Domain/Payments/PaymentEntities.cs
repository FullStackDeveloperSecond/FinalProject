using DoSelect.Domain.Common;

namespace DoSelect.Domain.Payments;

public enum PaymentMethod
{
    CreditCard,
    ATM,
    ConvenienceCode,
    CashOnDelivery,
    LinePay,
    ApplePay,
    GooglePay,
}

public enum PaymentAttemptStatus
{
    Pending,
    Processing,
    AwaitingPayment,
    Paid,
    Failed,
    Expired,
    Cancelled,
}

public enum PaymentEventProcessingStatus
{
    Received,
    Processed,
    IgnoredDuplicate,
    Failed,
}

public sealed class PaymentAttempt : MutablePublicEntity
{
    private static readonly IReadOnlyDictionary<PaymentAttemptStatus, PaymentAttemptStatus[]> AllowedTransitions =
        new Dictionary<PaymentAttemptStatus, PaymentAttemptStatus[]>
        {
            [PaymentAttemptStatus.Pending] = [PaymentAttemptStatus.AwaitingPayment],
            [PaymentAttemptStatus.AwaitingPayment] = [PaymentAttemptStatus.Processing, PaymentAttemptStatus.Cancelled, PaymentAttemptStatus.Expired],
            [PaymentAttemptStatus.Processing] = [PaymentAttemptStatus.Paid, PaymentAttemptStatus.Failed],
            [PaymentAttemptStatus.Paid] = [],
            [PaymentAttemptStatus.Failed] = [],
            [PaymentAttemptStatus.Expired] = [],
            [PaymentAttemptStatus.Cancelled] = [],
        };

    private PaymentAttempt() { }

    public PaymentAttempt(
        Guid publicId,
        long orderId,
        PaymentMethod method,
        decimal amount,
        string? providerCode,
        string idempotencyKey,
        DateTime? instructionExpiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId <= 0 || amount <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));
        OrderId = orderId;
        Method = method;
        Status = PaymentAttemptStatus.Pending;
        Amount = amount;
        ProviderCode = NormalizeOptional(providerCode);
        IdempotencyKey = RequireText(idempotencyKey, nameof(idempotencyKey));
        InstructionExpiresAtUtc = instructionExpiresAtUtc.HasValue
            ? RequireUtc(instructionExpiresAtUtc.Value, nameof(instructionExpiresAtUtc))
            : null;
    }

    public long OrderId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string? ProviderCode { get; private set; }
    public string? ExternalReference { get; private set; }
    public DateTime? InstructionExpiresAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;

    public void SetPaymentInstruction(string externalReference, DateTime occurredAtUtc)
    {
        ExternalReference = RequireText(externalReference, nameof(externalReference));
        Transition(PaymentAttemptStatus.AwaitingPayment, occurredAtUtc);
    }

    public void Transition(PaymentAttemptStatus next, DateTime occurredAtUtc, string? failureCode = null)
    {
        if (!AllowedTransitions[Status].Contains(next) ||
            next == PaymentAttemptStatus.Failed && string.IsNullOrWhiteSpace(failureCode))
        {
            throw new InvalidOperationException($"Payment cannot move from {Status} to {next}.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Status = next;
        PaidAtUtc = next == PaymentAttemptStatus.Paid ? occurredAtUtc : PaidAtUtc;
        FailedAtUtc = next == PaymentAttemptStatus.Failed ? occurredAtUtc : FailedAtUtc;
        FailureCode = next == PaymentAttemptStatus.Failed ? failureCode!.Trim() : FailureCode;
        MarkUpdated(occurredAtUtc);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class PaymentEvent : PublicEntity
{
    private PaymentEvent() { }

    public PaymentEvent(
        Guid publicId,
        long paymentAttemptId,
        string externalEventId,
        string eventType,
        DateTimeOffset occurredAt,
        DateTime receivedAtUtc,
        byte[] payloadHash,
        string? payloadSummaryJson,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (paymentAttemptId <= 0 || payloadHash.Length != 32)
        {
            throw new ArgumentException("The payment event relation or payload hash is invalid.");
        }

        PaymentAttemptId = paymentAttemptId;
        ExternalEventId = RequireText(externalEventId, nameof(externalEventId));
        EventType = RequireText(eventType, nameof(eventType));
        OccurredAt = occurredAt;
        ReceivedAtUtc = RequireUtc(receivedAtUtc, nameof(receivedAtUtc));
        PayloadHash = payloadHash.ToArray();
        PayloadSummaryJson = string.IsNullOrWhiteSpace(payloadSummaryJson) ? null : payloadSummaryJson.Trim();
        ProcessingStatus = PaymentEventProcessingStatus.Received;
    }

    public long PaymentAttemptId { get; private set; }
    public string ExternalEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public byte[] PayloadHash { get; private set; } = [];
    public string? PayloadSummaryJson { get; private set; }
    public PaymentEventProcessingStatus ProcessingStatus { get; private set; }
}
