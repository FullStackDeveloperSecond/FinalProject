using DoSelect.Application.Members;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Persistence.Identity;

/// <summary>
/// Runs <see cref="PurgeStaleUnverifiedMembersService"/> once at startup and then once every
/// 24 hours. A daily cadence is appropriate for a 7-day retention window — no need for a
/// dedicated job scheduler.
/// </summary>
public sealed class UnverifiedMemberCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<UnverifiedMemberCleanupBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var purgeService = scope.ServiceProvider.GetRequiredService<PurgeStaleUnverifiedMembersService>();
                var anonymizedCount = await purgeService.PurgeAsync(stoppingToken);
                if (anonymizedCount > 0)
                {
                    logger.LogInformation(
                        "Anonymized {AnonymizedCount} unverified member account(s) past the 7-day retention window.",
                        anonymizedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception while purging stale unverified members.");
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
