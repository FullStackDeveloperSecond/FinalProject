using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Members;

public sealed record RequestEmailVerificationCommand(string Email);

public sealed class RequestEmailVerificationService(
    IMemberRegistrationGateway gateway,
    IEmailSender emailSender,
    IOptions<FrontendLinkOptions> frontendLinkOptions)
{
    // Always completes without signalling whether the account exists or was already verified
    // (API DTO與Schema契約.md: EmailVerificationRequest 永遠回 202，不揭露帳號); the caller must
    // not branch on the outcome.
    public async Task RequestAsync(
        RequestEmailVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var outcome = await gateway.RequestEmailVerificationAsync(command.Email, cancellationToken);
        if (outcome is RequestMemberEmailVerificationOutcome.Issued issued)
        {
            await emailSender.SendAsync(
                MemberVerificationEmailComposer.Compose(
                    issued.Email,
                    issued.PublicId,
                    issued.Token,
                    frontendLinkOptions.Value.BaseUrl),
                cancellationToken);
        }
    }
}
