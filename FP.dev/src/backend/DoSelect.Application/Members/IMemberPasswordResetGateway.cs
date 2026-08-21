namespace DoSelect.Application.Members;

public interface IMemberPasswordResetGateway
{
    Task<RequestMemberPasswordResetOutcome> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<ResetMemberPasswordOutcome> ResetPasswordAsync(
        Guid userPublicId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public abstract record RequestMemberPasswordResetOutcome
{
    public sealed record Issued(Guid PublicId, string Email, string Token) : RequestMemberPasswordResetOutcome;

    // Covers "no such member", "not a member account", and any non-resettable lifecycle state
    // (suspended, anonymized, disabled) alike so the caller cannot infer account existence or
    // state from the outcome (API DTO與Schema契約.md: PasswordResetRequest 永遠回 202).
    public sealed record NotEligible : RequestMemberPasswordResetOutcome;
}

public abstract record ResetMemberPasswordOutcome
{
    public sealed record Success : ResetMemberPasswordOutcome;

    public sealed record TokenRejected : ResetMemberPasswordOutcome;

    public sealed record PasswordRejected(IReadOnlyCollection<string> Reasons) : ResetMemberPasswordOutcome;
}
