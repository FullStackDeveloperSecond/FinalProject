using DoSelect.Domain.Members;

namespace DoSelect.Application.Security;

/// <summary>
/// 管理員登入的第一階段（帳密）結果：失敗、成功但需綁定 TOTP、或成功且需輸入 TOTP。
/// 成功不代表建立完整管理 Session——完整 Session 只在 2FA 完成後才簽發。
/// </summary>
public sealed class AdminLoginResult
{
    private AdminLoginResult(
        string? errorCode,
        bool requiresEnrollment,
        bool requiresTwoFactor,
        string? userId,
        Guid? publicId)
    {
        ErrorCode = errorCode;
        RequiresEnrollment = requiresEnrollment;
        RequiresTwoFactor = requiresTwoFactor;
        UserId = userId;
        PublicId = publicId;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    public bool RequiresEnrollment { get; }

    public bool RequiresTwoFactor { get; }

    public string? UserId { get; }

    public Guid? PublicId { get; }

    public static AdminLoginResult Failure(string errorCode) => new(errorCode, false, false, null, null);

    public static AdminLoginResult NeedsEnrollment(string userId, Guid publicId) =>
        new(null, true, false, userId, publicId);

    public static AdminLoginResult NeedsTwoFactor(string userId, Guid publicId) =>
        new(null, false, true, userId, publicId);
}

/// <summary>
/// 管理員帳密登入：驗證密碼、判斷鎖定與停用、依是否已綁定 TOTP 決定下一步。
/// 呼叫端（Api 層）負責簽發 AdminChallenge Cookie；本類別只做純決策，不碰 Cookie／DbContext。
/// </summary>
public sealed class AdminLoginUseCase
{
    private const int MaxFailedAttempts = 5;

    // ⚠ 設計決策：寫死 30 分鐘，不依賴 IdentityOptions.Lockout.DefaultLockoutTimeSpan——
    // 那是單一全域值，無法同時滿足 Admin 30 分鐘與（未來）Member 15 分鐘兩種時長。
    private static readonly TimeSpan AdminLockoutDuration = TimeSpan.FromMinutes(30);

    private readonly IAdminAuthGateway _gateway;
    private readonly TimeProvider _timeProvider;

    public AdminLoginUseCase(IAdminAuthGateway gateway, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _gateway = gateway;
        _timeProvider = timeProvider;
    }

    public async Task<AdminLoginResult> ExecuteAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return AdminLoginResult.Failure(AdminAuthErrorCodes.InvalidCredentials);
        }

        var user = await _gateway.FindAdminByEmailAsync(email.Trim(), cancellationToken);
        if (user is null)
        {
            return AdminLoginResult.Failure(AdminAuthErrorCodes.InvalidCredentials);
        }

        var lockoutEnd = await _gateway.GetLockoutEndAsync(user.UserId, cancellationToken);
        if (lockoutEnd is { } end && end > _timeProvider.GetUtcNow())
        {
            return AdminLoginResult.Failure(AdminAuthErrorCodes.AccountLocked);
        }

        if (user.AccountStatus != AccountStatus.Active || !user.IsAdminProfileActive)
        {
            return AdminLoginResult.Failure(AdminAuthErrorCodes.AccountSuspended);
        }

        if (!await _gateway.CheckPasswordAsync(user.UserId, password, cancellationToken))
        {
            await _gateway.IncrementAccessFailedCountAsync(user.UserId, cancellationToken);
            var failedCount = await _gateway.GetAccessFailedCountAsync(user.UserId, cancellationToken);

            if (failedCount >= MaxFailedAttempts)
            {
                await _gateway.SetLockoutEndAsync(
                    user.UserId,
                    _timeProvider.GetUtcNow().Add(AdminLockoutDuration),
                    cancellationToken);
                return AdminLoginResult.Failure(AdminAuthErrorCodes.AccountLocked);
            }

            return AdminLoginResult.Failure(AdminAuthErrorCodes.InvalidCredentials);
        }

        await _gateway.ResetAccessFailedCountAsync(user.UserId, cancellationToken);

        return user.TwoFactorEnabled
            ? AdminLoginResult.NeedsTwoFactor(user.UserId, user.PublicId)
            : AdminLoginResult.NeedsEnrollment(user.UserId, user.PublicId);
    }
}
