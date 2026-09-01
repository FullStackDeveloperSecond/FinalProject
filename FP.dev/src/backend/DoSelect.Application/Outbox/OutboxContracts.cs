using DoSelect.Domain.Outbox;

namespace DoSelect.Application.Outbox;

public static class OutboxEventTypes
{
    public const string EmailNotificationRequestedV1 = "notification.email.requested.v1";
    public const string InAppNotificationRequestedV1 = "notification.in_app.requested.v1";
    public const string InventoryReconciliationMismatchDetectedV1 =
        "inventory.reconciliation_mismatch.detected.v1";
    public const string SimulatedInvoiceRequestedV1 = "invoice.simulated.requested.v1";
}

public sealed record EmailNotificationRequestedV1(
    Guid NotificationPublicId,
    string TemplateKey,
    string RecipientPurpose,
    string ResourceType,
    Guid ResourcePublicId,
    string Locale,
    int ParameterSetVersion);

public sealed record InAppNotificationRequestedV1(
    Guid NotificationPublicId,
    Guid MemberPublicId,
    string MessageKey,
    string ResourceType,
    Guid ResourcePublicId,
    string Locale,
    int ParameterSetVersion);

public sealed record InventoryReconciliationMismatchDetectedV1(
    Guid CasePublicId,
    Guid SkuPublicId,
    int ExpectedOnHand,
    int ActualOnHand,
    DateTime DetectedAtUtc);

public sealed record SimulatedInvoiceRequestedV1(Guid OrderPublicId);

public sealed class OutboxWriteRequest
{
    private OutboxWriteRequest(
        Guid publicId,
        string type,
        int payloadVersion,
        string aggregateType,
        Guid aggregatePublicId,
        object payload,
        DateTime occurredAtUtc,
        DateTime availableAtUtc,
        string correlationId)
    {
        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("Outbox PublicId is required.", nameof(publicId));
        }

        if (aggregatePublicId == Guid.Empty)
        {
            throw new ArgumentException("Aggregate PublicId is required.", nameof(aggregatePublicId));
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc || availableAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Outbox timestamps must use UTC.");
        }

