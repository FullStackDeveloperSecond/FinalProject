using DoSelect.Application.Builds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Builds;

/// <summary>
/// 組長 PR #34 round-4 review, item 5: <see cref="ICompatibilityCheckService.PurgeExpiredRunsAsync"/>
/// already existed but had no Hosted Service, schedule, or any other caller wiring it up — every
/// compatibility check (including anonymous, unauthenticated public calls) persists an immutable
/// CompatibilityCheckRun/Result snapshot, so without a running retention job those tables grow
/// without bound. Runs once at startup and then daily, mirroring
/// UnverifiedMemberCleanupBackgroundService's cadence; drains every batch in a loop each cycle
/// (not just one) so a large backlog — e.g. this job's first-ever run after being wired up late —
/// doesn't have to wait a full day per batch to catch up.
///
/// The 90-day retention window is Terry's own default: no spec doc names a retention period for
/// CompatibilityCheckRun specifically (it mirrors the 90-day committed-summary retention already
/// used elsewhere in this engineering package, e.g. import batch summaries) — flagged for 組長 to
/// confirm or override, not something derived from an existing written decision.
/// </summary>
public sealed class CompatibilityCheckRunRetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<CompatibilityCheckRunRetentionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(90);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var checkService = scope.ServiceProvider.GetRequiredService<ICompatibilityCheckService>();
                var olderThanUtc = DateTime.UtcNow - RetentionWindow;

                var totalDeleted = 0;
                int deletedThisBatch;
                do
                {
                    deletedThisBatch = await checkService.PurgeExpiredRunsAsync(olderThanUtc, BatchSize, stoppingToken);
                    totalDeleted += deletedThisBatch;
                }
                while (deletedThisBatch == BatchSize && !stoppingToken.IsCancellationRequested);

                if (totalDeleted > 0)
                {
                    logger.LogInformation(
                        "Purged {DeletedCount} compatibility check run(s) past the {RetentionDays}-day retention window.",
                        totalDeleted, RetentionWindow.TotalDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception while purging expired compatibility check runs.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
