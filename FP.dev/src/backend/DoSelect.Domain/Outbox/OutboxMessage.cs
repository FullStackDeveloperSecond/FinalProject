using DoSelect.Domain.Common;

namespace DoSelect.Domain.Outbox;

public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
}

/// <summary>
/// Durable integration event stored in the same SQL Server transaction as the business change.
/// Payloads are serialized by the infrastructure writer from the application's closed v1 payload
/// set; arbitrary entity graphs and secrets are not accepted here.
/// </summary>
public sealed class OutboxMessage : PublicEntity
{
    private OutboxMessage()
    {
    }

    internal OutboxMessage(
        Guid publicId,
        string type,
        int payloadVersion,
        string aggregateType,
        Guid aggregatePublicId,
        string payloadJson,
        DateTime occurredAtUtc,
        DateTime availableAtUtc,
        string correlationId,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (payloadVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadVersion));
        }

        if (aggregatePublicId == Guid.Empty)
        {
            throw new ArgumentException("Aggregate PublicId is required.", nameof(aggregatePublicId));
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        availableAtUtc = RequireUtc(availableAtUtc, nameof(availableAtUtc));
        if (availableAtUtc < occurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableAtUtc),
                "AvailableAtUtc cannot precede OccurredAtUtc.");
        }

        Type = RequireBoundedText(type, nameof(type), 128);
        PayloadVersion = payloadVersion;
        AggregateType = RequireBoundedText(aggregateType, nameof(aggregateType), 64);
        AggregatePublicId = aggregatePublicId;
        PayloadJson = RequireBoundedText(payloadJson, nameof(payloadJson), 8_000);
        OccurredAtUtc = occurredAtUtc;
        AvailableAtUtc = availableAtUtc;
        CorrelationId = RequireBoundedText(correlationId, nameof(correlationId), 64);
        Status = OutboxMessageStatus.Pending;
    }

    public string Type { get; private set; } = string.Empty;
    public int PayloadVersion { get; private set; }
    public string AggregateType { get; private set; } = string.Empty;
    public Guid AggregatePublicId { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime AvailableAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];

    public void Claim(DateTime claimedAtUtc, DateTime leaseUntilUtc)
    {
        claimedAtUtc = RequireUtc(claimedAtUtc, nameof(claimedAtUtc));
        leaseUntilUtc = RequireUtc(leaseUntilUtc, nameof(leaseUntilUtc));
        if (leaseUntilUtc <= claimedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseUntilUtc));
        }

        if (Status is OutboxMessageStatus.Processed or OutboxMessageStatus.Failed ||
            AvailableAtUtc > claimedAtUtc)
        {
            throw new InvalidOperationException("The outbox message is not available for claiming.");
        }

        Status = OutboxMessageStatus.Processing;
        AvailableAtUtc = leaseUntilUtc;
        AttemptCount++;
        LastErrorCode = null;
    }

    public void Complete(DateTime processedAtUtc)
    {
        processedAtUtc = RequireUtc(processedAtUtc, nameof(processedAtUtc));
        if (Status != OutboxMessageStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing outbox message can complete.");
        }

        Status = OutboxMessageStatus.Processed;
        ProcessedAtUtc = processedAtUtc;
        AvailableAtUtc = processedAtUtc;
        LastErrorCode = null;
    }

    public void ScheduleRetry(string errorCode, DateTime availableAtUtc)
    {
        if (Status != OutboxMessageStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing outbox message can be retried.");
        }

        availableAtUtc = RequireUtc(availableAtUtc, nameof(availableAtUtc));
        Status = OutboxMessageStatus.Pending;
        AvailableAtUtc = availableAtUtc;
        LastErrorCode = RequireBoundedText(errorCode, nameof(errorCode), 64);
    }

    public void Fail(string errorCode)
    {
        if (Status != OutboxMessageStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing outbox message can fail.");
        }

        Status = OutboxMessageStatus.Failed;
        LastErrorCode = RequireBoundedText(errorCode, nameof(errorCode), 64);
    }

    private static string RequireBoundedText(string value, string parameterName, int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
