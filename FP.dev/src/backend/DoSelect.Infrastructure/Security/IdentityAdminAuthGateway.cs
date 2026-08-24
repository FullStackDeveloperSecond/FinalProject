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
        await _userManager.AccessFailedAsync(user);
    }

    public async Task ResetAccessFailedCountAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        await _userManager.ResetAccessFailedCountAsync(user);
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
        await _userManager.SetLockoutEndDateAsync(user, lockoutEndUtc);
    }

    public async Task<AdminTotpSecret> GetOrCreateAuthenticatorSecretAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);

        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        var otpAuthUri = BuildOtpAuthUri(user.Email ?? user.UserName ?? userId, key!);
        return new AdminTotpSecret(key!, otpAuthUri);
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
        await _userManager.SetTwoFactorEnabledAsync(user, true);
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
