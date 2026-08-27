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

    // 跟 MemberLoginGateway 同一套手法：密碼雜湊驗證本身就是刻意昂貴的計算，任何略過它的
    // 路徑（帳號不存在／已鎖定）耗時都會明顯較短。對一個固定假使用者的雜湊跑一次「不可能
    // 成功」的驗證，把耗時拉平，回應延遲就不再是帳號是否存在／是否鎖定的旁路訊號。
    private static readonly ApplicationUser DummyUser =
        ApplicationUser.CreateMember(Guid.CreateVersion7(), "dummy-timing-guard@example.invalid", DateTime.UtcNow);

    private static readonly string DummyPasswordHash =
        new PasswordHasher<ApplicationUser>().HashPassword(DummyUser, Guid.NewGuid().ToString("N"));

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DoSelectDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public IdentityAdminAuthGateway(
        UserManager<ApplicationUser> userManager, DoSelectDbContext dbContext, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _userManager = userManager;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
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

    public Task PerformDummyPasswordVerificationAsync(
        string password, CancellationToken cancellationToken = default)
    {
        _userManager.PasswordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, password);
        return Task.CompletedTask;
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

    public async Task<DateTimeOffset?> RegisterFailedAttemptAsync(
        string userId,
        TimeSpan lockoutDurationOnThreshold,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);

        // ⚠ DEC-P269：Identity 的 AccessFailedAsync 一旦達到 Options.Lockout.MaxFailedAccessAttempts
        // 門檻（全域設定，Member／Admin 共用同一個 5 次門檻——只有「鎖多久」需要依 AccountType
        // 分開，見 PersistenceServiceCollectionExtensions），會在同一次呼叫內「順便」用全域
        // DefaultLockoutTimeSpan（15 分鐘）鎖定帳號、並把 AccessFailedCount 重設回 0——這兩者都是
        // Identity 內建、無法關掉的副作用。
        //
        // ⚠ 這個重設正是原本 bug 的根源：如果改用「呼叫後讀 AccessFailedCount 判斷是否達門檻」，
        // 命中門檻的那一次呼叫讀到的其實是重設後的 0，永遠不會被判定為「剛好鎖定」，30 分鐘的
        // 覆蓋動作永遠不會執行（實測用真正的 UserManager／SQL Server 才發現，fake gateway 測不出
        // 來）。改成呼叫後用 IsLockedOutAsync 直接問 Identity「現在是不是被鎖住了」，不管是這次
        // 呼叫剛觸發的、還是之前就已經鎖著的，一律覆蓋成 AccountType 對應的正確時長。
        //
        // ⚠ alex review：這裡不再自己開/關交易——兩次寫入（Identity 自己的 15 分鐘、這裡覆蓋的
        // 正確時長）與呼叫端（AdminAuthController.Login）之後補寫的中央 Audit 必須同一個交易，
        // Audit 失敗要讓鎖定也一併 rollback。交易邊界改由呼叫端持有，跟 recovery-codes/use、
        // totp/enroll/confirm、totp/rebind/confirm 一致；本方法只負責寫入，不 Commit／Rollback。
        ThrowIfFailed(await _userManager.AccessFailedAsync(user), nameof(RegisterFailedAttemptAsync), userId);
        if (!await _userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        var lockoutEnd = _timeProvider.GetUtcNow().Add(lockoutDurationOnThreshold);
        ThrowIfFailed(
            await _userManager.SetLockoutEndDateAsync(user, lockoutEnd),
            nameof(RegisterFailedAttemptAsync),
            userId);
        return lockoutEnd;
    }

    public async Task<AdminTotpSecret> GetOrCreateAuthenticatorSecretAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);

        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            // ⚠ alex review P2#6：ResetAuthenticatorKeyAsync 其實回傳 IdentityResult（先前註解
            // 誤判成 plain Task），原本被直接丟棄——一旦 Identity Store 內部寫入失敗（例如
            // Concurrency），程式碼仍會當作成功繼續往下走。這裡改成跟其他呼叫一致，透過
            // ThrowIfFailed 檢查；下面的讀回檢查繼續保留，作為第二道防線。
            ThrowIfFailed(
                await _userManager.ResetAuthenticatorKeyAsync(user),
                nameof(GetOrCreateAuthenticatorSecretAsync),
                userId);
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

        // ⚠ alex review P2#6：null 回傳（Identity token provider 內部失敗）先前被直接轉成空
        // 陣列，呼叫端仍會回報成功——管理員以為拿到新 Recovery Code，實際上一組都沒有、
        // 永久失去救援管道。改成明確失敗，讓呼叫端的交易一併 rollback。
        var codeArray = codes?.ToArray() ?? [];
        if (codeArray.Length != count)
        {
            throw new InvalidOperationException(
                $"Failed to generate {count} recovery codes for admin user '{userId}' " +
                $"(received {codeArray.Length}).");
        }

        return codeArray;
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
