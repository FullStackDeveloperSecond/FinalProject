using System.Text.RegularExpressions;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

public sealed class RefundExecutionReaderTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    [Fact]
    public void AddDoSelectRefunds_ResolvesTheExecuteServiceAndItsReader()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<IRefundExecutionReader>();
        var service = scope.ServiceProvider.GetRequiredService<ExecuteRefundService>();

        Assert.IsType<RefundExecutionReader>(reader);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullForAnEmptyPublicIdWithoutQuerying()
    {
        await using var context = CreateContext();
        var reader = new RefundExecutionReader(context);

        Assert.Null(await reader.FindAsync(Guid.Empty));
    }

    [Fact]
    public void ReaderQueriesAreCoveredByTheDocumentedIndexes()
    {
        using var context = CreateContext();

        var refund = context.Model.FindEntityType(typeof(Refund))!;
        var attempt = context.Model.FindEntityType(typeof(PaymentAttempt))!;

        Assert.Contains(
            refund.GetIndexes(),
            index => index.Properties.Any(property => property.Name == nameof(Refund.PublicId)));
        Assert.Contains(
            refund.GetIndexes(),
            index => index.Properties.Any(property => property.Name == nameof(Refund.OrderId)));
        Assert.Contains(
            attempt.GetIndexes(),
            index => index.GetDatabaseName() == "IX_PaymentAttempts_OrderId_CreatedAtUtc");
    }

    /// <summary>
    /// B1 具名例外的守門測試：掃描**整個** Refund Infrastructure，逐元件白名單。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 白名單來自 alex 於 2026-08-28 的 B1 正式裁定，以**目前核准的表**為上限。
    /// 未來要新增跨模組存取必須重新 review 並更新這份清單與文件，
    /// 不得把 B1 擴張成任意直接存取。
    /// </para>
    /// <para>
    /// 舊版守門測試只掃 <c>RefundExecutionReader.cs</c> 一個檔案，所以**新增任何
    /// Reader 都能繞過** —— 我自己就是這樣在同一支 PR 裡加了三個跨模組讀取而沒被擋下。
    /// 這一版改成掃整個資料夾，而且檔案沒有列進白名單就直接失敗。
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryRefundInfrastructureComponentStaysInsideItsNamedException()
    {
        // 逐元件白名單。key 是檔名，value 是該元件獲准存取的資料表。
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // 可退款餘額由本模組自己的表推導，不碰其他模組。
            ["RefundExecutionReader.cs"] = ["Refunds", "PaymentAttempts"],

            // B1-1：退款計算所需的可信快照。
            ["RefundTrustedInputsReader.cs"] =
            [
                "Refunds", "RefundAllocations",
                "ReturnRequests", "ReturnItems",
                "Orders", "OrderItems", "OrderCoupons",
            ],

            // B1-2：分攤寫入解析 OrderItem 內部主鍵。
            // B1-4：管理員身分／授權／Audit Actor。
            ["RefundExecutor.cs"] =
            [
                "Refunds", "RefundAllocations", "PaymentAttempts",
                "OrderItems",
                "Users", "UserRoles", "Roles", "AdminProfiles",
            ],

            // B1-3：正式 RefundDto 的唯讀投影。
            ["RefundReader.cs"] =
            [
                "Refunds", "RefundAllocations",
                "Orders", "ReturnRequests", "OrderItems", "Users",
            ],

            // 只做 DI 註冊，不碰任何表。
            ["RefundsServiceCollectionExtensions.cs"] = [],
        };

        // _context 上不是 DbSet 的成員，不算資料表存取。
        string[] notTables = ["ChangeTracker", "Entry", "SaveChangesAsync", "Database", "Set"];

        var directory = RefundInfrastructureDirectory();
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);

            // 沒列進白名單的新元件一律失敗 —— 這正是舊版守門漏掉的情況。
            Assert.True(
                allowed.ContainsKey(name),
                $"{name} 不在 B1 白名單裡。新增 Refund Infrastructure 元件必須先經過 " +
                "review、更新白名單與文件，不能自動取得跨模組存取。");

            var touched = Regex.Matches(File.ReadAllText(file), @"_context\.([A-Za-z]+)")
                .Select(match => match.Groups[1].Value)
                .Where(member => !notTables.Contains(member, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(table => table, StringComparer.Ordinal)
                .ToArray();

            var extra = touched.Except(allowed[name], StringComparer.Ordinal).ToArray();

            Assert.True(
                extra.Length == 0,
                $"{name} 存取了白名單以外的資料表：{string.Join("、", extra)}。" +
                "B1 是具名窄範圍例外，不是跨模組通則。");
        }
    }

    /// <summary>
    /// B1 落地要求：Gateway／Reader 不得自行開啟或提交交易。
    /// </summary>
    /// <remarks>
    /// 交易必須由 <c>IIdempotencyExecutor</c> 擁有。自己 <c>BeginTransaction</c> 會讓
    /// <c>EfIdempotencyExecutor</c> 直接丟例外，但那是執行期才發現；這裡在原始碼層擋下來。
    /// </remarks>
    [Fact]
    public void NoRefundInfrastructureComponentOwnsItsOwnTransaction()
    {
        foreach (var file in Directory.GetFiles(
            RefundInfrastructureDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            Assert.DoesNotContain("BeginTransaction", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CommitAsync", source, StringComparison.Ordinal);
            Assert.False(
                source.Contains(".Commit()", StringComparison.Ordinal),
                $"{name} 自行提交交易；交易必須由 IIdempotencyExecutor 擁有。");
        }
    }

    private static string RefundInfrastructureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(
            directory!.FullName, "src", "backend", "DoSelect.Infrastructure", "Refunds");
        Assert.True(Directory.Exists(path), $"找不到 Refund Infrastructure 目錄：{path}");
        return path;
    }

    [Fact]
    public void TheExecutorRunsInASerializableTransactionWithAConditionalUpdate()
    {
        // P0：核對狀態與餘額、更新退款狀態必須在同一交易內，
        // 並靠 Serializable 範圍鎖加 rowversion 樂觀鎖擋住並行超額退款。
        //
        // 交易本身改由共用 IIdempotencyExecutor 擁有（DEC-BATCH-019 A1），因此這裡
        // 斷言的是「有把 Serializable 指定給它」，而不是自己 BeginTransaction ——
        // 自己開交易反而會讓 EfIdempotencyExecutor 直接丟
        // 「must own the business transaction」。
        var source = File.ReadAllText(ExecutorSourcePath());

        Assert.Contains("_idempotencyExecutor.ExecuteAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedRefundRowVersion", source, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", source, StringComparison.Ordinal);

        // 退款列必須被追蹤才能在同一交易內條件更新。
        // 其他唯讀查詢（例如把 Identity Id 換成管理員 PublicId）可以用 AsNoTracking。
        var refundQuery = source[source.IndexOf("_context.Refunds", StringComparison.Ordinal)..];
        var refundQueryEnd = refundQuery.IndexOf("cancellationToken);", StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AsNoTracking",
            refundQuery[..refundQueryEnd],
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheAuditIsWrittenInsideTheRefundTransaction()
    {
        // DEC-P289：稽核與退款狀態必須同批提交，任一失敗即整體回滾。
        // Audit 若在 SaveChanges 之後或另一個交易寫入，退款就可能沒有稽核紀錄。
        var source = File.ReadAllText(ExecutorSourcePath());

        // 比對的是呼叫點的順序，不是方法定義的位置。
        // 提交由共用 Executor 負責，因此這裡只能比到 SaveChanges 為止；
        // 「同一交易」由上面那條測試（Serializable 指定給 Executor）保證。
        var auditCall = source.IndexOf("WriteAudit(refund", StringComparison.Ordinal);
        var saveIndex = source.IndexOf(
            "await _context.SaveChangesAsync(cancellationToken);",
            StringComparison.Ordinal);

        Assert.True(auditCall > 0, "The executor must write a central audit entry.");
        Assert.Contains("_auditWriter.Add", source, StringComparison.Ordinal);
        Assert.InRange(auditCall, 0, saveIndex);
    }

    [Fact]
    public void TheExecutionReasonNeverReachesTheRefundRow()
    {
        // reasonCode 與 note 只寫中央 AuditLog，不寫回 Refund（DEC-P289）。
        var source = File.ReadAllText(ExecutorSourcePath());

        Assert.DoesNotContain("refund.ReasonCode =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("refund.Note", source, StringComparison.Ordinal);

        // reason 只收 safe-code，note 走中央 Audit 的獨立欄位。先前兩者被串成
        // `reasonCode: note` 塞進 reason，任何含空白或中文的 note 都會讓 reason
        // 驗證失敗，把一次正常退款變成 500。
        Assert.Contains("reason: request.ReasonCode.Trim()", source, StringComparison.Ordinal);
        Assert.Contains("note: request.Note", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildReason", source, StringComparison.Ordinal);

        // 請求來源不得被丟掉。
        Assert.Contains(
            "remoteIpAddress: request.RemoteIpAddress", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAuditActorUsesThePublicIdNotTheIdentityId()
    {
        // DEC-P290：稽核與管理員摘要都不得出現內部 Identity Id。
        var source = File.ReadAllText(ExecutorSourcePath());

        Assert.Contains("admin.PublicId", source, StringComparison.Ordinal);
        Assert.Contains("AuditActorType.Admin", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditActor.Create(AuditActorType.Admin, adminUserId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExecutorHandlesDeadlockAtTheTransactionBoundary()
    {
        // alex 以真實 SQL Server 驗出：兩筆並行退款在 Serializable 下會產生死結（1205），
        // 該 SqlException 被包成 DbUpdateException，只捕 DbUpdateConcurrencyException 會讓它逃出，
        // 接上 API 後變成非預期的 500。
        // 修正方式是在交易邊界重跑整段；只重試 SaveChanges 會沿用死結前讀到的舊餘額。
        var source = File.ReadAllText(ExecutorSourcePath());

        Assert.Contains("1205", source, StringComparison.Ordinal);
        Assert.Contains("SqlException", source, StringComparison.Ordinal);
        Assert.Contains("InnerException", source, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Clear()", source, StringComparison.Ordinal);
        Assert.Contains("RefundErrorCodes.ConcurrencyConflict", source, StringComparison.Ordinal);

        // 重試必須包住整個交易，而不是只包 SaveChangesAsync。交易改由共用 Executor
        // 擁有之後，重試迴圈要包住的就是那一次 ExecuteAsync 呼叫。
        var retryIndex = source.IndexOf(
            "_idempotencyExecutor.ExecuteAsync", StringComparison.Ordinal);
        var catchIndex = source.IndexOf("IsRetryableConflict", StringComparison.Ordinal);
        Assert.True(retryIndex > 0 && catchIndex > retryIndex);
    }

    [Fact]
    public void TheConcurrencyConflictCodeIsRegisteredInTheCatalogue()
    {
        // concurrency_conflict 是目錄既有代碼，不是本模組新造的別名。
        Assert.Equal("concurrency_conflict", RefundErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public void TheRefundRowCarriesARowVersionForOptimisticConcurrency()
    {
        using var context = CreateContext();

        var rowVersion = context.Model.FindEntityType(typeof(Refund))!
            .FindProperty(nameof(Refund.RowVersion));

        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
    }

    [Fact]
    public void ThePreviewReaderDocumentsThatItIsNotAtomic()
    {
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.Contains("不保證原子性", source, StringComparison.Ordinal);
        Assert.Contains("IRefundExecutor", source, StringComparison.Ordinal);
    }

    private static string ExecutorSourcePath() => SourcePath("RefundExecutor.cs");

    private static string ReaderSourcePath() => SourcePath("RefundExecutionReader.cs");

    private static string SourcePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src", "backend", "DoSelect.Infrastructure", "Refunds", fileName);
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = SyntheticConnectionString,
            })
            .Build();

        return new ServiceCollection()
            .AddDoSelectPersistence(configuration)
            .AddDoSelectRefunds()
            .BuildServiceProvider();
    }

    private static DoSelectDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);
}
