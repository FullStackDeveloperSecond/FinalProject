using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Email;
using MailKit.Security;
using MimeKit;

namespace DoSelect.Infrastructure.Tests;

public sealed class EmailSenderTests
{
    private static readonly SmtpEmailOptions ValidOptions = new()
    {
        SmtpHost = "smtp.example.test",
        SmtpPort = 587,
        UserName = "smtp-user",
        Password = "synthetic-test-password",
        SenderName = "DoSelect Test",
        SenderAddress = "sender@example.test",
        TimeoutMilliseconds = 15000,
    };

    [Fact]
    public async Task LocalSender_WhenMessageIsValid_ReturnsSuppressedWithoutDelivery()
    {
        var sender = new LocalEmailSender();

        var result = await sender.SendAsync(CreateMessage());

        Assert.Equal(EmailDeliveryStatus.Suppressed, result.Status);
        Assert.False(result.WasDelivered);
        Assert.Equal(EmailDeliveryErrorCodes.Suppressed, result.ErrorCode);
        Assert.Null(result.MessageId);
    }

    [Fact]
    public async Task SmtpSender_WhenTransportAcceptsMessage_ReturnsGeneratedMessageId()
    {
        var transport = new FakeSmtpTransport();
        var sender = new SmtpEmailSender(ValidOptions, transport);

        var result = await sender.SendAsync(CreateMessage());

        Assert.Equal(EmailDeliveryStatus.Sent, result.Status);
        Assert.True(result.WasDelivered);
        Assert.False(string.IsNullOrWhiteSpace(result.MessageId));
        Assert.Null(result.ErrorCode);
        Assert.Equal("sender@example.test", transport.FromAddress);
        Assert.Equal("DoSelect Test", transport.FromName);
        Assert.Equal("member@example.test", transport.ToAddress);
        Assert.Equal("Synthetic body", transport.TextBody);
        Assert.Equal("<p>Synthetic body</p>", transport.HtmlBody);
    }

    [Fact]
    public async Task SmtpSender_WhenTransportIsUnavailable_ReturnsSafeTransientFailure()
    {
        var transport = new FakeSmtpTransport(new IOException("synthetic recipient detail"));
        var sender = new SmtpEmailSender(ValidOptions, transport);

        var result = await sender.SendAsync(CreateMessage());

        Assert.Equal(EmailDeliveryStatus.TransientFailure, result.Status);
        Assert.Equal(EmailDeliveryErrorCodes.TransportUnavailable, result.ErrorCode);
        Assert.DoesNotContain("recipient", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SmtpSender_WhenAuthenticationFails_ReturnsSafePermanentFailure()
    {
        var transport = new FakeSmtpTransport(
            new AuthenticationException("synthetic-test-password"));
        var sender = new SmtpEmailSender(ValidOptions, transport);

        var result = await sender.SendAsync(CreateMessage());

        Assert.Equal(EmailDeliveryStatus.PermanentFailure, result.Status);
        Assert.Equal(EmailDeliveryErrorCodes.AuthenticationFailed, result.ErrorCode);
        Assert.DoesNotContain(
            "synthetic-test-password",
            result.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmtpSender_WhenMessageIsInvalid_DoesNotCallTransport()
    {
        var transport = new FakeSmtpTransport();
        var sender = new SmtpEmailSender(ValidOptions, transport);
        var message = CreateMessage() with { RecipientAddress = "not-an-email" };

        var result = await sender.SendAsync(message);

        Assert.Equal(EmailDeliveryStatus.PermanentFailure, result.Status);
        Assert.Equal(EmailDeliveryErrorCodes.InvalidMessage, result.ErrorCode);
        Assert.Null(transport.ToAddress);
    }

    [Fact]
    public async Task SmtpSender_WhenCallerCancels_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var transport = new FakeSmtpTransport();
        var sender = new SmtpEmailSender(ValidOptions, transport);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sender.SendAsync(CreateMessage(), cancellationSource.Token));
    }

    private static EmailMessage CreateMessage()
    {
        return new EmailMessage(
            "member@example.test",
            "Synthetic subject",
            "Synthetic body",
            "<p>Synthetic body</p>");
    }

    private sealed class FakeSmtpTransport : ISmtpTransport
    {
        private readonly Exception? _exception;

        public FakeSmtpTransport(Exception? exception = null)
        {
            _exception = exception;
        }

        public string? FromAddress { get; private set; }

        public string? FromName { get; private set; }

        public string? ToAddress { get; private set; }

        public string? TextBody { get; private set; }

        public string? HtmlBody { get; private set; }

        public Task SendAsync(
            MimeMessage message,
            SmtpEmailOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_exception is not null)
            {
                throw _exception;
            }

            FromAddress = message.From.Mailboxes.Single().Address;
            FromName = message.From.Mailboxes.Single().Name;
            ToAddress = message.To.Mailboxes.Single().Address;
            TextBody = message.TextBody;
            HtmlBody = message.HtmlBody;
            return Task.CompletedTask;
        }
    }
}
