using DoSelect.Application.Security;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Tests;

/// <summary>
/// 涵蓋 alex review P1#4（MFA 完成前重新驗證管理員資格）與 P2#7（Rebind 原子化）的
/// Use Case 層邏輯。Gateway 全部用 Fake 取代，不碰 Identity／資料庫。
/// </summary>
public sealed class AdminTwoFactorUseCaseTests
{
    private static readonly AdminAuthUserSnapshot ActiveAdmin = new(
        UserId: "user-1",
        PublicId: Guid.NewGuid(),
        Email: "admin@example.com",
        DisplayName: "Admin One",
        AccountStatus: AccountStatus.Active,
        PreferredLocale: SupportedLocale.ZhTw,
        EmailConfirmed: true,
        IsAdminProfileActive: true,
        TwoFactorEnabled: true,
        Roles: []);

    private static readonly AdminAuthUserSnapshot SuspendedAdmin = ActiveAdmin with
    {
        AccountStatus = AccountStatus.Suspended,
    };

    private static readonly AdminAuthUserSnapshot RemovedFromAdminProfile = ActiveAdmin with
    {
        IsAdminProfileActive = false,
    };

    [Fact]
    public async Task VerifyTotpAsync_WhenCodeIsCorrectAndAccountIsEligible_ReturnsSuccess()
    {
        var gateway = new FakeAdminAuthGateway { VerifyTotpCode = (_, _) => true, FindById = _ => ActiveAdmin };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.VerifyTotpAsync("user-1", "123456");

        Assert.True(result.IsSuccess);
        Assert.Equal(ActiveAdmin, result.User);
    }

    [Fact]
    public async Task VerifyTotpAsync_WhenCodeIsWrong_ReturnsTwoFactorInvalid()
    {
        var gateway = new FakeAdminAuthGateway { VerifyTotpCode = (_, _) => false };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.VerifyTotpAsync("user-1", "000000");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.TwoFactorInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task VerifyTotpAsync_WhenAccountWasSuspendedAfterPasswordCheck_ReturnsAccountSuspended()
    {
        // 密碼驗證通過、簽發 challenge 之後，帳號在完成 2FA 前被停權——沒有 P1#4 的重新
        // 驗證，TOTP 碼正確就會直接放行，等同用「舊資格」取得新 Session。
        var gateway = new FakeAdminAuthGateway { VerifyTotpCode = (_, _) => true, FindById = _ => SuspendedAdmin };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.VerifyTotpAsync("user-1", "123456");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountSuspended, result.ErrorCode);
    }

