using DoSelect.Application.Security;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Tests;

public sealed class AdminLoginUseCaseTests
{
    private static readonly AdminAuthUserSnapshot ActiveAdminWithTwoFactor = new(
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

    private static readonly AdminAuthUserSnapshot ActiveAdminWithoutTwoFactor =
        ActiveAdminWithTwoFactor with { TwoFactorEnabled = false };

    private static readonly AdminAuthUserSnapshot SuspendedAdmin =
        ActiveAdminWithTwoFactor with { AccountStatus = AccountStatus.Suspended };

    [Fact]
    public async Task ExecuteAsync_WhenPasswordIsCorrectAndTwoFactorIsEnabled_ReturnsNeedsTwoFactor()
    {
        var gateway = new FakeAdminAuthGateway
        {
            FindByEmail = _ => ActiveAdminWithTwoFactor,
            CheckPassword = (_, _) => true,
        };
        var useCase = new AdminLoginUseCase(gateway, TimeProvider.System);

        var result = await useCase.ExecuteAsync("admin@example.com", "correct-password");

        Assert.True(result.IsSuccess);
        Assert.True(result.RequiresTwoFactor);
        Assert.False(result.RequiresEnrollment);
        Assert.True(gateway.AccessFailedCountWasReset);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPasswordIsCorrectAndTwoFactorIsNotEnabled_ReturnsNeedsEnrollment()
    {
        var gateway = new FakeAdminAuthGateway
        {
            FindByEmail = _ => ActiveAdminWithoutTwoFactor,
            CheckPassword = (_, _) => true,
        };
        var useCase = new AdminLoginUseCase(gateway, TimeProvider.System);

        var result = await useCase.ExecuteAsync("admin@example.com", "correct-password");

        Assert.True(result.IsSuccess);
        Assert.True(result.RequiresEnrollment);
        Assert.False(result.RequiresTwoFactor);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmailIsUnknown_ReturnsInvalidCredentials()
    {
        var gateway = new FakeAdminAuthGateway { FindByEmail = _ => null };
        var useCase = new AdminLoginUseCase(gateway, TimeProvider.System);

        var result = await useCase.ExecuteAsync("nobody@example.com", "anything");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPasswordIsWrong_IncrementsFailedCountAndReturnsInvalidCredentials()
    {
        var gateway = new FakeAdminAuthGateway
        {
            FindByEmail = _ => ActiveAdminWithTwoFactor,
            CheckPassword = (_, _) => false,
            AccessFailedCount = 1,
        };
        var useCase = new AdminLoginUseCase(gateway, TimeProvider.System);

        var result = await useCase.ExecuteAsync("admin@example.com", "wrong-password");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.InvalidCredentials, result.ErrorCode);
        Assert.Equal(1, gateway.IncrementCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheFifthWrongPasswordIsSubmitted_LocksTheAccountFor30Minutes()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FixedTimeProvider(now);
        var gateway = new FakeAdminAuthGateway
        {
            FindByEmail = _ => ActiveAdminWithTwoFactor,
            CheckPassword = (_, _) => false,
            AccessFailedCount = 5,
        };
        var useCase = new AdminLoginUseCase(gateway, timeProvider);

        var result = await useCase.ExecuteAsync("admin@example.com", "wrong-password");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountLocked, result.ErrorCode);
        Assert.Equal(now.AddMinutes(30), gateway.LockoutEndSet);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnExistingLockoutIsStillActive_ReturnsAccountLockedWithoutCheckingThePassword()
    {
        var now = DateTimeOffset.UtcNow;
        var gateway = new FakeAdminAuthGateway
        {
            FindByEmail = _ => ActiveAdminWithTwoFactor,
            LockoutEnd = now.AddMinutes(10),
            CheckPassword = (_, _) => throw new InvalidOperationException("Password must not be checked while locked out."),
        };
        var useCase = new AdminLoginUseCase(gateway, new FixedTimeProvider(now));

        var result = await useCase.ExecuteAsync("admin@example.com", "correct-password");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountLocked, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAccountIsSuspended_ReturnsAccountSuspendedWithoutCheckingThePassword()
    {
        var gateway = new FakeAdminAuthGateway
        {
            FindByEmail = _ => SuspendedAdmin,
            CheckPassword = (_, _) => throw new InvalidOperationException("Password must not be checked while suspended."),
        };
        var useCase = new AdminLoginUseCase(gateway, TimeProvider.System);

        var result = await useCase.ExecuteAsync("admin@example.com", "correct-password");

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminAuthErrorCodes.AccountSuspended, result.ErrorCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAdminAuthGateway : IAdminAuthGateway
    {
        public Func<string, AdminAuthUserSnapshot?>? FindByEmail { get; init; }

        public Func<string, string, bool>? CheckPassword { get; init; }

        public int AccessFailedCount { get; set; }

        public DateTimeOffset? LockoutEnd { get; set; }

        public int IncrementCallCount { get; private set; }

        public bool AccessFailedCountWasReset { get; private set; }

        public DateTimeOffset? LockoutEndSet { get; private set; }

        public Task<AdminAuthUserSnapshot?> FindAdminByEmailAsync(
            string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(FindByEmail is null ? null : FindByEmail(email));

        public Task<AdminAuthUserSnapshot?> FindAdminByIdAsync(
            string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CheckPasswordAsync(
            string userId, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(CheckPassword is null ? false : CheckPassword(userId, password));

        public Task<int> GetAccessFailedCountAsync(
            string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AccessFailedCount);

        public Task IncrementAccessFailedCountAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            IncrementCallCount++;
            return Task.CompletedTask;
        }

        public Task ResetAccessFailedCountAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            AccessFailedCountWasReset = true;
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetLockoutEndAsync(
            string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(LockoutEnd);

        public Task SetLockoutEndAsync(
            string userId, DateTimeOffset lockoutEndUtc, CancellationToken cancellationToken = default)
        {
            LockoutEndSet = lockoutEndUtc;
            return Task.CompletedTask;
        }

        public Task<AdminTotpSecret> GetOrCreateAuthenticatorSecretAsync(
            string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminTotpSecret> BeginRebindSecretAsync(
            string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> PromotePendingSecretAndVerifyAsync(
            string userId, string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> VerifyTotpCodeAsync(
            string userId, string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task EnableTwoFactorAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(
            string userId, int count, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RedeemRecoveryCodeAsync(
            string userId, string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
