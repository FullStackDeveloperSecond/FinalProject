using DoSelect.Application.Outbox;

namespace DoSelect.Application.Notifications;

public sealed record EmailNotificationContent(
    string? RecipientUserId,
    EmailMessage Message);

public interface IEmailNotificationContentResolver
{
    Task<EmailNotificationContent?> ResolveAsync(
        EmailNotificationRequestedV1 request,
        CancellationToken cancellationToken = default);
}

public sealed record InAppNotificationContent(
    string Type,
    string Title,
    string Body,
    DateTime? ExpiresAtUtc = null);

public interface IInAppNotificationContentRenderer
{
    InAppNotificationContent? Render(InAppNotificationRequestedV1 request);
}
