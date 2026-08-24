using DoSelect.Application.Common;
using DoSelect.Application.Members;
using DoSelect.Application.Notifications;
using DoSelect.Domain.Members;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Tests;

public sealed class RegisterMemberServiceTests
{
    [Fact]
    public async Task RegisterAsync_WhenSubmissionIsValid_CreatesMemberAndSendsMaskedVerificationEmail()
    {
        var gateway = new FakeMemberRegistrationGateway(
            _ => new CreateMemberOutcome.Success(
                Guid.NewGuid(),
                "jane.doe@example.com",
                AccountStatus.PendingEmailVerification,
                "confirmation-token"));
        var emailSender = new RecordingEmailDispatchQueue();
        var service = CreateService(gateway, emailSender);

        var result = await service.RegisterAsync(ValidCommand());

        var success = Assert.IsType<RegisterMemberResult.Success>(result);
        Assert.Equal("j*******@example.com", success.EmailMasked);
        Assert.Equal(AccountStatus.PendingEmailVerification, success.AccountStatus);
        Assert.Single(emailSender.SentMessages);
        Assert.Contains("verify-email", emailSender.SentMessages[0].TextBody);
        Assert.Contains(success.PublicId.ToString("D"), emailSender.SentMessages[0].TextBody);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailIsAlreadyRegistered_ReturnsTheSameSuccessShapeAsAFreshRegistrationWithoutSendingEmail()
    {
        // Non-enumerable by design: an unauthenticated caller must not be able to tell an
        // already-registered email apart from a brand-new one (Alex review, 2026-08-21).
        var gateway = new FakeMemberRegistrationGateway(_ => new CreateMemberOutcome.EmailInUse());
        var emailSender = new RecordingEmailDispatchQueue();
        var service = CreateService(gateway, emailSender);

        var result = await service.RegisterAsync(ValidCommand());

        var success = Assert.IsType<RegisterMemberResult.Success>(result);
        Assert.Equal("j*******@example.com", success.EmailMasked);
        Assert.Equal(AccountStatus.PendingEmailVerification, success.AccountStatus);
        Assert.NotEqual(Guid.Empty, success.PublicId);
        Assert.Empty(emailSender.SentMessages);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailIsAlreadyRegistered_ReturnsAPublicIdWithTheSameUuidVersionAsARealOne()
    {
        // A v4 GUID for the synthetic duplicate-email PublicId was itself an oracle: the version
        // nibble told an attacker new-vs-duplicate apart even though every other part of the
        // response was identical (Alex review, 2026-08-24). Real accounts get their PublicId from
        // Guid.CreateVersion7() (see CreateMemberAsync), so the synthetic one must match.
        var gateway = new FakeMemberRegistrationGateway(_ => new CreateMemberOutcome.EmailInUse());
        var service = CreateService(gateway, new RecordingEmailDispatchQueue());

        var result = await service.RegisterAsync(ValidCommand());

        var success = Assert.IsType<RegisterMemberResult.Success>(result);
        Assert.Equal(7, UuidVersion(success.PublicId));
    }

    private static int UuidVersion(Guid guid) => Convert.ToInt32(guid.ToString("N")[12].ToString(), 16);

    [Fact]
    public async Task RegisterAsync_WhenTermsVersionIsNotCurrent_ReturnsValidationFailedWithoutCallingGateway()
    {
        var gateway = new FakeMemberRegistrationGateway(
            _ => throw new InvalidOperationException("Gateway should not be called."));
        var service = CreateService(gateway, new RecordingEmailDispatchQueue());
        var command = ValidCommand() with { AcceptTermsVersion = 999 };

        var result = await service.RegisterAsync(command);

        var validationFailed = Assert.IsType<RegisterMemberResult.ValidationFailed>(result);
        Assert.True(validationFailed.Errors.ContainsKey("acceptTermsVersion"));
    }

    [Fact]
    public async Task RegisterAsync_WhenLocaleIsUnsupported_ReturnsValidationFailed()
    {
        var gateway = new FakeMemberRegistrationGateway(
            _ => throw new InvalidOperationException("Gateway should not be called."));
        var service = CreateService(gateway, new RecordingEmailDispatchQueue());
        var command = ValidCommand() with { Locale = "fr-FR" };

        var result = await service.RegisterAsync(command);

        var validationFailed = Assert.IsType<RegisterMemberResult.ValidationFailed>(result);
        Assert.True(validationFailed.Errors.ContainsKey("locale"));
    }

    [Fact]
    public async Task RegisterAsync_WhenPasswordIsRejectedByIdentity_ReturnsValidationFailedWithReasons()
    {
        var gateway = new FakeMemberRegistrationGateway(
            _ => new CreateMemberOutcome.PasswordRejected(["Password too weak."]));
        var service = CreateService(gateway, new RecordingEmailDispatchQueue());

        var result = await service.RegisterAsync(ValidCommand());

        var validationFailed = Assert.IsType<RegisterMemberResult.ValidationFailed>(result);
        Assert.Equal(["Password too weak."], validationFailed.Errors["password"]);
    }

    private static RegisterMemberCommand ValidCommand() => new(
        "jane.doe@example.com",
        "correct-horse-battery-staple",
        "Jane Doe",
        null,
        RegisterMemberService.CurrentTermsVersion);

    private static RegisterMemberService CreateService(
        IMemberRegistrationGateway gateway,
        IEmailDispatchQueue emailDispatchQueue) =>
        new(gateway, emailDispatchQueue, new EmailRequestThrottle(Options.Create(new RateLimitOptions())), Options.Create(new FrontendLinkOptions
        {
            BaseUrl = "http://localhost:5173",
        }));

    private sealed class FakeMemberRegistrationGateway(
        Func<CreateMemberRequest, CreateMemberOutcome> createMember) : IMemberRegistrationGateway
    {
        public Task<CreateMemberOutcome> CreateMemberAsync(
            CreateMemberRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(createMember(request));

        public Task<ConfirmMemberEmailOutcome> ConfirmEmailAsync(
            Guid userPublicId,
            string token,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RequestMemberEmailVerificationOutcome> RequestEmailVerificationAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingEmailDispatchQueue : IEmailDispatchQueue
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public void Enqueue(EmailMessage message) => SentMessages.Add(message);
    }
}
