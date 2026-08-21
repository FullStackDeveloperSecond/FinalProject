using DoSelect.Application.Members;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Tests;

public sealed class LoginMemberServiceTests
{
    [Fact]
    public async Task LoginAsync_WhenCredentialsAreValid_ReturnsSuccessWithMaskedEmail()
    {
        var gateway = new FakeMemberLoginGateway(
            (_, _) => new MemberLoginOutcome.Success(
                Guid.NewGuid(),
                "user-1",
                "Jane Doe",
                "jane.doe@example.com",
                AccountStatus.Active,
                SupportedLocale.ZhTw,
                "stamp-1"));
        var service = new LoginMemberService(gateway);

        var result = await service.LoginAsync(new LoginMemberCommand("jane.doe@example.com", "password", true));

        var success = Assert.IsType<LoginMemberResult.Success>(result);
        Assert.Equal("j*******@example.com", success.EmailMasked);
        Assert.True(success.RememberMe);
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordIsWrong_ReturnsInvalidCredentials()
    {
        var gateway = new FakeMemberLoginGateway((_, _) => new MemberLoginOutcome.InvalidCredentials());
        var service = new LoginMemberService(gateway);

        var result = await service.LoginAsync(new LoginMemberCommand("jane.doe@example.com", "wrong", false));

        Assert.IsType<LoginMemberResult.InvalidCredentials>(result);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsLockedOut_ReturnsLockedOutWithEndTime()
    {
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        var gateway = new FakeMemberLoginGateway((_, _) => new MemberLoginOutcome.LockedOut(lockoutEnd));
        var service = new LoginMemberService(gateway);

        var result = await service.LoginAsync(new LoginMemberCommand("jane.doe@example.com", "password", false));

        var lockedOut = Assert.IsType<LoginMemberResult.LockedOut>(result);
        Assert.Equal(lockoutEnd, lockedOut.LockoutEndUtc);
    }

    [Fact]
    public async Task LoginAsync_WhenEmailIsUnverified_ReturnsEmailUnverified()
    {
        var gateway = new FakeMemberLoginGateway((_, _) => new MemberLoginOutcome.EmailUnverified());
        var service = new LoginMemberService(gateway);

        var result = await service.LoginAsync(new LoginMemberCommand("jane.doe@example.com", "password", false));

        Assert.IsType<LoginMemberResult.EmailUnverified>(result);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsSuspended_ReturnsSuspended()
    {
        var gateway = new FakeMemberLoginGateway((_, _) => new MemberLoginOutcome.Suspended());
        var service = new LoginMemberService(gateway);

        var result = await service.LoginAsync(new LoginMemberCommand("jane.doe@example.com", "password", false));

        Assert.IsType<LoginMemberResult.Suspended>(result);
    }

    [Fact]
    public async Task GetSessionAsync_WhenUserIdIsNull_ReturnsAnonymous()
    {
        var gateway = new FakeMemberLoginGateway(
            (_, _) => throw new NotSupportedException(),
            _ => throw new NotSupportedException());
        var service = new LoginMemberService(gateway);

        var result = await service.GetSessionAsync(null);

        Assert.IsType<MemberSessionResult.Anonymous>(result);
    }

    [Fact]
    public async Task GetSessionAsync_WhenMemberIsActive_ReturnsAuthenticatedWithMaskedEmail()
    {
        var publicId = Guid.NewGuid();
        var gateway = new FakeMemberLoginGateway(
            (_, _) => throw new NotSupportedException(),
            _ => new MemberSessionSnapshot(publicId, "Jane Doe", "jane.doe@example.com", true, SupportedLocale.ZhTw));
        var service = new LoginMemberService(gateway);

        var result = await service.GetSessionAsync("user-1");

        var authenticated = Assert.IsType<MemberSessionResult.Authenticated>(result);
        Assert.Equal("j*******@example.com", authenticated.EmailMasked);
    }

    [Fact]
    public async Task GetSessionAsync_WhenMemberIsNotFound_ReturnsAnonymous()
    {
        var gateway = new FakeMemberLoginGateway(
            (_, _) => throw new NotSupportedException(),
            _ => null);
        var service = new LoginMemberService(gateway);

        var result = await service.GetSessionAsync("user-1");

        Assert.IsType<MemberSessionResult.Anonymous>(result);
    }

    private sealed class FakeMemberLoginGateway(
        Func<string, string, MemberLoginOutcome> validateCredentials,
        Func<string, MemberSessionSnapshot?>? findByUserId = null) : IMemberLoginGateway
    {
        public Task<MemberLoginOutcome> ValidateCredentialsAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(validateCredentials(email, password));

        public Task<MemberSessionSnapshot?> FindActiveMemberByUserIdAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(findByUserId is null ? null : findByUserId(userId));
    }
}
