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
