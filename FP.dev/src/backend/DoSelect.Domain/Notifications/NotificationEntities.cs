using DoSelect.Domain.Common;

namespace DoSelect.Domain.Notifications;

public enum EmailDeliveryStatus
{
    Pending,
    Processing,
    Sent,
    Suppressed,
    Failed,
}

public sealed class Notification : PublicEntity
{
    private Notification()
    {
    }

    public Notification(
        Guid publicId,
        string recipientUserId,
        string type,
        string title,
        string body,
        string? resourceType,
        Guid? resourcePublicId,
        DateTime? expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if ((resourceType is null) != (resourcePublicId is null))
        {
            throw new ArgumentException("Resource type and PublicId must be supplied together.");
        }

        if (resourcePublicId == Guid.Empty)
        {
            throw new ArgumentException("Resource PublicId cannot be empty.", nameof(resourcePublicId));
        }

        if (expiresAtUtc is not null)
        {
            expiresAtUtc = RequireUtc(expiresAtUtc.Value, nameof(expiresAtUtc));
            if (expiresAtUtc <= createdAtUtc)
            {
                throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            }
        }

        RecipientUserId = RequireBoundedText(recipientUserId, nameof(recipientUserId), 450);
        Type = RequireStableText(type, nameof(type), 64);
        Title = RequireBoundedText(title, nameof(title), 200);
        Body = RequireBoundedText(body, nameof(body), 1_000);
        ResourceType = resourceType is null
            ? null
            : RequireStableText(resourceType, nameof(resourceType), 64);
        ResourcePublicId = resourcePublicId;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string RecipientUserId { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? ResourceType { get; private set; }
    public Guid? ResourcePublicId { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }

    public void MarkRead(DateTime readAtUtc)
    {
        readAtUtc = RequireUtc(readAtUtc, nameof(readAtUtc));
        ReadAtUtc ??= readAtUtc;
    }

    private static string RequireBoundedText(string value, string parameterName, int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string RequireStableText(string value, string parameterName, int maximumLength)
    {
        value = RequireBoundedText(value, parameterName, maximumLength);
        return value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character == 46 || character == 95 || character == 45 || character == 58)
            ? value
            : throw new ArgumentException("The value must be a stable identifier.", parameterName);
    }
}

public sealed class EmailDelivery : MutableEntity
{
    private EmailDelivery()
    {
    }

    public EmailDelivery(
        Guid notificationPublicId,
        string? recipientUserId,
        string recipientEmailNormalized,
        string templateCode,
        int templateVersion,
        string recipientPurpose,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (notificationPublicId == Guid.Empty)
        {
            throw new ArgumentException("Notification PublicId is required.", nameof(notificationPublicId));
        }

        if (templateVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(templateVersion));
        }

        NotificationPublicId = notificationPublicId;
        RecipientUserId = string.IsNullOrWhiteSpace(recipientUserId)
            ? null
            : RequireBoundedText(recipientUserId, nameof(recipientUserId), 450);
        RecipientEmailNormalized = RequireBoundedText(
            recipientEmailNormalized,
            nameof(recipientEmailNormalized),
            320).ToUpperInvariant();
        TemplateCode = RequireStableText(templateCode, nameof(templateCode), 64);
        TemplateVersion = templateVersion;
        RecipientPurpose = RequireStableText(recipientPurpose, nameof(recipientPurpose), 64);
        Status = EmailDeliveryStatus.Pending;
        NextAttemptAtUtc = createdAtUtc;
    }

    public Guid NotificationPublicId { get; private set; }
    public string? RecipientUserId { get; private set; }
    public string RecipientEmailNormalized { get; private set; } = string.Empty;
    public string TemplateCode { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public string RecipientPurpose { get; private set; } = string.Empty;
    public EmailDeliveryStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public string? LastErrorCode { get; private set; }

    public void BeginAttempt(DateTime attemptedAtUtc)
    {
        attemptedAtUtc = RequireUtc(attemptedAtUtc, nameof(attemptedAtUtc));
        if (Status != EmailDeliveryStatus.Pending ||
            NextAttemptAtUtc is null ||
            NextAttemptAtUtc > attemptedAtUtc)
        {
            throw new InvalidOperationException("The email delivery is not ready for an attempt.");
        }

        Status = EmailDeliveryStatus.Processing;
        AttemptCount++;
        NextAttemptAtUtc = null;
        LastErrorCode = null;
        MarkUpdated(attemptedAtUtc);
    }

    public void MarkSent(string providerMessageId, DateTime sentAtUtc)
    {
        EnsureProcessing();
        Status = EmailDeliveryStatus.Sent;
        ProviderMessageId = RequireBoundedText(providerMessageId, nameof(providerMessageId), 128);
        SentAtUtc = RequireUtc(sentAtUtc, nameof(sentAtUtc));
        MarkUpdated(sentAtUtc);
    }

    public void MarkSuppressed(string errorCode, DateTime completedAtUtc)
    {
        EnsureProcessing();
        Status = EmailDeliveryStatus.Suppressed;
        LastErrorCode = RequireStableText(errorCode, nameof(errorCode), 64);
        FailedAtUtc = RequireUtc(completedAtUtc, nameof(completedAtUtc));
        MarkUpdated(completedAtUtc);
    }

    public void ScheduleRetry(string errorCode, DateTime nextAttemptAtUtc, DateTime failedAtUtc)
    {
        EnsureProcessing();
        nextAttemptAtUtc = RequireUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        failedAtUtc = RequireUtc(failedAtUtc, nameof(failedAtUtc));
        if (nextAttemptAtUtc <= failedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAtUtc));
        }

        Status = EmailDeliveryStatus.Pending;
        LastErrorCode = RequireStableText(errorCode, nameof(errorCode), 64);
        NextAttemptAtUtc = nextAttemptAtUtc;
        FailedAtUtc = failedAtUtc;
        MarkUpdated(failedAtUtc);
    }

    public void MarkFailed(string errorCode, DateTime failedAtUtc)
    {
        EnsureProcessing();
        Status = EmailDeliveryStatus.Failed;
        LastErrorCode = RequireStableText(errorCode, nameof(errorCode), 64);
        FailedAtUtc = RequireUtc(failedAtUtc, nameof(failedAtUtc));
        MarkUpdated(failedAtUtc);
    }

    private void EnsureProcessing()
    {
        if (Status != EmailDeliveryStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing email delivery can complete.");
        }
    }

    private static string RequireBoundedText(string value, string parameterName, int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string RequireStableText(string value, string parameterName, int maximumLength)
    {
        value = RequireBoundedText(value, parameterName, maximumLength);
        return value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character == 46 || character == 95 || character == 45 || character == 58)
            ? value
            : throw new ArgumentException("The value must be a stable identifier.", parameterName);
    }
}
