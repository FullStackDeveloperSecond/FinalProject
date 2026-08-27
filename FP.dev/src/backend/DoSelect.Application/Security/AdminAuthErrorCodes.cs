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

    /// <summary>⚠ 新增：2FA 挑戰嘗試次數超過門檻，challenge 已被強制失效。</summary>
    public const string ChallengeRateLimited = "admin_challenge_rate_limited";

    /// <summary>
    /// ⚠ 新增（alex review 裁定 A1）：Rebind 簽發前必須先驗證現有 TOTP 或消耗一組 Recovery
    /// Code；請求沒有恰好帶其中一種憑證時使用。
    /// </summary>
    public const string RebindStepUpRequired = "admin_rebind_step_up_required";
}
