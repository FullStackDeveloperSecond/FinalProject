using DoSelect.Application.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Inventory;

/// <summary>
/// M-10 逾時取消（庫存規則.md「背景排程自動取消逾時訂單並釋放保留庫存」）。形狀比照
/// <see cref="Builds.CompatibilityCheckRunRetentionBackgroundService"/>。
///
/// 組長 PR #85 round-1 review [P1]：第一版只釋放庫存保留，訂單留在 PendingPayment、優惠券座位與
/// 待處理組裝資源也沒回收，而且付款成功與這輪掃描在期限邊界可以同時成立。round-2 裁定 B1 進一步
/// 移除了那個「只釋放庫存卻不取消訂單」的公開契約，訂單層取消是現在唯一的逾時入口。現在改為以「訂單」為單位，透過
/// <see cref="IOrderTimeoutCancellationService"/> 在同一交易內原子取消並回收全部資源；訂單列的
/// RowVersion 是併發仲裁者，付款與排程不可能都成功。
///
/// 間隔取 1 分鐘：正式保留期限是信用卡／行動支付 15 分鐘、ATM／超商代碼 3 天，但同一份文件允許
/// Demo 環境縮到 2～3 分鐘以展示逾時釋放——掃描間隔若比那還長，展示時就永遠看不到釋放發生。
///
/// 先等一個間隔再掃第一次。冷啟動時本來就在建 EF 模型、暖連線池、承接第一波流量，維護工作不該
/// 再插一腳；晚一分鐘取消對 15 分鐘／3 天的期限無關緊要。這也讓整合測試的 WebApplicationFactory
/// 生命週期內不會掃到，不會干擾 Required CI 那道量測 20ms 登入延遲差的側通道測試。
/// </summary>
public sealed class InventoryReservationExpiryBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<InventoryReservationExpiryBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 組長 PR #85 round-1 review [P2]：一輪不載入全部逾期資料。每批 200 筆，一輪最多 25 批
    /// （5,000 筆）——服務停機後累積的 backlog 會分好幾輪清完，而不是一次把記憶體與資料庫吃滿。
    /// 一輪拿到的批次小於 BatchSize 就代表清完了，提早收工。
    /// </summary>
    private const int BatchSize = 200;
    private const int MaximumBatchesPerCycle = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var cancelledTotal = 0;
                var failedTotal = 0;
                OrderTimeoutCursor? cursor = null;

                for (var batch = 0; batch < MaximumBatchesPerCycle && !stoppingToken.IsCancellationRequested; batch++)
                {
                    // 每一批都用自己的 scope／DbContext：一輪要跑好幾批，共用一個 DbContext 會讓
                    // ChangeTracker 隨著 backlog 一起長大，等於換個地方重蹈無界記憶體的覆轍。
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var cancellationService = scope.ServiceProvider
                        .GetRequiredService<IOrderTimeoutCancellationService>();

                    var result = await cancellationService.CancelOverduePendingPaymentOrdersAsync(
                        DateTime.UtcNow,
                        BatchSize,
                        cursor,
                        stoppingToken);
                    cancelledTotal += result.Cancelled;
                    failedTotal += result.Failed;
                    cursor = result.NextCursor;

                    // 組長 PR #85 round-3 review [P2]：判斷依據是「這一批檢視了幾筆」，不是「取消了
                    // 幾筆」。庫存不一致的訂單這一輪修不好，但它們確實佔了一個名額——用取消數判斷
                    // 會讓一批全是壞資料時整輪提早收工，而那些壞資料每分鐘都會再被撈到最前面，
                    // 排在後面的健康逾時訂單永遠輪不到。游標則保證下一批一定往後走。
                    if (result.Examined < BatchSize)
                    {
                        break;
                    }
                }

                if (cancelledTotal > 0)
                {
                    logger.LogInformation(
                        "Cancelled {CancelledCount} order(s) past their payment deadline and released their reserved resources.",
                        cancelledTotal);
                }

                if (failedTotal > 0)
                {
                    // 每一筆的細節（訂單 PublicId、correlation id）由服務層記在 Warning 裡；
                    // 這裡只給一輪的總數，讓值班的人一眼看出有沒有累積。
                    logger.LogWarning(
                        "{FailedCount} overdue order(s) could not be cancelled because their inventory state is inconsistent; they need manual repair.",
                        failedTotal);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 一輪失敗不該讓排程整個停掉——下一分鐘再試一次即可。
                logger.LogError(ex, "Unhandled exception while cancelling orders past their payment deadline.");
            }
        }
    }
}
