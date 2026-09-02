using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// 匯入暫存的清理（匯入暫存與庫存調整設計.md「安全與清理」）：
/// <list type="bullet">
/// <item>「Committed Batch 摘要與結果保存 90 天；ImportRow／Raw JSON 在提交後最多保存 24 小時。」</item>
/// <item>「Invalid、Failed、Expired 的錯誤列保存 24 小時供下載修正，Batch 摘要保存 90 天。」</item>
/// <item>「清理由 maintenance Queue 執行；正式領域資料與 AuditLog 是長期稽核來源。」</item>
/// </list>
///
/// 形狀比照 <c>OutboxRetentionJob</c>／<c>AuditRetentionJob</c>：每次最多處理一個有界批次，重跑
/// 是冪等的。刪的只有暫存列與逾期摘要——套用進去的商品、庫存 Movement 與 AuditLog 不在這裡，
/// 也不該在這裡。
/// </summary>
public sealed class ImportRetentionJob(
    DoSelectDbContext context,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan RowRetention = TimeSpan.FromHours(24);
    public static readonly TimeSpan BatchRetention = TimeSpan.FromDays(90);
    public const int BatchSize = 500;

    private static readonly ImportBatchStatus[] TerminalStatuses =
    [
        ImportBatchStatus.Committed,
        ImportBatchStatus.Invalid,
        ImportBatchStatus.Failed,
        ImportBatchStatus.Expired,
    ];

    /// <summary>回傳這一輪動到的筆數（翻成 Expired 的批次＋刪掉的列＋刪掉的批次）。</summary>
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // 1. 過了 24 小時效期卻沒人收尾的批次先翻成 Expired。ImportBatchStaging.ExpireStaleBatchesAsync
        //    只在同一位管理員下一次上傳時才跑；不再上傳的人留下的批次要靠這裡。
        var expired = await ExpireStaleBatchesAsync(nowUtc, cancellationToken);

        // 2. 終態超過 24 小時的批次，刪掉它的列（RawJson 跟著列走）。摘要留著。
        var rowsDeleted = await DeleteExpiredRowsAsync(nowUtc - RowRetention, cancellationToken);

        // 3. 終態超過 90 天的批次摘要整筆刪除；FK 是 Cascade，殘留的列一起走。
        var batchesDeleted = await DeleteExpiredBatchesAsync(nowUtc - BatchRetention, cancellationToken);

        return expired + rowsDeleted + batchesDeleted;
    }

    private async Task<int> ExpireStaleBatchesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var stale = await context.ImportBatches
            .Where(batch => !TerminalStatuses.Contains(batch.Status) && batch.ExpiresAtUtc <= nowUtc)
            .OrderBy(batch => batch.ExpiresAtUtc)
            .ThenBy(batch => batch.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        foreach (var batch in stale)
        {
            batch.ChangeStatus(ImportBatchStatus.Expired, nowUtc);
        }

        await context.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    /// <summary>
    /// 「終態之後 24 小時」以 UpdatedAtUtc 計：Committed 的 UpdatedAtUtc 就是 ConfirmedAtUtc，
    /// Invalid 是 Preview 完成時間，Expired 是被翻成 Expired 的時間。由本工作翻成 Expired 的批次會
    /// 從翻的那一刻再算 24 小時——比規格保守，但不會把管理員還來得及下載的錯誤列提早清掉。
    /// </summary>
    private async Task<int> DeleteExpiredRowsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var batchIds = await context.ImportBatches
            .Where(batch => TerminalStatuses.Contains(batch.Status) &&
                batch.UpdatedAtUtc <= cutoffUtc &&
                context.ImportRows.Any(row => row.ImportBatchId == batch.Id))
            .OrderBy(batch => batch.UpdatedAtUtc)
            .ThenBy(batch => batch.Id)
            .Select(batch => batch.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (batchIds.Count == 0)
        {
            return 0;
        }

        return await context.ImportRows
            .Where(row => batchIds.Contains(row.ImportBatchId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<int> DeleteExpiredBatchesAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var batches = await context.ImportBatches
            .Where(batch => TerminalStatuses.Contains(batch.Status) && batch.UpdatedAtUtc <= cutoffUtc)
            .OrderBy(batch => batch.UpdatedAtUtc)
            .ThenBy(batch => batch.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (batches.Count == 0)
        {
            return 0;
        }

        context.ImportBatches.RemoveRange(batches);
        await context.SaveChangesAsync(cancellationToken);
        return batches.Count;
    }
}