    [Fact]
    public async Task VerifyTotpAsync_WhenAdminProfileWasDeactivated_ReturnsAccountSuspended()
    {
        var gateway = new FakeAdminAuthGateway
        {
            VerifyTotpCode = (_, _) => true,
            FindById = _ => RemovedFromAdminProfile,
        };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.VerifyTotpAsync("user-1", "123456");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountSuspended, result.ErrorCode);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_WhenAccountWasSuspendedAfterPasswordCheck_ReturnsAccountSuspended()
    {
        var gateway = new FakeAdminAuthGateway
        {
            RedeemRecoveryCode = (_, _) => true,
            FindById = _ => SuspendedAdmin,
        };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.RedeemRecoveryCodeAsync("user-1", "aaaaaaaa");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountSuspended, result.ErrorCode);
    }

    [Fact]
    public async Task ConfirmEnrollmentAsync_WhenAccountWasSuspendedAfterPasswordCheck_ReturnsAccountSuspendedWithoutEnablingTwoFactor()
    {
        // alex review 第二輪 P1#4 核心回歸測試：資格檢查必須在啟用 2FA／產生 Recovery
        // Codes 之前。修正前的順序會先啟用、再檢查，讓停權帳號留下「2FA 已啟用但
        // 使用者拿不到新 Recovery Codes」的半成功狀態。這裡刻意讓 EnableTwoFactor／
        // GenerateRecoveryCodes 拋例外，只要它們被呼叫測試就會失敗，藉此鎖住呼叫順序。
        var gateway = new FakeAdminAuthGateway
        {
            VerifyTotpCode = (_, _) => true,
            EnableTwoFactor = _ => throw new InvalidOperationException(
                "2FA must not be enabled before the eligibility check."),
            GenerateRecoveryCodes = (_, _) => throw new InvalidOperationException(
                "Recovery codes must not be generated before the eligibility check."),
            FindById = _ => SuspendedAdmin,
        };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.ConfirmEnrollmentAsync("user-1", "123456");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountSuspended, result.ErrorCode);
    }

    [Fact]
    public async Task ConfirmRebindAsync_WhenPromotionAndVerificationSucceed_ReturnsSuccessWithNewRecoveryCodes()
    {
        var gateway = new FakeAdminAuthGateway
        {
            PromotePendingSecretAndVerify = (_, _) => true,
            GenerateRecoveryCodes = (_, _) => ["new-code-1", "new-code-2"],
            FindById = _ => ActiveAdmin,
        };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.ConfirmRebindAsync("user-1", "123456");

        Assert.True(result.IsSuccess);
        Assert.Equal(["new-code-1", "new-code-2"], result.RecoveryCodes);
    }

    [Fact]
    public async Task ConfirmRebindAsync_WhenTheCodeDoesNotMatchThePendingSecret_ReturnsTwoFactorInvalidWithoutGeneratingRecoveryCodes()
    {
        // P2#7 核心行為：驗證失敗時不得產生新 Recovery Code、不得回報成功——呼叫端
        // （Controller）靠這個失敗結果去 rollback 交易，讓「秘鑰提升」也一併復原。
        var recoveryCodesGenerated = false;
        var gateway = new FakeAdminAuthGateway
        {
            PromotePendingSecretAndVerify = (_, _) => false,
            GenerateRecoveryCodes = (_, _) =>
            {
                recoveryCodesGenerated = true;
                return ["should-not-be-called"];
            },
        };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.ConfirmRebindAsync("user-1", "000000");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.TwoFactorInvalid, result.ErrorCode);
        Assert.False(recoveryCodesGenerated);
    }

    [Fact]
    public async Task ConfirmRebindAsync_WhenAccountWasSuspendedAfterBegin_ReturnsAccountSuspended()
    {
        var gateway = new FakeAdminAuthGateway
        {
            PromotePendingSecretAndVerify = (_, _) => true,
            GenerateRecoveryCodes = (_, _) => ["new-code-1"],
            FindById = _ => SuspendedAdmin,
        };
        var useCase = new AdminTwoFactorUseCase(gateway, new FakeQrCodeGenerator());

        var result = await useCase.ConfirmRebindAsync("user-1", "123456");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountSuspended, result.ErrorCode);
    }

    private sealed class FakeQrCodeGenerator : ITotpQrCodeGenerator
    {
        public string CreatePngDataUri(string otpAuthUri) => "data:image/png;base64,fake";
    }

    private sealed class FakeAdminAuthGateway : IAdminAuthGateway
    {
        public Func<string, AdminAuthUserSnapshot?>? FindById { get; init; }

        public Func<string, string, bool>? VerifyTotpCode { get; init; }

        public Func<string, string, bool>? RedeemRecoveryCode { get; init; }

        public Action<string>? EnableTwoFactor { get; init; }

        public Func<string, int, IReadOnlyList<string>>? GenerateRecoveryCodes { get; init; }

        public Func<string, bool>? BeginRebindSecret { get; init; }

        public Func<string, string, bool>? PromotePendingSecretAndVerify { get; init; }

        public Task<AdminAuthUserSnapshot?> FindAdminByEmailAsync(
            string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminAuthUserSnapshot?> FindAdminByIdAsync(
            string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FindById is null ? null : FindById(userId));

        public Task<bool> CheckPasswordAsync(
            string userId, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> GetAccessFailedCountAsync(
            string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task IncrementAccessFailedCountAsync(
            string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResetAccessFailedCountAsync(
            string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DateTimeOffset?> GetLockoutEndAsync(
            string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetLockoutEndAsync(
            string userId, DateTimeOffset lockoutEndUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminTotpSecret> GetOrCreateAuthenticatorSecretAsync(
            string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminTotpSecret("SECRETKEY", "otpauth://totp/fake"));

        public Task<AdminTotpSecret> BeginRebindSecretAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            BeginRebindSecret?.Invoke(userId);
            return Task.FromResult(new AdminTotpSecret("PENDINGKEY", "otpauth://totp/fake-pending"));
        }

        public Task<bool> PromotePendingSecretAndVerifyAsync(
            string userId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(PromotePendingSecretAndVerify is null ? false : PromotePendingSecretAndVerify(userId, code));

        public Task<bool> VerifyTotpCodeAsync(
            string userId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(VerifyTotpCode is null ? false : VerifyTotpCode(userId, code));

        public Task EnableTwoFactorAsync(string userId, CancellationToken cancellationToken = default)
        {
            EnableTwoFactor?.Invoke(userId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(
            string userId, int count, CancellationToken cancellationToken = default) =>
            Task.FromResult(GenerateRecoveryCodes is null ? [] : GenerateRecoveryCodes(userId, count));

        public Task<bool> RedeemRecoveryCodeAsync(
            string userId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(RedeemRecoveryCode is null ? false : RedeemRecoveryCode(userId, code));
    }
}