        if (availableAtUtc < occurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(availableAtUtc));
        }

        PublicId = publicId;
        Type = RequireStableText(type, nameof(type), 128);
        PayloadVersion = payloadVersion > 0
            ? payloadVersion
            : throw new ArgumentOutOfRangeException(nameof(payloadVersion));
        AggregateType = RequireStableText(aggregateType, nameof(aggregateType), 64);
        AggregatePublicId = aggregatePublicId;
        Payload = ValidatePayload(payload ?? throw new ArgumentNullException(nameof(payload)));
        OccurredAtUtc = occurredAtUtc;
        AvailableAtUtc = availableAtUtc;
        CorrelationId = RequireStableText(correlationId, nameof(correlationId), 64);
    }

    public Guid PublicId { get; }
    public string Type { get; }
    public int PayloadVersion { get; }
    public string AggregateType { get; }
    public Guid AggregatePublicId { get; }
    public object Payload { get; }
    public DateTime OccurredAtUtc { get; }
    public DateTime AvailableAtUtc { get; }
    public string CorrelationId { get; }

    public static OutboxWriteRequest Create(
        Guid publicId,
        string aggregateType,
        Guid aggregatePublicId,
        EmailNotificationRequestedV1 payload,
        DateTime occurredAtUtc,
        DateTime availableAtUtc,
        string correlationId) =>
        CreateKnown(
            publicId,
            OutboxEventTypes.EmailNotificationRequestedV1,
            aggregateType,
            aggregatePublicId,
            payload,
            occurredAtUtc,
            availableAtUtc,
            correlationId);

    public static OutboxWriteRequest Create(
        Guid publicId,
        string aggregateType,
        Guid aggregatePublicId,
        SimulatedInvoiceRequestedV1 payload,
        DateTime occurredAtUtc,
        DateTime availableAtUtc,
        string correlationId) =>
        CreateKnown(
            publicId,
            OutboxEventTypes.SimulatedInvoiceRequestedV1,
            aggregateType,
            aggregatePublicId,
            payload,
            occurredAtUtc,
            availableAtUtc,
            correlationId);

    public static OutboxWriteRequest Create(
        Guid publicId,
        string aggregateType,
        Guid aggregatePublicId,
        InAppNotificationRequestedV1 payload,
        DateTime occurredAtUtc,
        DateTime availableAtUtc,
        string correlationId) =>
        CreateKnown(
            publicId,
            OutboxEventTypes.InAppNotificationRequestedV1,
            aggregateType,
            aggregatePublicId,
            payload,
            occurredAtUtc,
            availableAtUtc,
            correlationId);

    public static OutboxWriteRequest Create(
        Guid publicId,
        string aggregateType,
        Guid aggregatePublicId,
        InventoryReconciliationMismatchDetectedV1 payload,
        DateTime occurredAtUtc,
        DateTime availableAtUtc,
        string correlationId) =>
        CreateKnown(
            publicId,
            OutboxEventTypes.InventoryReconciliationMismatchDetectedV1,
            aggregateType,
            aggregatePublicId,
            payload,
            occurredAtUtc,
            availableAtUtc,
            correlationId);

    private static OutboxWriteRequest CreateKnown(
        Guid publicId,
        string type,
        string aggregateType,
        Guid aggregatePublicId,
        object payload,
        DateTime occurredAtUtc,
        DateTime availableAtUtc,
        string correlationId) =>
        new(
            publicId,
            type,
            payloadVersion: 1,
            aggregateType,
            aggregatePublicId,
            payload,
            occurredAtUtc,
            availableAtUtc,
            correlationId);

    private static string RequireStableText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        value = value.Trim();
        if (value.Length > maximumLength || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '_' and not '-' and not ':'))
        {
            throw new ArgumentException("The value is not a stable identifier.", parameterName);
        }

        return value;
    }

    private static object ValidatePayload(object payload)
    {
        switch (payload)
        {
            case EmailNotificationRequestedV1 email:
                RequirePublicId(email.NotificationPublicId, nameof(email.NotificationPublicId));
                RequireStableText(email.TemplateKey, nameof(email.TemplateKey), 128);
                RequireStableText(email.RecipientPurpose, nameof(email.RecipientPurpose), 64);
                RequireStableText(email.ResourceType, nameof(email.ResourceType), 64);
                RequirePublicId(email.ResourcePublicId, nameof(email.ResourcePublicId));
                RequireStableText(email.Locale, nameof(email.Locale), 10);
                RequirePositiveVersion(email.ParameterSetVersion, nameof(email.ParameterSetVersion));
                break;
            case InAppNotificationRequestedV1 inApp:
                RequirePublicId(inApp.NotificationPublicId, nameof(inApp.NotificationPublicId));
                RequirePublicId(inApp.MemberPublicId, nameof(inApp.MemberPublicId));
                RequireStableText(inApp.MessageKey, nameof(inApp.MessageKey), 128);
                RequireStableText(inApp.ResourceType, nameof(inApp.ResourceType), 64);
                RequirePublicId(inApp.ResourcePublicId, nameof(inApp.ResourcePublicId));
                RequireStableText(inApp.Locale, nameof(inApp.Locale), 10);
                RequirePositiveVersion(inApp.ParameterSetVersion, nameof(inApp.ParameterSetVersion));
                break;
            case InventoryReconciliationMismatchDetectedV1 inventory:
                RequirePublicId(inventory.CasePublicId, nameof(inventory.CasePublicId));
                RequirePublicId(inventory.SkuPublicId, nameof(inventory.SkuPublicId));
                if (inventory.ExpectedOnHand < 0 || inventory.ActualOnHand < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(payload));
                }

                if (inventory.DetectedAtUtc.Kind != DateTimeKind.Utc)
                {
                    throw new ArgumentException("DetectedAtUtc must use UTC.", nameof(payload));
                }

                break;
            case SimulatedInvoiceRequestedV1 invoice:
                RequirePublicId(invoice.OrderPublicId, nameof(invoice.OrderPublicId));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    "The outbox payload type is not registered.");
        }

        return payload;
    }

    private static void RequirePublicId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("PublicId is required.", parameterName);
        }
    }

    private static void RequirePositiveVersion(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public interface IOutboxWriter
{
    OutboxMessage Add(OutboxWriteRequest request);
}
