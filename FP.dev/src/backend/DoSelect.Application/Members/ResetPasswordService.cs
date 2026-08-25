namespace DoSelect.Application.Members;

public sealed record ResetPasswordCommand(Guid UserPublicId, string Token, string NewPassword);

public abstract record ResetPasswordResult
{
    public sealed record Success : ResetPasswordResult;

    public sealed record TokenInvalid : ResetPasswordResult;

    public sealed record PasswordRejected(IReadOnlyDictionary<string, string[]> Errors) : ResetPasswordResult;
}

public sealed class ResetPasswordService(IMemberPasswordResetGateway gateway)
{
    public async Task<ResetPasswordResult> ResetAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var outcome = await gateway.ResetPasswordAsync(
            command.UserPublicId,
            command.Token,
            command.NewPassword,
            cancellationToken);

        return outcome switch
        {
            ResetMemberPasswordOutcome.Success => new ResetPasswordResult.Success(),
            ResetMemberPasswordOutcome.TokenRejected => new ResetPasswordResult.TokenInvalid(),
            ResetMemberPasswordOutcome.PasswordRejected passwordRejected =>
                new ResetPasswordResult.PasswordRejected(
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["newPassword"] = passwordRejected.Reasons.ToArray(),
                    }),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(ResetMemberPasswordOutcome)} type '{outcome.GetType()}'."),
        };
    }
}
