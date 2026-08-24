namespace DoSelect.Application.Security;

/// <summary>TOTP 驗證或 Recovery Code 兌換的結果。</summary>
public sealed class AdminTwoFactorResult
{
    private AdminTwoFactorResult(string? errorCode, AdminAuthUserSnapshot? user)
    {
        ErrorCode = errorCode;
        User = user;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    public AdminAuthUserSnapshot? User { get; }

    public static AdminTwoFactorResult Failure(string errorCode) => new(errorCode, null);

    public static AdminTwoFactorResult Success(AdminAuthUserSnapshot user) => new(null, user);
}

/// <summary>TOTP 綁定第一步：回傳秘鑰、otpauth URI 與 QR 碼圖片。</summary>
public sealed record AdminEnrollmentBeginResult(string SecretKey, string OtpAuthUri, string QrCodeDataUri);

/// <summary>TOTP 綁定確認結果：成功時附上僅顯示一次的 Recovery Code 清單。</summary>
public sealed class AdminEnrollmentConfirmResult
{
    private AdminEnrollmentConfirmResult(
        string? errorCode,
        AdminAuthUserSnapshot? user,
        IReadOnlyList<string>? recoveryCodes)
    {
        ErrorCode = errorCode;
        User = user;
        RecoveryCodes = recoveryCodes;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    public AdminAuthUserSnapshot? User { get; }

    public IReadOnlyList<string>? RecoveryCodes { get; }

    public static AdminEnrollmentConfirmResult Failure(string errorCode) => new(errorCode, null, null);

    public static AdminEnrollmentConfirmResult Success(
        AdminAuthUserSnapshot user, IReadOnlyList<string> recoveryCodes) =>
        new(null, user, recoveryCodes);
}

/// <summary>
/// 管理員第二階段（TOTP／Recovery Code／首次綁定）流程。呼叫端須先透過
/// AdminChallenge Cookie 確認密碼已驗證，本類別只做驗證邏輯本身。
/// 2FA 失敗不得影響密碼鎖定計數——規格只定義密碼鎖定，2FA 失敗次數不在範圍內。
/// </summary>
public sealed class AdminTwoFactorUseCase
{
    private const int RecoveryCodeCount = 10;

    private readonly IAdminAuthGateway _gateway;
    private readonly ITotpQrCodeGenerator _qrCodeGenerator;

    public AdminTwoFactorUseCase(IAdminAuthGateway gateway, ITotpQrCodeGenerator qrCodeGenerator)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(qrCodeGenerator);

        _gateway = gateway;
        _qrCodeGenerator = qrCodeGenerator;
    }

    public async Task<AdminTwoFactorResult> VerifyTotpAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        if (!await _gateway.VerifyTotpCodeAsync(userId, code.Trim(), cancellationToken))
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        var user = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        return user is null
            ? AdminTwoFactorResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid)
            : AdminTwoFactorResult.Success(user);
    }

    public async Task<AdminTwoFactorResult> RedeemRecoveryCodeAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.RecoveryCodeInvalid);
        }

        if (!await _gateway.RedeemRecoveryCodeAsync(userId, code.Trim(), cancellationToken))
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.RecoveryCodeInvalid);
        }

        var user = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        return user is null
            ? AdminTwoFactorResult.Failure(AdminAuthErrorCodes.RecoveryCodeInvalid)
            : AdminTwoFactorResult.Success(user);
    }

    public async Task<AdminEnrollmentBeginResult> BeginEnrollmentAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var secret = await _gateway.GetOrCreateAuthenticatorSecretAsync(userId, cancellationToken);
        var qrCodeDataUri = _qrCodeGenerator.CreatePngDataUri(secret.OtpAuthUri);
        return new AdminEnrollmentBeginResult(secret.SecretKey, secret.OtpAuthUri, qrCodeDataUri);
    }

    public async Task<AdminEnrollmentConfirmResult> ConfirmEnrollmentAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        if (!await _gateway.VerifyTotpCodeAsync(userId, code.Trim(), cancellationToken))
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        await _gateway.EnableTwoFactorAsync(userId, cancellationToken);
        var recoveryCodes = await _gateway.GenerateRecoveryCodesAsync(
            userId, RecoveryCodeCount, cancellationToken);

        var user = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        return user is null
            ? AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid)
            : AdminEnrollmentConfirmResult.Success(user, recoveryCodes);
    }

    /// <summary>
    /// ⚠ 新增：讓已登入管理員重新綁定 TOTP（例如換手機）。跟 <see cref="BeginEnrollmentAsync"/>
    /// 不同——這裡呼叫 <see cref="IAdminAuthGateway.ResetAuthenticatorSecretAsync"/>，
    /// 無條件產生新秘鑰，取代舊的。呼叫端（Api 層）需在 Confirm 成功後 bump
    /// SecurityStamp，讓既有 Session 失效（UC-ADMIN-AUTH-01 的撤銷情境）。
    /// </summary>
    public async Task<AdminEnrollmentBeginResult> BeginRebindAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var secret = await _gateway.ResetAuthenticatorSecretAsync(userId, cancellationToken);
        var qrCodeDataUri = _qrCodeGenerator.CreatePngDataUri(secret.OtpAuthUri);
        return new AdminEnrollmentBeginResult(secret.SecretKey, secret.OtpAuthUri, qrCodeDataUri);
    }

    /// <summary>驗證新裝置的碼、重新產生 Recovery Code（舊的一併失效）。</summary>
    public async Task<AdminEnrollmentConfirmResult> ConfirmRebindAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        if (!await _gateway.VerifyTotpCodeAsync(userId, code.Trim(), cancellationToken))
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        var recoveryCodes = await _gateway.GenerateRecoveryCodesAsync(
            userId, RecoveryCodeCount, cancellationToken);

        var user = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        return user is null
            ? AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid)
            : AdminEnrollmentConfirmResult.Success(user, recoveryCodes);
    }
}
