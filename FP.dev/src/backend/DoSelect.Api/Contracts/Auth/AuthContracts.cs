using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Members;
using DoSelect.Domain.Members;

namespace DoSelect.Api.Contracts.Auth;

public sealed class RegisterRequest
{
    [Required]
    [StringLength(320, MinimumLength = 3)]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;

    public string? Locale { get; init; }

    [Required]
    public int AcceptTermsVersion { get; init; }

    public RegisterMemberCommand ToCommand() =>
        new(Email.Trim(), Password, DisplayName.Trim(), Locale, AcceptTermsVersion);
}

public sealed record RegisterAcceptedResponse(
    Guid PublicId,
    string EmailMasked,
    string AccountStatus);

public sealed class EmailVerificationConfirmRequest
{
    [Required]
    public Guid UserPublicId { get; init; }

    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string Token { get; init; } = string.Empty;

    public ConfirmEmailVerificationCommand ToCommand() => new(UserPublicId, Token);
}

public sealed record EmailVerificationConfirmedResponse(string AccountStatus);

public sealed class LoginRequest
{
    [Required]
    [StringLength(320, MinimumLength = 3)]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }

    public LoginMemberCommand ToCommand() => new(Email.Trim(), Password, RememberMe);
}

public sealed record CurrentUserDto(
    Guid PublicId,
    string DisplayName,
    string EmailMasked,
    bool EmailVerified,
    string Locale);

public sealed record AuthSessionDto(
    bool IsAuthenticated,
    CurrentUserDto? User = null,
    DateTimeOffset? ExpiresAtUtc = null);

public static class AccountStatusTokens
{
    public static string ToToken(AccountStatus accountStatus) => accountStatus switch
    {
        AccountStatus.PendingEmailVerification => "pendingEmailVerification",
        AccountStatus.Active => "active",
        AccountStatus.Suspended => "suspended",
        AccountStatus.Anonymized => "anonymized",
        AccountStatus.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(accountStatus)),
    };
}

public static class LocaleTokens
{
    public static string ToToken(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "zh-TW",
        SupportedLocale.JaJp => "ja-JP",
        SupportedLocale.KoKr => "ko-KR",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };
}
