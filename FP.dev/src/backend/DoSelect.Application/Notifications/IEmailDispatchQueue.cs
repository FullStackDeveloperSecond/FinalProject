namespace DoSelect.Application.Notifications;

/// <summary>
/// Hands an email off for asynchronous delivery. Enqueuing never awaits the underlying transport
/// (SMTP, etc.) — callers on the HTTP request path must not block on mail delivery, and the
/// existence of a request must not leak through response timing (e.g. register, forgot-password).
/// </summary>
public interface IEmailDispatchQueue
{
    void Enqueue(EmailMessage message);
}
