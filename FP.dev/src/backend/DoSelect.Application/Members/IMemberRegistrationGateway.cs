using DoSelect.Domain.Members;

namespace DoSelect.Application.Members;

public interface IMemberRegistrationGateway
{
    Task<CreateMemberOutcome> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken = default);

    Task<ConfirmMemberEmailOutcome> ConfirmEmailAsync(
        Guid userPublicId,
        string token,
        CancellationToken cancellationToken = default);

    Task<RequestMemberEmailVerificationOutcome> RequestEmailVerificationAsync(
        string email,
        CancellationToken cancellationToken = default);
}

public sealed record CreateMemberRequest(
    string Email,
    string Password,
    string DisplayName,
    SupportedLocale Locale);

public abstract record CreateMemberOutcome
{
    public sealed record Success(
        Guid PublicId,
        string Email,
        AccountStatus AccountStatus,
        string EmailConfirmationToken) : CreateMemberOutcome;

    public sealed record EmailInUse : CreateMemberOutcome;

    public sealed record PasswordRejected(IReadOnlyCollection<string> Reasons) : CreateMemberOutcome;
}

public abstract record ConfirmMemberEmailOutcome
{
    public sealed record Success(AccountStatus AccountStatus) : ConfirmMemberEmailOutcome;

    public sealed record TokenRejected : ConfirmMemberEmailOutcome;
}

public abstract record RequestMemberEmailVerificationOutcome
{
    public sealed record Issued(Guid PublicId, string Email, string Token) : RequestMemberEmailVerificationOutcome;

    // Covers "no such member", "not a member account", and "already verified" alike so the
    // caller cannot infer account existence or state from the outcome (API DTO與Schema契約.md:
    // EmailVerificationRequest 永遠回 202，不揭露帳號).
    public sealed record NotEligible : RequestMemberEmailVerificationOutcome;
}
