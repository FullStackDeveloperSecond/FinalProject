using DoSelect.Domain.Members;

namespace DoSelect.Application.Members;

public sealed record LoginMemberCommand(string Email, string Password, bool RememberMe);

public abstract record LoginMemberResult
{
    public sealed record Success(
        Guid PublicId,
        string UserId,
        string DisplayName,
        string EmailMasked,
        SupportedLocale Locale,
        bool RememberMe,
        string SecurityStamp) : LoginMemberResult;

    public sealed record InvalidCredentials : LoginMemberResult;

    public sealed record LockedOut(DateTimeOffset LockoutEndUtc) : LoginMemberResult;

    public sealed record EmailUnverified : LoginMemberResult;

    public sealed record Suspended : LoginMemberResult;
}

public abstract record MemberSessionResult
{
    public sealed record Authenticated(
        Guid PublicId,
        string DisplayName,
        string EmailMasked,
        bool EmailVerified,
        SupportedLocale Locale) : MemberSessionResult;

    public sealed record Anonymous : MemberSessionResult;
}

public sealed class LoginMemberService(IMemberLoginGateway gateway)
{
    public async Task<LoginMemberResult> LoginAsync(
        LoginMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var outcome = await gateway.ValidateCredentialsAsync(
            command.Email,
            command.Password,
            cancellationToken);

        return outcome switch
        {
            MemberLoginOutcome.Success success => new LoginMemberResult.Success(
                success.PublicId,
                success.UserId,
                success.DisplayName,
                EmailMasking.Mask(success.Email),
                success.Locale,
                command.RememberMe,
                success.SecurityStamp),
            MemberLoginOutcome.InvalidCredentials => new LoginMemberResult.InvalidCredentials(),
            MemberLoginOutcome.LockedOut lockedOut => new LoginMemberResult.LockedOut(lockedOut.LockoutEndUtc),
            MemberLoginOutcome.EmailUnverified => new LoginMemberResult.EmailUnverified(),
            MemberLoginOutcome.Suspended => new LoginMemberResult.Suspended(),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(MemberLoginOutcome)} type '{outcome.GetType()}'."),
        };
    }

    public async Task<MemberSessionResult> GetSessionAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new MemberSessionResult.Anonymous();
        }

        var snapshot = await gateway.FindActiveMemberByUserIdAsync(userId, cancellationToken);
        return snapshot is null
            ? new MemberSessionResult.Anonymous()
            : new MemberSessionResult.Authenticated(
                snapshot.PublicId,
                snapshot.DisplayName,
                EmailMasking.Mask(snapshot.Email),
                snapshot.EmailVerified,
                snapshot.Locale);
    }
}
