namespace DoSelect.Application.Notifications;

public interface IEmailSender
{
    Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    string RecipientAddress,
    string Subject,
    string TextBody,
    string? HtmlBody = null);

public enum EmailDeliveryStatus
{
    Sent,
    Suppressed,
    TransientFailure,
    PermanentFailure,
}

public sealed record EmailDeliveryResult(
    EmailDeliveryStatus Status,
    string? MessageId = null,
    string? ErrorCode = null)
{
    public bool WasDelivered => Status == EmailDeliveryStatus.Sent;
}

public static class EmailDeliveryErrorCodes
{
    public const string Suppressed = "email_delivery_suppressed";
    public const string InvalidMessage = "email_invalid_message";
    public const string AuthenticationFailed = "email_authentication_failed";
    public const string Rejected = "email_rejected";
    public const string TransportUnavailable = "email_transport_unavailable";
    public const string ContentUnavailable = "email_content_unavailable";
    public const string DeliveryAmbiguous = "email_delivery_ambiguous";
}
