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
        var emailSender = new RecordingEmailSender();
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
    public async Task RegisterAsync_WhenEmailIsAlreadyRegistered_ReturnsEmailInUseWithoutSendingEmail()
    {
        var gateway = new FakeMemberRegistrationGateway(_ => new CreateMemberOutcome.EmailInUse());
        var emailSender = new RecordingEmailSender();
        var service = CreateService(gateway, emailSender);

        var result = await service.RegisterAsync(ValidCommand());

        Assert.IsType<RegisterMemberResult.EmailInUse>(result);
        Assert.Empty(emailSender.SentMessages);
    }

    [Fact]
    public async Task RegisterAsync_WhenTermsVersionIsNotCurrent_ReturnsValidationFailedWithoutCallingGateway()
    {
        var gateway = new FakeMemberRegistrationGateway(
            _ => throw new InvalidOperationException("Gateway should not be called."));
        var service = CreateService(gateway, new RecordingEmailSender());
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
        var service = CreateService(gateway, new RecordingEmailSender());
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
        var service = CreateService(gateway, new RecordingEmailSender());

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
        IEmailSender emailSender) =>
        new(gateway, emailSender, Options.Create(new FrontendLinkOptions
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

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public Task<EmailDeliveryResult> SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.FromResult(new EmailDeliveryResult(EmailDeliveryStatus.Sent));
        }
    }
}
