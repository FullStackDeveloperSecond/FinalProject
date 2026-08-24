using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using DoSelect.Domain.Members;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Members;

public sealed record RegisterMemberCommand(
    string Email,
    string Password,
    string DisplayName,
    string? Locale,
    int AcceptTermsVersion);

public abstract record RegisterMemberResult
{
    public sealed record Success(
        Guid PublicId,
        string EmailMasked,
        AccountStatus AccountStatus) : RegisterMemberResult;

    public sealed record ValidationFailed(
        IReadOnlyDictionary<string, string[]> Errors) : RegisterMemberResult;

    public sealed record RateLimited : RegisterMemberResult;
}

public sealed class RegisterMemberService(
    IMemberRegistrationGateway gateway,
    IEmailDispatchQueue emailDispatchQueue,
    IEmailRequestThrottle emailRequestThrottle,
    IOptions<FrontendLinkOptions> frontendLinkOptions)
{
    private const string ThrottlePurpose = "register";

    // No terms-of-service version registry is documented yet; the currently accepted
    // version is pinned here until one exists.
    public const int CurrentTermsVersion = 1;

    public async Task<RegisterMemberResult> RegisterAsync(
        RegisterMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!emailRequestThrottle.TryAcquire(ThrottlePurpose, command.Email))
        {
            return new RegisterMemberResult.RateLimited();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (command.AcceptTermsVersion != CurrentTermsVersion)
        {
            errors["acceptTermsVersion"] = ["The current terms of service version must be accepted."];
        }

        if (!TryResolveLocale(command.Locale, out var locale))
        {
            errors["locale"] = ["The locale is not supported."];
        }

        if (errors.Count > 0)
        {
            return new RegisterMemberResult.ValidationFailed(errors);
        }

        var outcome = await gateway.CreateMemberAsync(
            new CreateMemberRequest(command.Email, command.Password, command.DisplayName, locale),
            cancellationToken);

        switch (outcome)
        {
            case CreateMemberOutcome.EmailInUse:
                // Non-enumerable by design (Alex review, 2026-08-21): the public response for an
                // already-registered email must be indistinguishable from a fresh registration —
                // same status code, same shape, no real PublicId of the existing account. Emitting
                // a distinct 409/error here (as before) let an unauthenticated caller test which
                // emails are already members, which the acceptance spec explicitly forbids. The
                // synthetic PublicId must also use the same UUID version as a real one
                // (CreateVersion7): a v4 fallback here was itself an oracle — the version nibble
                // in the returned GUID told an attacker new vs. duplicate apart even though the
                // rest of the response was identical (Alex review, 2026-08-24).
                return new RegisterMemberResult.Success(
                    Guid.CreateVersion7(),
                    EmailMasking.Mask(command.Email),
                    AccountStatus.PendingEmailVerification);

            case CreateMemberOutcome.PasswordRejected passwordRejected:
                return new RegisterMemberResult.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["password"] = passwordRejected.Reasons.ToArray(),
                    });

            case CreateMemberOutcome.Success success:
                SendVerificationEmail(success);
                return new RegisterMemberResult.Success(
                    success.PublicId,
                    EmailMasking.Mask(success.Email),
                    success.AccountStatus);

            default:
                throw new InvalidOperationException(
                    $"Unhandled {nameof(CreateMemberOutcome)} type '{outcome.GetType()}'.");
        }
    }

    private void SendVerificationEmail(CreateMemberOutcome.Success success) =>
        emailDispatchQueue.Enqueue(
            MemberVerificationEmailComposer.Compose(
                success.Email,
                success.PublicId,
                success.EmailConfirmationToken,
                frontendLinkOptions.Value.BaseUrl));

    private static bool TryResolveLocale(string? rawLocale, out SupportedLocale locale)
    {
        if (string.IsNullOrWhiteSpace(rawLocale))
        {
            locale = SupportedLocale.ZhTw;
            return true;
        }

        switch (rawLocale)
        {
            case "zh-TW":
                locale = SupportedLocale.ZhTw;
                return true;
            case "ja-JP":
                locale = SupportedLocale.JaJp;
                return true;
            case "ko-KR":
                locale = SupportedLocale.KoKr;
                return true;
            default:
                locale = default;
                return false;
        }
    }
}
