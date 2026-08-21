using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Members;

public sealed record RequestEmailVerificationCommand(string Email);

public enum RequestEmailVerificationResult
{
    Completed,
    RateLimited,
}

public sealed class RequestEmailVerificationService(
    IMemberRegistrationGateway gateway,
    IEmailDispatchQueue emailDispatchQueue,
    IEmailRequestThrottle emailRequestThrottle,
    IOptions<FrontendLinkOptions> frontendLinkOptions)
{
    private const string ThrottlePurpose = "email-verification";

    // Always completes without signalling whether the account exists or was already verified
    // (API DTO與Schema契約.md: EmailVerificationRequest 永遠回 202，不揭露帳號); the caller must
    // not branch on the outcome other than for the rate-limited case, which does not reveal
    // account existence either — the same email is throttled whether or not it belongs to an
    // account.
    public async Task<RequestEmailVerificationResult> RequestAsync(
        RequestEmailVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!emailRequestThrottle.TryAcquire(ThrottlePurpose, command.Email))
        {
            return RequestEmailVerificationResult.RateLimited;
        }

        var outcome = await gateway.RequestEmailVerificationAsync(command.Email, cancellationToken);
        if (outcome is RequestMemberEmailVerificationOutcome.Issued issued)
        {
            emailDispatchQueue.Enqueue(
                MemberVerificationEmailComposer.Compose(
                    issued.Email,
                    issued.PublicId,
                    issued.Token,
                    frontendLinkOptions.Value.BaseUrl));
        }

        return RequestEmailVerificationResult.Completed;
    }
}
