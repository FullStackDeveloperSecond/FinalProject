using DoSelect.Domain.Members;

namespace DoSelect.Application.Members;

public interface IMemberLoginGateway
{
    Task<MemberLoginOutcome> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<MemberSessionSnapshot?> FindActiveMemberByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed record MemberSessionSnapshot(
    Guid PublicId,
    string DisplayName,
    string Email,
    bool EmailVerified,
    SupportedLocale Locale);

public abstract record MemberLoginOutcome
{
    public sealed record Success(
        Guid PublicId,
        string UserId,
        string DisplayName,
        string Email,
        AccountStatus AccountStatus,
        SupportedLocale Locale,
        string SecurityStamp) : MemberLoginOutcome;

    public sealed record InvalidCredentials : MemberLoginOutcome;

    public sealed record LockedOut(DateTimeOffset LockoutEndUtc) : MemberLoginOutcome;

    public sealed record EmailUnverified : MemberLoginOutcome;

    public sealed record Suspended : MemberLoginOutcome;
}
