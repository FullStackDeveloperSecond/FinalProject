using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Members;

public sealed record RequestPasswordResetCommand(string Email);

public enum RequestPasswordResetResult
{
    Completed,
    RateLimited,
}

public sealed class RequestPasswordResetService(
    IMemberPasswordResetGateway gateway,
    IEmailDispatchQueue emailDispatchQueue,
    IEmailRequestThrottle emailRequestThrottle,
    IOptions<FrontendLinkOptions> frontendLinkOptions)
{
    private const string ThrottlePurpose = "password-reset";

    // Always completes without signalling whether the account exists (API DTO與Schema契約.md:
    // PasswordResetRequest 永遠回 202); the caller must not branch on the outcome other than for
    // the rate-limited case, which does not reveal account existence either — the same email is
    // throttled whether or not it belongs to an account.
    public async Task<RequestPasswordResetResult> RequestAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!emailRequestThrottle.TryAcquire(ThrottlePurpose, command.Email))
        {
            return RequestPasswordResetResult.RateLimited;
        }

        var outcome = await gateway.RequestPasswordResetAsync(command.Email, cancellationToken);
        if (outcome is RequestMemberPasswordResetOutcome.Issued issued)
        {
            emailDispatchQueue.Enqueue(
                MemberPasswordResetEmailComposer.Compose(
                    issued.Email,
                    issued.PublicId,
                    issued.Token,
                    frontendLinkOptions.Value.BaseUrl));
        }

        return RequestPasswordResetResult.Completed;
    }
}
