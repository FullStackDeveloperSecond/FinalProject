namespace DoSelect.Application.Security;

/// <summary>UC-ADMIN-AUTH-01 既有錯誤碼，加上規格未列出的少量新增（見各常數註解）。</summary>
public static class AdminAuthErrorCodes
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string AccountSuspended = "account_suspended";
    public const string TwoFactorRequired = "admin_two_factor_required";
    public const string TwoFactorInvalid = "admin_two_factor_invalid";
    public const string RecoveryCodeInvalid = "admin_recovery_code_invalid";

    /// <summary>⚠ 新增：AdminChallenge Cookie 缺失、過期或與 challengePublicId 不符。</summary>
    public const string ChallengeInvalid = "admin_challenge_invalid";

    /// <summary>⚠ 新增：帳密正確但尚未綁定 TOTP，必須先完成綁定流程。</summary>
    public const string EnrollmentRequired = "admin_two_factor_enrollment_required";
}
