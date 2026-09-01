using DoSelect.Application.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Inventory;

/// <summary>
/// M-10 逾時取消（庫存規則.md「背景排程自動取消逾時訂單並釋放保留庫存」）。
/// <see cref="IInventoryReservationService.ExpireOverdueReservationsAsync"/> 早就寫好了，連併發與
/// 冪等測試都有，但整個儲存庫裡唯一的呼叫者是測試——它的契約註解自己寫著「job logic exists,
/// caller decides when to invoke it」。少了這支排程，過期的保留會一直佔著 ReservedQuantity，最後
/// 一件商品明明沒人買成也永遠顯示缺貨。形狀完全比照
/// <see cref="Builds.CompatibilityCheckRunRetentionBackgroundService"/>。
///
/// 間隔取 1 分鐘：正式保留期限是信用卡／行動支付 15 分鐘、ATM／超商代碼 3 天，但同一份文件允許
/// Demo 環境縮到 2～3 分鐘以展示逾時釋放——掃描間隔若比那還長，展示時就永遠看不到釋放發生。
/// 每次掃描是一句帶索引條件的 UPDATE 級查詢，一分鐘一次的成本可以忽略。
///
/// 範圍界線：這裡只釋放庫存保留。規格同一段提到的「自動取消逾時訂單」屬於 M-08 訂單狀態機，是
/// haru 的模組（工程包 §7：我提供 Reservation，取得 Order 唯讀摘要），不在本 PR 內。
/// </summary>
public sealed class InventoryReservationExpiryBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<InventoryReservationExpiryBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reservationService = scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();

                // 掃描本身是冪等的：已經被釋放的保留會被跳過，不會重複釋放（庫存規則.md「兩種
                // 方式都必須確保冪等」），所以就算這一輪跟管理員的手動釋放撞在一起也沒關係。
                var released = await reservationService.ExpireOverdueReservationsAsync(
                    DateTime.UtcNow,
                    stoppingToken);

                if (released > 0)
                {
                    logger.LogInformation(
                        "Released {ReleasedCount} overdue inventory reservation(s).",
                        released);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 一次掃描失敗不該讓排程整個停掉——下一分鐘再試一次即可。
                logger.LogError(ex, "Unhandled exception while releasing overdue inventory reservations.");
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
