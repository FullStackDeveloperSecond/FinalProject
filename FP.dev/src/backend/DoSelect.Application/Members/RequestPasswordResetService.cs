using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Members;

public sealed record RequestPasswordResetCommand(string Email);

public sealed class RequestPasswordResetService(
    IMemberPasswordResetGateway gateway,
    IEmailSender emailSender,
    IOptions<FrontendLinkOptions> frontendLinkOptions)
{
    // Always completes without signalling whether the account exists (API DTO與Schema契約.md:
    // PasswordResetRequest 永遠回 202); the caller must not branch on the outcome.
    public async Task RequestAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var outcome = await gateway.RequestPasswordResetAsync(command.Email, cancellationToken);
        if (outcome is RequestMemberPasswordResetOutcome.Issued issued)
        {
            await emailSender.SendAsync(
                MemberPasswordResetEmailComposer.Compose(
                    issued.Email,
                    issued.PublicId,
                    issued.Token,
                    frontendLinkOptions.Value.BaseUrl),
                cancellationToken);
        }
    }
}
