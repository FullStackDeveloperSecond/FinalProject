using DoSelect.Domain.Members;

namespace DoSelect.Application.Security;

/// <summary>
/// 管理員登入所需的使用者快照。Application 層不依賴 Identity 型別，
/// 由 Infrastructure 的 <see cref="IAdminAuthGateway"/> 實作負責轉換。
/// </summary>
public sealed record AdminAuthUserSnapshot(
    string UserId,
    Guid PublicId,
    string Email,
    string DisplayName,
    AccountStatus AccountStatus,
    SupportedLocale PreferredLocale,
    bool EmailConfirmed,
    bool IsAdminProfileActive,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles);

/// <summary>TOTP 綁定所需的秘鑰與掃碼用 URI。</summary>
public sealed record AdminTotpSecret(string SecretKey, string OtpAuthUri);

/// <summary>
/// 管理員登入／TOTP／Recovery Code 的讀寫埠。實作屬於 Infrastructure，
/// 一律透過 ASP.NET Core Identity 既有的 UserManager／Token Provider Store，
/// 不另外新增資料表（TOTP 秘鑰、Recovery Code 都存在既有 AspNetUserTokens）。
/// </summary>
public interface IAdminAuthGateway
{
    Task<AdminAuthUserSnapshot?> FindAdminByEmailAsync(
        string email, CancellationToken cancellationToken = default);

    Task<AdminAuthUserSnapshot?> FindAdminByIdAsync(
        string userId, CancellationToken cancellationToken = default);

    Task<bool> CheckPasswordAsync(
        string userId, string password, CancellationToken cancellationToken = default);

    Task<int> GetAccessFailedCountAsync(
        string userId, CancellationToken cancellationToken = default);

    Task IncrementAccessFailedCountAsync(
        string userId, CancellationToken cancellationToken = default);

    Task ResetAccessFailedCountAsync(
        string userId, CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLockoutEndAsync(
        string userId, CancellationToken cancellationToken = default);

    Task SetLockoutEndAsync(
        string userId, DateTimeOffset lockoutEndUtc, CancellationToken cancellationToken = default);

    Task<AdminTotpSecret> GetOrCreateAuthenticatorSecretAsync(
        string userId, CancellationToken cancellationToken = default);

    Task<bool> VerifyTotpCodeAsync(
        string userId, string code, CancellationToken cancellationToken = default);

    Task EnableTwoFactorAsync(
        string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(
        string userId, int count, CancellationToken cancellationToken = default);

    Task<bool> RedeemRecoveryCodeAsync(
        string userId, string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// ⚠ 新增：QR Code 圖片產生埠，實作用 QRCoder（待 alex 審核的新套件）。
/// </summary>
public interface ITotpQrCodeGenerator
{
    string CreatePngDataUri(string otpAuthUri);
}
