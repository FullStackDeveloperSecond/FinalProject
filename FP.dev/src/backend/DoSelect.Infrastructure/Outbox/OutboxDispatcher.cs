using System.Data;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Persistence;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Outbox;

public sealed class BackgroundJobSettings
{
    public const string SectionName = "BackgroundJobs";

    public string SchemaName { get; init; } = "HangFire";
    public int WorkerCount { get; init; } = 4;
    public int DispatcherPollSeconds { get; init; } = 5;
    public int DispatcherBatchSize { get; init; } = 20;
    public int ClaimLeaseSeconds { get; init; } = 60;
}

public interface IOutboxDispatcher
{
    Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default);
}

public sealed class OutboxDispatcher(
    DoSelectDbContext context,
    IBackgroundJobClient backgroundJobs,
    IOptions<BackgroundJobSettings> settings,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    private const string ClaimSql = """
        SELECT TOP (@batchSize) candidate.*
        FROM [OutboxMessages] AS candidate WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE candidate.[Status] IN ('Pending', 'Processing')
          AND candidate.[AvailableAtUtc] <= @nowUtc
          AND NOT EXISTS
          (
              SELECT 1
              FROM [OutboxMessages] AS earlier WITH (READPAST)
              WHERE earlier.[AggregateType] = candidate.[AggregateType]
                AND earlier.[AggregatePublicId] = candidate.[AggregatePublicId]
                AND earlier.[Status] NOT IN ('Processed', 'Failed')
                AND
                (
                    earlier.[OccurredAtUtc] < candidate.[OccurredAtUtc]
                    OR (earlier.[OccurredAtUtc] = candidate.[OccurredAtUtc] AND earlier.[Id] < candidate.[Id])
                )
          )
        ORDER BY candidate.[OccurredAtUtc], candidate.[Id];
        """;

    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var options = settings.Value;
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var candidates = await context.OutboxMessages
            .FromSqlRaw(
                ClaimSql,
                new SqlParameter("@batchSize", options.DispatcherBatchSize),
                new SqlParameter("@nowUtc", now))
            .ToListAsync(cancellationToken);

        var leaseUntil = now.AddSeconds(options.ClaimLeaseSeconds);
        foreach (var candidate in candidates)
        {
            candidate.Claim(now, leaseUntil);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            try
            {
                backgroundJobs.Create(
                    Job.FromExpression<OutboxDispatchJob>(job => job.ProcessAsync(
                        candidate.PublicId,
                        candidate.PayloadVersion,
                        candidate.CorrelationId,
                        CancellationToken.None)),
                    new EnqueuedState(QueueFor(candidate.Type)));
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to enqueue claimed outbox message. OutboxPublicId={OutboxPublicId} CorrelationId={CorrelationId}",
                    candidate.PublicId,
                    candidate.CorrelationId);
                // The claim lease expires after 60 seconds, so a later poll safely re-enqueues it.
            }
        }

        return candidates.Count;
    }

    private static string QueueFor(string eventType) => eventType switch
    {
        OutboxEventTypes.EmailNotificationRequestedV1 => "notifications",
        OutboxEventTypes.InAppNotificationRequestedV1 => "notifications",
        OutboxEventTypes.InventoryReconciliationMismatchDetectedV1 => "critical",
        _ => "maintenance",
    };
}

public sealed class OutboxDispatchJob(
    DoSelectDbContext context,
    IEnumerable<IOutboxConsumer> consumers,
    TimeProvider timeProvider,
    ILogger<OutboxDispatchJob> logger)
{
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task ProcessAsync(
        Guid outboxPublicId,
        int payloadVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.SingleOrDefaultAsync(
            candidate => candidate.PublicId == outboxPublicId,
            cancellationToken);
        if (message is null || message.Status is OutboxMessageStatus.Processed or OutboxMessageStatus.Failed)
        {
            return;
        }

        if (message.Status != OutboxMessageStatus.Processing)
        {
            logger.LogWarning(
                "Ignored an outbox job whose message is not claimed. OutboxPublicId={OutboxPublicId} Status={Status}",
                outboxPublicId,
                message.Status);
            return;
        }

        if (message.PayloadVersion != payloadVersion ||
            !string.Equals(message.CorrelationId, correlationId, StringComparison.Ordinal))
        {
            message.Fail("outbox_job_contract_mismatch");
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var consumer = consumers.SingleOrDefault(candidate =>
            string.Equals(candidate.EventType, message.Type, StringComparison.Ordinal));
        if (consumer is null)
        {
            message.Fail("outbox_consumer_unavailable");
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        OutboxConsumeResult result;
        try
        {
            result = await consumer.ConsumeAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled outbox consumer failure. OutboxPublicId={OutboxPublicId} Type={Type} CorrelationId={CorrelationId}",
                message.PublicId,
                message.Type,
                message.CorrelationId);
            message.Fail("outbox_consumer_unhandled");
            await context.SaveChangesAsync(CancellationToken.None);
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (result.Succeeded)
        {
            message.Complete(now);
        }
        else if (result.ShouldRetry && result.RetryDelay is not null)
        {
            message.ScheduleRetry(
                result.ErrorCode ?? "outbox_consumer_transient_failure",
                now + result.RetryDelay.Value);
        }
        else
        {
            message.Fail(result.ErrorCode ?? "outbox_consumer_failed");
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A duplicate Hangfire execution already finalized or rescheduled this message.
            // Reloading avoids overwriting the winner and makes this execution idempotent.
            await context.Entry(message).ReloadAsync(cancellationToken);
        }
    }
}

public sealed class OutboxDispatcherBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobSettings> settings,
    ILogger<OutboxDispatcherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(settings.Value.DispatcherPollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                await dispatcher.DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatcher polling failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }
}
