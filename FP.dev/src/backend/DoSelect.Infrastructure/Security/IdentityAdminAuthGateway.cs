using DoSelect.Application.Security;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Security;

/// <summary>
/// <see cref="IAdminAuthGateway"/> 的實作，一律透過 <see cref="UserManager{TUser}"/> 存取
/// ASP.NET Core Identity 既有的 Store（AspNetUsers／AspNetUserTokens），不建立新資料表。
/// TOTP 秘鑰、Recovery Code 都存在既有 AspNetUserTokens。
/// </summary>
public sealed class IdentityAdminAuthGateway : IAdminAuthGateway
{
    private const string TotpIssuer = "DoSelect";

    // 自訂 token slot，只存放 Rebind 尚未確認的待生效秘鑰，不影響正式 authenticator key。
    private const string PendingSecretLoginProvider = "DoSelectAdminRebind";
    private const string PendingSecretTokenName = "PendingAuthenticatorKey";

    // ⚠ 複製 ASP.NET Core Identity UserManager 內部存放「正式 authenticator key」的
    // LoginProvider／TokenName（UserManager<TUser> 原始碼的 private const
    // InternalLoginProvider = "[AspNetUserStore]"、AuthenticatorKeyTokenName =
    // "AuthenticatorKey"，自 .NET Core 2.x 起穩定，但不是公開 API 契約）。這是唯一能把
    // 「特定」秘鑰值設成正式 key 的方式——UserManager 沒有公開的
    // SetAuthenticatorKey(value) 方法，只有會產生亂數新值的 ResetAuthenticatorKeyAsync。
    // 若此慣例未來變動，AdminAuthControllerTests 的 Rebind 回滾測試會立即失敗。
    private const string IdentityAuthenticatorLoginProvider = "[AspNetUserStore]";
    private const string IdentityAuthenticatorKeyTokenName = "AuthenticatorKey";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DoSelectDbContext _dbContext;

    public IdentityAdminAuthGateway(UserManager<ApplicationUser> userManager, DoSelectDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(dbContext);

        _userManager = userManager;
        _dbContext = dbContext;
    }

    public async Task<AdminAuthUserSnapshot?> FindAdminByEmailAsync(
        string email, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(email);
        return user is null || user.AccountType != Domain.Members.AccountType.Admin
            ? null
            : await BuildSnapshotAsync(user, cancellationToken);
    }

