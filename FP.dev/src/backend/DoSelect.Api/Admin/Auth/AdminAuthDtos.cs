using System.ComponentModel.DataAnnotations;

namespace DoSelect.Api.Admin.Auth;

/// <summary>對應 API DTO 契約 AuthSessionDto——未登入或未完成 2FA 一律 IsAuthenticated=false，不外洩 Roles。</summary>
public sealed record AuthSessionDto(
    bool IsAuthenticated,
    CurrentUserDto? User,
    DateTimeOffset? ExpiresAtUtc,
    bool? RequiresTwoFactor);

/// <summary>對應 API DTO 契約 CurrentUserDto。Roles 只在管理端回傳。</summary>
public sealed record CurrentUserDto(
    Guid PublicId,
    string DisplayName,
    string EmailMasked,
    bool EmailVerified,
    string Locale,
    IReadOnlyList<string>? Roles);

public sealed record AdminLoginRequest(
    [Required, EmailAddress, StringLength(320, MinimumLength = 3)] string Email,
    [Required, StringLength(128, MinimumLength = 1)] string Password);

/// <summary>
/// requiresEnrollment 是規格 4 端點以外的新增欄位——尚未綁定 TOTP 的管理員需要先走綁定流程。
/// </summary>
public sealed record AdminLoginResponseDto(
    bool RequiresTwoFactor,
    bool RequiresEnrollment,
    Guid TwoFactorChallengePublicId);

public sealed record TotpVerifyRequest(
    Guid ChallengePublicId,
    [Required, RegularExpression(@"^\d{6}$")] string Code);

public sealed record RecoveryCodeUseRequest(
    Guid ChallengePublicId,
    [Required, StringLength(64, MinimumLength = 8)] string Code);

public sealed record AdminAuthResultDto(CurrentUserDto User, DateTimeOffset ExpiresAtUtc);

/// <summary>⚠ 新增端點回應：TOTP 綁定第一步。</summary>
public sealed record TotpEnrollBeginResponseDto(string SecretKey, string OtpAuthUri, string QrCodeDataUri);

/// <summary>⚠ 新增端點請求：TOTP 綁定確認。</summary>
public sealed record TotpEnrollConfirmRequest(
    Guid ChallengePublicId,
    [Required, RegularExpression(@"^\d{6}$")] string Code);

/// <summary>⚠ 新增端點回應：Recovery Code 只在這裡顯示一次，之後無法再次取得。</summary>
public sealed record TotpEnrollConfirmResponseDto(
    IReadOnlyList<string> RecoveryCodes,
    CurrentUserDto User,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// ⚠ 新增端點回應：Rebind 第一步。跟 <see cref="TotpEnrollBeginResponseDto"/> 不同之處是多帶一個
/// 短效、單次、綁定使用者的 ChallengePublicId（DEC-P297）——confirm 必須附上同一組值才算數。
/// </summary>
public sealed record TotpRebindBeginResponseDto(
    string SecretKey, string OtpAuthUri, string QrCodeDataUri, Guid ChallengePublicId);

/// <summary>
/// ⚠ 新增端點請求：已登入管理員重新綁定 TOTP 的確認步驟（換手機情境）。
/// 對應 UC-ADMIN-AUTH-01「TOTP 重新綁定，既有 Session 失效」的觸發入口。ChallengePublicId
/// 必須符合 BeginRebind 簽發的短效 Challenge（DEC-P297，alex review P1#3）。
/// </summary>
public sealed record TotpRebindConfirmRequest(
    Guid ChallengePublicId,
    [Required, RegularExpression(@"^\d{6}$")] string Code);
