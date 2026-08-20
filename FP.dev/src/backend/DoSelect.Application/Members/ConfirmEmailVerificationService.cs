using DoSelect.Domain.Members;

namespace DoSelect.Application.Members;

public sealed record ConfirmEmailVerificationCommand(Guid UserPublicId, string Token);

public abstract record ConfirmEmailVerificationResult
{
    public sealed record Success(AccountStatus AccountStatus) : ConfirmEmailVerificationResult;

    public sealed record TokenInvalid : ConfirmEmailVerificationResult;
}

public sealed class ConfirmEmailVerificationService(IMemberRegistrationGateway gateway)
{
    public async Task<ConfirmEmailVerificationResult> ConfirmAsync(
        ConfirmEmailVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var outcome = await gateway.ConfirmEmailAsync(
            command.UserPublicId,
            command.Token,
            cancellationToken);

        return outcome switch
        {
            ConfirmMemberEmailOutcome.Success success =>
                new ConfirmEmailVerificationResult.Success(success.AccountStatus),
            ConfirmMemberEmailOutcome.TokenRejected =>
                new ConfirmEmailVerificationResult.TokenInvalid(),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(ConfirmMemberEmailOutcome)} type '{outcome.GetType()}'."),
        };
    }
}