    public async Task<AdminAuthUserSnapshot?> FindAdminByIdAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user is null || user.AccountType != Domain.Members.AccountType.Admin
            ? null
            : await BuildSnapshotAsync(user, cancellationToken);
    }

    public async Task<bool> CheckPasswordAsync(
        string userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user is not null && await _userManager.CheckPasswordAsync(user, password);
    }

    public async Task<int> GetAccessFailedCountAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        return await _userManager.GetAccessFailedCountAsync(user);
    }

    public async Task IncrementAccessFailedCountAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        ThrowIfFailed(await _userManager.AccessFailedAsync(user), nameof(IncrementAccessFailedCountAsync), userId);
    }

    public async Task ResetAccessFailedCountAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        ThrowIfFailed(
            await _userManager.ResetAccessFailedCountAsync(user), nameof(ResetAccessFailedCountAsync), userId);
    }

    public async Task<DateTimeOffset?> GetLockoutEndAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        return await _userManager.GetLockoutEndDateAsync(user);
    }

    public async Task SetLockoutEndAsync(
        string userId, DateTimeOffset lockoutEndUtc, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        ThrowIfFailed(
            await _userManager.SetLockoutEndDateAsync(user, lockoutEndUtc), nameof(SetLockoutEndAsync), userId);
    }

    public async Task<AdminTotpSecret> GetOrCreateAuthenticatorSecretAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);

        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            // ResetAuthenticatorKeyAsync returns plain Task (Identity exposes no IdentityResult
            // here) — a failed persist would leave the key empty, so read it back and fail loudly
            // instead of silently continuing with a null/empty secret.
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException(
                    $"Failed to persist a new authenticator key for admin user '{userId}'.");
            }
        }

        var otpAuthUri = BuildOtpAuthUri(user.Email ?? user.UserName ?? userId, key);
        return new AdminTotpSecret(key, otpAuthUri);
    }

    public async Task<AdminTotpSecret> BeginRebindSecretAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);

        var key = _userManager.GenerateNewAuthenticatorKey();
        var setResult = await _userManager.SetAuthenticationTokenAsync(
            user, PendingSecretLoginProvider, PendingSecretTokenName, key);
        ThrowIfFailed(setResult, nameof(BeginRebindSecretAsync), userId);

        var otpAuthUri = BuildOtpAuthUri(user.Email ?? user.UserName ?? userId, key);
        return new AdminTotpSecret(key, otpAuthUri);
    }

    public async Task<bool> PromotePendingSecretAndVerifyAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);

        var pendingKey = await _userManager.GetAuthenticationTokenAsync(
            user, PendingSecretLoginProvider, PendingSecretTokenName);
        if (string.IsNullOrEmpty(pendingKey))
        {
            return false;
        }

        var promoteResult = await _userManager.SetAuthenticationTokenAsync(
            user, IdentityAuthenticatorLoginProvider, IdentityAuthenticatorKeyTokenName, pendingKey);
        if (!promoteResult.Succeeded)
        {
            return false;
        }

        if (!await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            // 驗證失敗：呼叫端會在同一交易中 rollback，讓上面的 promote 也一併復原，
            // 正式 authenticator key 維持 rollback 前（也就是舊裝置）的值。
            return false;
        }

        var cleanupResult = await _userManager.RemoveAuthenticationTokenAsync(
            user, PendingSecretLoginProvider, PendingSecretTokenName);
        ThrowIfFailed(cleanupResult, nameof(PromotePendingSecretAndVerifyAsync), userId);

        return true;
    }

    public async Task<bool> VerifyTotpCodeAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        return await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code);
    }

    public async Task EnableTwoFactorAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        ThrowIfFailed(await _userManager.SetTwoFactorEnabledAsync(user, true), nameof(EnableTwoFactorAsync), userId);
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(
        string userId, int count, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, count);
        return codes?.ToArray() ?? [];
    }

    public async Task<bool> RedeemRecoveryCodeAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        var result = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
        return result.Succeeded;
    }

    private async Task<ApplicationUser> RequireUserAsync(string userId) =>
        await _userManager.FindByIdAsync(userId)
        ?? throw new InvalidOperationException($"Admin user '{userId}' was not found.");

    /// <summary>
    /// 目前除了 RedeemTwoFactorRecoveryCodeAsync 以外，所有 IdentityResult 都直接被忽略——
    /// 一旦 Identity Store 內部失敗（例如 Concurrency），程式碼仍會當作成功繼續。統一在這裡
    /// 檢查並拋出，讓呼叫端（Controller 交易邊界）能感知並 rollback，而不是回傳假的成功。
    /// </summary>
    private static void ThrowIfFailed(IdentityResult result, string operation, string userId)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
            throw new InvalidOperationException(
                $"Identity operation '{operation}' failed for admin user '{userId}': {errors}");
        }
    }

    private async Task<AdminAuthUserSnapshot> BuildSnapshotAsync(
        ApplicationUser user, CancellationToken cancellationToken)
    {
        var adminProfile = await _dbContext.AdminProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.UserId == user.Id, cancellationToken);
        var roles = await _userManager.GetRolesAsync(user);

        return new AdminAuthUserSnapshot(
            user.Id,
            user.PublicId,
            user.Email ?? string.Empty,
            adminProfile?.DisplayName ?? user.Email ?? string.Empty,
            user.AccountStatus,
            user.PreferredLocale,
            user.EmailConfirmed,
            adminProfile?.IsActive ?? false,
            user.TwoFactorEnabled,
            roles.ToArray());
    }

    private static string BuildOtpAuthUri(string email, string secretKey) =>
        $"otpauth://totp/{Uri.EscapeDataString(TotpIssuer)}:{Uri.EscapeDataString(email)}" +
        $"?secret={secretKey}&issuer={Uri.EscapeDataString(TotpIssuer)}&digits=6&algorithm=SHA1&period=30";
}
