using DoSelect.Application.Members;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Tests;

public sealed class ConfirmEmailVerificationServiceTests
{
    [Fact]
    public async Task ConfirmAsync_WhenTokenIsValid_ReturnsSuccessWithActiveStatus()
    {
        var gateway = new FakeMemberRegistrationGateway(
            (_, _) => new ConfirmMemberEmailOutcome.Success(AccountStatus.Active));
        var service = new ConfirmEmailVerificationService(gateway);

        var result = await service.ConfirmAsync(new ConfirmEmailVerificationCommand(Guid.NewGuid(), "token"));

        var success = Assert.IsType<ConfirmEmailVerificationResult.Success>(result);
        Assert.Equal(AccountStatus.Active, success.AccountStatus);
    }

    [Fact]
    public async Task ConfirmAsync_WhenTokenIsRejected_ReturnsTokenInvalid()
    {
        var gateway = new FakeMemberRegistrationGateway(
            (_, _) => new ConfirmMemberEmailOutcome.TokenRejected());
        var service = new ConfirmEmailVerificationService(gateway);

        var result = await service.ConfirmAsync(new ConfirmEmailVerificationCommand(Guid.NewGuid(), "bad-token"));

        Assert.IsType<ConfirmEmailVerificationResult.TokenInvalid>(result);
    }

    private sealed class FakeMemberRegistrationGateway(
        Func<Guid, string, ConfirmMemberEmailOutcome> confirmEmail) : IMemberRegistrationGateway
    {
        public Task<CreateMemberOutcome> CreateMemberAsync(
            CreateMemberRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConfirmMemberEmailOutcome> ConfirmEmailAsync(
            Guid userPublicId,
            string token,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(confirmEmail(userPublicId, token));

        public Task<RequestMemberEmailVerificationOutcome> RequestEmailVerificationAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
