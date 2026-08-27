using DoSelect.Application.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Persistence.Orders;

/// <summary>
/// 每日清理到期滿 30 天的 GuestOrderAccessRequests／Tokens（DEC-P267）。依主鍵分批
/// （每批最多 <see cref="BatchSize"/> 筆）刪除，一個 Tick 內反覆呼叫直到回傳 0——避免單次
/// 累積了太多到期資料時，一個批次清不完就要等下一個 24 小時週期。結構比照
/// <c>UnverifiedMemberCleanupBackgroundService</c>。
/// </summary>
public sealed class GuestOrderAccessCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<GuestOrderAccessCleanupBackgroundService> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromDays(30);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var gateway = scope.ServiceProvider.GetRequiredService<IGuestOrderAccessGateway>();
                var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime - RetentionAfterExpiry;

                var totalDeleted = 0;
                int deleted;
                do
                {
                    deleted = await gateway.PurgeExpiredAsync(cutoffUtc, BatchSize, stoppingToken);
                    totalDeleted += deleted;
                }
                while (deleted > 0 && !stoppingToken.IsCancellationRequested);

                if (totalDeleted > 0)
                {
                    logger.LogInformation(
                        "Purged {DeletedCount} expired guest order access request/token row(s) past the 30-day retention window.",
                        totalDeleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception while purging expired guest order access rows.");
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
