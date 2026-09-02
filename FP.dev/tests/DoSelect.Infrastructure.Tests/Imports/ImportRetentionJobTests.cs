using System.Security.Cryptography;
using System.Text;
using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Imports;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Imports;

/// <summary>
/// 匯入暫存與庫存調整設計.md「安全與清理」：列 24 小時、摘要 90 天、由 maintenance Queue 執行。
/// 時間全部靠注入的 TimeProvider 往前撥，不去改實體的私有欄位——這樣測的才是工作真正的判斷式。
/// 對真實 SQL Server 跑：ExecuteDeleteAsync 與 Cascade 都是資料庫層的行為。
/// </summary>
[Collection(nameof(ImportServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ImportRetentionJobTests
{
    /// <summary>
    /// ImportBatchStaging.ExpireStaleBatchesAsync 只在同一位管理員下一次上傳時才跑；不再上傳的人留下
    /// 的 Ready 批次要靠這裡收尾，否則它的列永遠不會進入 24 小時倒數。
    /// </summary>
    [Fact]
    public async Task RunAsync_FlipsStaleUnfinishedBatchesToExpired()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var now = DateTime.UtcNow;
        var stale = await SeedBatchAsync(context, now, rows: 1);
        var fresh = await SeedBatchAsync(context, now, rows: 1, expiresAtUtc: now.AddHours(48));

        await RunJobAsync(now.AddHours(25));

        await using var verify = ImportServiceFixture.CreateContext();
        Assert.Equal(ImportBatchStatus.Expired, await StatusOf(verify, stale));
        Assert.Equal(ImportBatchStatus.Ready, await StatusOf(verify, fresh));
        // 剛翻成 Expired 的批次，錯誤列還在——管理員還有 24 小時可以下載修正。
        Assert.Equal(1, await RowCountOf(verify, stale));
    }

    /// <summary>「ImportRow／Raw JSON 在提交後最多保存 24 小時」——摘要留著，列不留。</summary>
    [Fact]
    public async Task RunAsync_DeletesRowsOfTerminalBatchesAfterTwentyFourHoursButKeepsTheSummary()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var now = DateTime.UtcNow;
        var committed = await SeedBatchAsync(context, now, rows: 2, finish: batch => batch.Complete("{}", now));
        var invalid = await SeedBatchAsync(context, now, rows: 2, finish: batch => batch.ChangeStatus(ImportBatchStatus.Invalid, now));

        // 23 小時：還沒到，什麼都不動。
        await RunJobAsync(now.AddHours(23));
        await using (var verify = ImportServiceFixture.CreateContext())
        {
            Assert.Equal(2, await RowCountOf(verify, committed));
            Assert.Equal(2, await RowCountOf(verify, invalid));
        }

        // 25 小時：列清掉，摘要與統計仍在。
        await RunJobAsync(now.AddHours(25));
        await using (var verify = ImportServiceFixture.CreateContext())
        {
            Assert.Equal(0, await RowCountOf(verify, committed));
            Assert.Equal(0, await RowCountOf(verify, invalid));
            var summary = await verify.ImportBatches.AsNoTracking().SingleAsync(candidate => candidate.PublicId == committed);
            Assert.Equal(ImportBatchStatus.Committed, summary.Status);
            Assert.Equal(2, summary.RowCount);
            Assert.NotNull(summary.ResultSummaryJson);
        }
    }

    /// <summary>「Batch 摘要保存 90 天」，之後整筆刪除；Cascade 帶走任何殘留的列。</summary>
    [Fact]
    public async Task RunAsync_DeletesTerminalBatchSummariesAfterNinetyDays()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var now = DateTime.UtcNow;
        var old = await SeedBatchAsync(context, now, rows: 1, finish: batch => batch.ChangeStatus(ImportBatchStatus.Failed, now));
        var recent = await SeedBatchAsync(context, now.AddDays(5), rows: 0, finish: batch => batch.Complete("{}", now.AddDays(5)));

        await RunJobAsync(now.AddDays(91));

        await using var verify = ImportServiceFixture.CreateContext();
        Assert.False(await verify.ImportBatches.AnyAsync(candidate => candidate.PublicId == old));
        Assert.Equal(0, await verify.ImportRows.CountAsync(row => row.ImportKey == $"ROW-{old:N}"));
        Assert.True(await verify.ImportBatches.AnyAsync(candidate => candidate.PublicId == recent));
    }

    /// <summary>
    /// 工作跑在自己的乾淨 DbContext 上——正式環境的 Hangfire scope 就是這樣。若沿用種資料的 context，
    /// 它還追蹤著已被 ExecuteDelete 砍掉的列，RemoveRange 批次時會對那些列再發一次 DELETE，
    /// 撞上 0 rows affected 的併發例外；那是測試的假象，不是工作的行為。
    /// </summary>
    private static async Task RunJobAsync(DateTime utcNow)
    {
        await using var jobContext = ImportServiceFixture.CreateContext();
        await new ImportRetentionJob(jobContext, new FixedTimeProvider(utcNow)).RunAsync(CancellationToken.None);
    }

    private static async Task<ImportBatchStatus> StatusOf(DoSelectDbContext context, Guid publicId) =>
        (await context.ImportBatches.AsNoTracking().SingleAsync(candidate => candidate.PublicId == publicId)).Status;

    private static async Task<int> RowCountOf(DoSelectDbContext context, Guid publicId)
    {
        var id = await context.ImportBatches.AsNoTracking()
            .Where(candidate => candidate.PublicId == publicId)
            .Select(candidate => candidate.Id)
            .SingleAsync();
        return await context.ImportRows.CountAsync(row => row.ImportBatchId == id);
    }

    /// <summary>
    /// 直接種批次而不是跑 Preview：這裡要測的是清理判斷式，不是匯入本身；而且每位管理員同型別
    /// 只能有一個進行中的批次，用真正的 Preview 種不出「多個 Ready 批次」這種情境。
    /// </summary>
    private static async Task<Guid> SeedBatchAsync(
        DoSelectDbContext context,
        DateTime createdAtUtc,
        int rows,
        DateTime? expiresAtUtc = null,
        Action<ImportBatch>? finish = null)
    {
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var batch = new ImportBatch(
            Guid.CreateVersion7(),
            ImportType.InventoryAdjustment,
            templateVersion: 1,
            adminId,
            expiresAtUtc ?? createdAtUtc.AddHours(24),
            Guid.CreateVersion7(),
            createdAtUtc);
        batch.SetSources(new byte[32], "stock.csv", null, null, null, null, createdAtUtc);
        context.ImportBatches.Add(batch);
        await context.SaveChangesAsync();

        for (var index = 0; index < rows; index++)
        {
            var payload = $"{{\"Payload\":{{\"SkuCode\":\"SKU-{index}\"}},\"PreimageRowVersion\":null}}";
            context.ImportRows.Add(new ImportRow(
                batch.Id,
                ImportDataset.InventoryAdjustments,
                sourceRowNumber: index + 2,
                importKey: index == 0 ? $"ROW-{batch.PublicId:N}" : $"SKU-{index}",
                ImportRowAction.Update,
                payload,
                errorCodes: null,
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)),
                rawJson: "[]",
                createdAtUtc));
        }

        batch.SetPreviewStatistics(rows, 0, rows, 0, 0, normalizedContentVersion: 1, createdAtUtc);
        finish?.Invoke(batch);
        await context.SaveChangesAsync();
        return batch.PublicId;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }
}
