using System.Text.Json;
using DoSelect.Application.Notifications;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Notifications;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DeliveryState = DoSelect.Domain.Notifications.EmailDeliveryStatus;
using SenderState = DoSelect.Application.Notifications.EmailDeliveryStatus;

namespace DoSelect.Infrastructure.Outbox;

public sealed record OutboxConsumeResult(
    bool Succeeded,
    bool ShouldRetry,
    string? ErrorCode = null,
    TimeSpan? RetryDelay = null)
{
    public static OutboxConsumeResult Success() => new(true, false);

    public static OutboxConsumeResult Retry(string errorCode, TimeSpan delay) =>
        new(false, true, errorCode, delay);

    public static OutboxConsumeResult Failure(string errorCode) =>
        new(false, false, errorCode);
}

public interface IOutboxConsumer
{
    string EventType { get; }

    Task<OutboxConsumeResult> ConsumeAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class InAppNotificationOutboxConsumer(
    DoSelectDbContext context,
    IInAppNotificationContentRenderer renderer,
    TimeProvider timeProvider) : IOutboxConsumer
{
    public string EventType => OutboxEventTypes.InAppNotificationRequestedV1;

    public async Task<OutboxConsumeResult> ConsumeAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.PayloadVersion != 1)
        {
            return OutboxConsumeResult.Failure("outbox_payload_version_unsupported");
        }

        var payload = Deserialize<InAppNotificationRequestedV1>(message.PayloadJson);
        if (payload is null)
        {
            return OutboxConsumeResult.Failure("outbox_payload_invalid");
        }

        if (await context.Notifications.AnyAsync(
                notification => notification.PublicId == payload.NotificationPublicId,
                cancellationToken))
        {
            return OutboxConsumeResult.Success();
        }

        var content = renderer.Render(payload);
        if (content is null)
        {
            return OutboxConsumeResult.Failure("notification_content_unavailable");
        }

        var recipientUserId = await context.Users.AsNoTracking()
            .Where(user => user.PublicId == payload.MemberPublicId)
            .Select(user => user.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (recipientUserId is null)
        {
            return OutboxConsumeResult.Failure("notification_recipient_not_found");
        }

        context.Notifications.Add(new Notification(
            payload.NotificationPublicId,
            recipientUserId,
            content.Type,
            content.Title,
            content.Body,
            payload.ResourceType,
            payload.ResourcePublicId,
            content.ExpiresAtUtc,
            timeProvider.GetUtcNow().UtcDateTime));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return OutboxConsumeResult.Success();
        }
        catch (DbUpdateException exception) when (SqlDuplicateKey.IsViolation(exception))
        {
            context.ChangeTracker.Clear();
            if (await context.Notifications.AnyAsync(
                notification => notification.PublicId == payload.NotificationPublicId,
                cancellationToken))
            {
                return OutboxConsumeResult.Success();
            }

            throw;
        }
    }

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, OutboxJson.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

