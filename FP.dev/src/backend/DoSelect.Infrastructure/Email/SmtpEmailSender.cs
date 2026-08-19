using System.Net.Sockets;
using DoSelect.Application.Notifications;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace DoSelect.Infrastructure.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailOptions _options;
    private readonly ISmtpTransport _transport;

    public SmtpEmailSender(SmtpEmailOptions options)
        : this(options, new MailKitSmtpTransport())
    {
    }

    internal SmtpEmailSender(SmtpEmailOptions options, ISmtpTransport transport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (!EmailMessageFactory.TryCreate(message, _options, out var mimeMessage))
        {
            return new EmailDeliveryResult(
                EmailDeliveryStatus.PermanentFailure,
                ErrorCode: EmailDeliveryErrorCodes.InvalidMessage);
        }

        using (mimeMessage)
        {
            try
            {
                await _transport.SendAsync(mimeMessage, _options, cancellationToken);
                return new EmailDeliveryResult(
                    EmailDeliveryStatus.Sent,
                    mimeMessage.MessageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                SmtpFailureClassifier.TryClassify(exception, out var failure))
            {
                return failure;
            }
        }
    }
}

internal interface ISmtpTransport
{
    Task SendAsync(
        MimeMessage message,
        SmtpEmailOptions options,
        CancellationToken cancellationToken);
}

internal sealed class MailKitSmtpTransport : ISmtpTransport
{
    public async Task SendAsync(
        MimeMessage message,
        SmtpEmailOptions options,
        CancellationToken cancellationToken)
    {
        using var client = new SmtpClient
        {
            CheckCertificateRevocation = true,
            Timeout = options.TimeoutMilliseconds,
        };

        try
        {
            await client.ConnectAsync(
                options.SmtpHost,
                options.SmtpPort,
                SecureSocketOptions.StartTls,
                cancellationToken);
            await client.AuthenticateAsync(
                options.UserName,
                options.Password,
                cancellationToken);
            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Disconnect failure must not turn an accepted message into a retry.
                }
            }
        }
    }
}

internal static class EmailMessageFactory
{
    public static bool IsValid(EmailMessage message)
    {
        return IsInternetMailbox(message.RecipientAddress, out _) &&
            !string.IsNullOrWhiteSpace(message.Subject) &&
            (!string.IsNullOrWhiteSpace(message.TextBody) ||
                !string.IsNullOrWhiteSpace(message.HtmlBody));
    }

    public static bool TryCreate(
        EmailMessage message,
        SmtpEmailOptions options,
        out MimeMessage mimeMessage)
    {
        mimeMessage = new MimeMessage();
        if (!IsValid(message) ||
            !IsInternetMailbox(message.RecipientAddress, out var recipient) ||
            !IsInternetMailbox(options.SenderAddress, out var sender))
        {
            mimeMessage.Dispose();
            return false;
        }

        mimeMessage.MessageId = MimeUtils.GenerateMessageId("doselect.local");
        mimeMessage.From.Add(new MailboxAddress(options.SenderName, sender.Address));
        mimeMessage.To.Add(recipient);
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        return true;
    }

    private static bool IsInternetMailbox(
        string address,
        out MailboxAddress mailboxAddress)
    {
        mailboxAddress = null!;
        if (!address.Contains('@') ||
            !MailboxAddress.TryParse(address, out var parsedAddress) ||
            parsedAddress is null)
        {
            return false;
        }

        mailboxAddress = parsedAddress;
        return true;
    }
}

internal static class SmtpFailureClassifier
{
    public static bool TryClassify(
        Exception exception,
        out EmailDeliveryResult failure)
    {
        switch (exception)
        {
            case AuthenticationException:
            case ServiceNotAuthenticatedException:
                failure = Permanent(EmailDeliveryErrorCodes.AuthenticationFailed);
                return true;
            case SmtpCommandException commandException
                when IsTransient(commandException.StatusCode):
                failure = Transient(EmailDeliveryErrorCodes.TransportUnavailable);
                return true;
            case SmtpCommandException:
                failure = Permanent(EmailDeliveryErrorCodes.Rejected);
                return true;
            case SmtpProtocolException:
            case ServiceNotConnectedException:
            case IOException:
            case SocketException:
            case TimeoutException:
                failure = Transient(EmailDeliveryErrorCodes.TransportUnavailable);
                return true;
            default:
                failure = null!;
                return false;
        }
    }

    private static bool IsTransient(SmtpStatusCode statusCode)
    {
        var numericStatusCode = (int)statusCode;
        return numericStatusCode is >= 400 and <= 499;
    }

    private static EmailDeliveryResult Transient(string errorCode)
    {
        return new EmailDeliveryResult(
            EmailDeliveryStatus.TransientFailure,
            ErrorCode: errorCode);
    }

    private static EmailDeliveryResult Permanent(string errorCode)
    {
        return new EmailDeliveryResult(
            EmailDeliveryStatus.PermanentFailure,
            ErrorCode: errorCode);
    }
}
