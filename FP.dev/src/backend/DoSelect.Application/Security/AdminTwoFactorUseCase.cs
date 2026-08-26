using DoSelect.Domain.Members;

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
        if (user is null)
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        // ⚠ 重新驗證管理員資格：密碼驗證通過到完成 2FA 之間，帳號可能已被停權或移除
        // 管理員資格。沒有這個檢查，被停權的帳號仍能完成 2FA 取得新 Session（alex review
        // P1#4）。
        return IsEligible(user)
            ? AdminTwoFactorResult.Success(user)
            : AdminTwoFactorResult.Failure(AdminAuthErrorCodes.AccountSuspended);
    }

    public async Task<AdminTwoFactorResult> RedeemRecoveryCodeAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.RecoveryCodeInvalid);
        }

        // ⚠ alex review P1#5：資格檢查必須在兌換之前。Recovery Code 是單次有效——原本先兌換
        // 再檢查資格，若帳號在 challenge 期間被停權，API 雖然回 account_suspended，但這組碼
        // 已經永久失效，管理員平白少了一組救援碼卻什麼都沒換到。
        var userBeforeRedeem = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        if (userBeforeRedeem is null)
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.RecoveryCodeInvalid);
        }

        if (!IsEligible(userBeforeRedeem))
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.AccountSuspended);
        }

        if (!await _gateway.RedeemRecoveryCodeAsync(userId, code.Trim(), cancellationToken))
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.RecoveryCodeInvalid);
        }

        var user = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return AdminTwoFactorResult.Failure(AdminAuthErrorCodes.RecoveryCodeInvalid);
        }

        return IsEligible(user)
            ? AdminTwoFactorResult.Success(user)
            : AdminTwoFactorResult.Failure(AdminAuthErrorCodes.AccountSuspended);
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

        // ⚠ alex review 第二輪 P1#4：資格檢查必須在「啟用 2FA、產生 Recovery Codes」之前。
        // 原本檢查放在最後——管理員在流程中被停權時，API 雖然回 account_suspended，但
        // 2FA 已經啟用、舊 Recovery Codes 已作廢，使用者卻拿不到這次產生的新 codes，
        // 帳號會卡在半啟用、無法使用的狀態。
        var userBeforeMutation = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        if (userBeforeMutation is null)
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        if (!IsEligible(userBeforeMutation))
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.AccountSuspended);
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
    /// ⚠ 新增：讓已登入管理員重新綁定 TOTP（例如換手機）。新秘鑰先存在待確認 slot
    /// （見 <see cref="IAdminAuthGateway.BeginRebindSecretAsync"/>），舊的正式 authenticator
    /// key 在 Confirm 成功前完全不受影響——中途放棄或確認失敗，舊裝置仍可正常登入。
    /// 呼叫端（Api 層）需在 Confirm 成功後 bump SecurityStamp，讓既有 Session 失效
    /// （UC-ADMIN-AUTH-01 的撤銷情境）。
    /// </summary>
    public async Task<AdminEnrollmentBeginResult> BeginRebindAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var secret = await _gateway.BeginRebindSecretAsync(userId, cancellationToken);
        var qrCodeDataUri = _qrCodeGenerator.CreatePngDataUri(secret.OtpAuthUri);
        return new AdminEnrollmentBeginResult(secret.SecretKey, secret.OtpAuthUri, qrCodeDataUri);
    }

    /// <summary>
    /// 驗證新裝置的碼、把待確認秘鑰提升為正式 key、重新產生 Recovery Code（舊的一併失效）。
    /// 驗證失敗時 <see cref="IAdminAuthGateway.PromotePendingSecretAndVerifyAsync"/> 回傳
    /// false——呼叫端須在同一交易中 rollback，讓「提升為正式 key」這步也一併復原。
    /// </summary>
    public async Task<AdminEnrollmentConfirmResult> ConfirmRebindAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        if (!await _gateway.PromotePendingSecretAndVerifyAsync(userId, code.Trim(), cancellationToken))
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        var recoveryCodes = await _gateway.GenerateRecoveryCodesAsync(
            userId, RecoveryCodeCount, cancellationToken);

        var user = await _gateway.FindAdminByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.TwoFactorInvalid);
        }

        return IsEligible(user)
            ? AdminEnrollmentConfirmResult.Success(user, recoveryCodes)
            : AdminEnrollmentConfirmResult.Failure(AdminAuthErrorCodes.AccountSuspended);
    }

    /// <summary>
    /// 密碼驗證通過後到完成 2FA 之間，帳號可能已被停權或移除管理員資格；這裡用跟登入
    /// 相同的兩個旗標重新檢查一次，避免用「舊資格」完成 2FA 取得新 Session（alex review
    /// P1#4）。
    /// </summary>
    private static bool IsEligible(AdminAuthUserSnapshot user) =>
        user.AccountStatus == AccountStatus.Active && user.IsAdminProfileActive;
}
