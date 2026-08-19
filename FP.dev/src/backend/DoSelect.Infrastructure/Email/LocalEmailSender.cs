using DoSelect.Application.Notifications;

namespace DoSelect.Infrastructure.Email;

public sealed class LocalEmailSender : IEmailSender
{
    public Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var result = EmailMessageFactory.IsValid(message)
            ? new EmailDeliveryResult(
                EmailDeliveryStatus.Suppressed,
                ErrorCode: EmailDeliveryErrorCodes.Suppressed)
            : new EmailDeliveryResult(
                EmailDeliveryStatus.PermanentFailure,
                ErrorCode: EmailDeliveryErrorCodes.InvalidMessage);

        return Task.FromResult(result);
    }
}
