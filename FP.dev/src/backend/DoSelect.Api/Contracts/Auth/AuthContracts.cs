using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DoSelect.Application.Common;
using DoSelect.Application.Members;
using DoSelect.Domain.Members;

namespace DoSelect.Api.Contracts.Auth;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RegisterRequest
{
    private string _email = string.Empty;
    private string _displayName = string.Empty;
    private string? _locale;

    [Required]
    [StringLength(320, MinimumLength = 3)]
    [EmailAddress]
    public string Email
    {
        get => _email;
        init => _email = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName
    {
        get => _displayName;
        init => _displayName = InputNormalization.Canonicalize(value);
    }

    public string? Locale
    {
        get => _locale;
        init => _locale = value is null ? null : InputNormalization.Canonicalize(value);
    }

    [Required]
    public int AcceptTermsVersion { get; init; }

    public RegisterMemberCommand ToCommand() => new(
        Email,
        Password,
        DisplayName,
        Locale,
        AcceptTermsVersion);
}

public sealed record RegisterAcceptedResponse(
    Guid PublicId,
    string EmailMasked,
    string AccountStatus);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EmailVerificationRequest
{
    private string _email = string.Empty;

    [Required]
    [StringLength(320, MinimumLength = 3)]
    [EmailAddress]
    public string Email
    {
        get => _email;
        init => _email = InputNormalization.Canonicalize(value);
    }

    public RequestEmailVerificationCommand ToCommand() => new(Email);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PasswordResetRequest
{
    private string _email = string.Empty;

    [Required]
    [StringLength(320, MinimumLength = 3)]
    [EmailAddress]
    public string Email
    {
        get => _email;
        init => _email = InputNormalization.Canonicalize(value);
    }

    public RequestPasswordResetCommand ToCommand() => new(Email);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PasswordResetConfirmRequest
{
    [Required]
    public Guid UserPublicId { get; init; }

    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string Token { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;

    public ResetPasswordCommand ToCommand() => new(UserPublicId, Token, NewPassword);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoginRequest
{
    private string _email = string.Empty;

    [Required]
    [StringLength(320, MinimumLength = 3)]
    [EmailAddress]
    public string Email
    {
        get => _email;
        init => _email = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }

    public LoginMemberCommand ToCommand() => new(Email, Password, RememberMe);
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