public sealed class EmailNotificationOutboxConsumer(
    DoSelectDbContext context,
    IEmailNotificationContentResolver resolver,
    IEmailSender sender,
    TimeProvider timeProvider,
    ILogger<EmailNotificationOutboxConsumer> logger) : IOutboxConsumer
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan ProcessingRecoveryWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ConcurrentAttemptDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    public string EventType => OutboxEventTypes.EmailNotificationRequestedV1;

    public async Task<OutboxConsumeResult> ConsumeAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.PayloadVersion != 1)
        {
            return OutboxConsumeResult.Failure("outbox_payload_version_unsupported");
        }

        var payload = Deserialize(message.PayloadJson);
        if (payload is null)
        {
            return OutboxConsumeResult.Failure("outbox_payload_invalid");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var delivery = await context.EmailDeliveries.SingleOrDefaultAsync(
            candidate => candidate.NotificationPublicId == payload.NotificationPublicId,
            cancellationToken);

        if (delivery is not null)
        {
            var terminal = TerminalResult(delivery);
            if (terminal is not null)
            {
                return terminal;
            }

            if (delivery.Status == DeliveryState.Processing)
            {
                if (delivery.UpdatedAtUtc <= now - ProcessingRecoveryWindow)
                {
                    delivery.MarkFailed(EmailDeliveryErrorCodes.DeliveryAmbiguous, now);
                    await context.SaveChangesAsync(cancellationToken);
                    return OutboxConsumeResult.Failure(EmailDeliveryErrorCodes.DeliveryAmbiguous);
                }

                return OutboxConsumeResult.Retry(
                    "email_delivery_in_progress",
                    ConcurrentAttemptDelay);
            }

            if (delivery.NextAttemptAtUtc > now)
            {
                return OutboxConsumeResult.Retry(
                    delivery.LastErrorCode ?? EmailDeliveryErrorCodes.TransportUnavailable,
                    delivery.NextAttemptAtUtc.Value - now);
            }
        }
        else
        {
            var content = await resolver.ResolveAsync(payload, cancellationToken);
            if (content is null)
            {
                return OutboxConsumeResult.Failure(EmailDeliveryErrorCodes.ContentUnavailable);
            }

            delivery = new EmailDelivery(
                payload.NotificationPublicId,
                content.RecipientUserId,
                content.Message.RecipientAddress,
                payload.TemplateKey,
                payload.ParameterSetVersion,
                payload.RecipientPurpose,
                now);
            context.EmailDeliveries.Add(delivery);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (SqlDuplicateKey.IsViolation(exception))
            {
                context.ChangeTracker.Clear();
                delivery = await context.EmailDeliveries.SingleAsync(
                    candidate => candidate.NotificationPublicId == payload.NotificationPublicId,
                    cancellationToken);
                var terminal = TerminalResult(delivery);
                return terminal ?? OutboxConsumeResult.Retry(
                    "email_delivery_in_progress",
                    ConcurrentAttemptDelay);
            }
        }

        var resolvedContent = await resolver.ResolveAsync(payload, cancellationToken);
        if (resolvedContent is null)
        {
            delivery.BeginAttempt(now);
            delivery.MarkFailed(EmailDeliveryErrorCodes.ContentUnavailable, now);
            await context.SaveChangesAsync(cancellationToken);
            return OutboxConsumeResult.Failure(EmailDeliveryErrorCodes.ContentUnavailable);
        }

        delivery.BeginAttempt(now);
        await context.SaveChangesAsync(cancellationToken);

        EmailDeliveryResult sendResult;
        try
        {
            sendResult = await sender.SendAsync(resolvedContent.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Email delivery ended in an ambiguous state. NotificationPublicId={NotificationPublicId}",
                payload.NotificationPublicId);
            delivery.MarkFailed(EmailDeliveryErrorCodes.DeliveryAmbiguous, now);
            await context.SaveChangesAsync(CancellationToken.None);
            return OutboxConsumeResult.Failure(EmailDeliveryErrorCodes.DeliveryAmbiguous);
        }

        return await ApplySendResultAsync(delivery, sendResult, now, cancellationToken);
    }

    private async Task<OutboxConsumeResult> ApplySendResultAsync(
        EmailDelivery delivery,
        EmailDeliveryResult result,
        DateTime now,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case SenderState.Sent when !string.IsNullOrWhiteSpace(result.MessageId):
                delivery.MarkSent(result.MessageId, now);
                await context.SaveChangesAsync(cancellationToken);
                return OutboxConsumeResult.Success();
            case SenderState.Suppressed:
                delivery.MarkSuppressed(
                    result.ErrorCode ?? EmailDeliveryErrorCodes.Suppressed,
                    now);
                await context.SaveChangesAsync(cancellationToken);
                return OutboxConsumeResult.Success();
            case SenderState.TransientFailure when delivery.AttemptCount < MaximumAttempts:
                {
                    var delay = RetryDelays[Math.Min(delivery.AttemptCount - 1, RetryDelays.Length - 1)];
                    var errorCode = result.ErrorCode ?? EmailDeliveryErrorCodes.TransportUnavailable;
                    delivery.ScheduleRetry(errorCode, now + delay, now);
                    await context.SaveChangesAsync(cancellationToken);
                    return OutboxConsumeResult.Retry(errorCode, delay);
                }
            default:
                {
                    var errorCode = result.ErrorCode ?? EmailDeliveryErrorCodes.Rejected;
                    delivery.MarkFailed(errorCode, now);
                    await context.SaveChangesAsync(cancellationToken);
                    return OutboxConsumeResult.Failure(errorCode);
                }
        }
    }

    private static OutboxConsumeResult? TerminalResult(EmailDelivery delivery) =>
        delivery.Status switch
        {
            DeliveryState.Sent or DeliveryState.Suppressed => OutboxConsumeResult.Success(),
            DeliveryState.Failed => OutboxConsumeResult.Failure(
                delivery.LastErrorCode ?? EmailDeliveryErrorCodes.Rejected),
            _ => null,
        };

    private static EmailNotificationRequestedV1? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EmailNotificationRequestedV1>(json, OutboxJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };
}

internal static class SqlDuplicateKey
{
    public static bool IsViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
