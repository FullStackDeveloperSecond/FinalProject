using DoSelect.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Email;

/// <summary>
/// Drains <see cref="EmailDispatchChannel"/> and hands each message to <see cref="IEmailSender"/>.
/// Runs outside the HTTP request lifecycle so register / resend-verification / forgot-password
/// never wait on SMTP.
/// </summary>
public sealed class EmailDispatchBackgroundService(
    EmailDispatchChannel queue,
    IEmailSender emailSender,
    ILogger<EmailDispatchBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var result = await emailSender.SendAsync(message, stoppingToken);

                if (result.Status is EmailDeliveryStatus.TransientFailure or EmailDeliveryStatus.PermanentFailure)
                {
                    logger.LogWarning(
                        "Email dispatch to {RecipientAddress} did not succeed: {Status} ({ErrorCode}).",
                        message.RecipientAddress,
                        result.Status,
                        result.ErrorCode);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Unhandled exception while dispatching email to {RecipientAddress}.",
                    message.RecipientAddress);
            }
        }
    }
}
