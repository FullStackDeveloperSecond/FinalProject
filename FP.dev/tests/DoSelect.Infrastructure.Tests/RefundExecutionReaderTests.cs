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

    [Fact]
    public void TheReaderOnlyTouchesTablesThisModuleOwns()
    {
        // 可退款餘額由本模組的 Refunds 與 PaymentAttempts 推導，
        // 不查 Orders，符合工程包「不得讀取其他模組底層表」的約束。
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.Contains("_context.Refunds", source, StringComparison.Ordinal);
        Assert.Contains("_context.PaymentAttempts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Orders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.OrderItems", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Skus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.ShippingMethods", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExecutorRunsInASerializableTransactionWithAConditionalUpdate()
    {
        // P0：核對狀態與餘額、更新退款狀態必須在同一交易內，
        // 並靠 Serializable 範圍鎖加 rowversion 樂觀鎖擋住並行超額退款。
        var source = File.ReadAllText(ExecutorSourcePath());

        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AsNoTracking", source, StringComparison.Ordinal);
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

        // 重試必須包住整個 ExecuteOnceAsync，而不是只包 SaveChangesAsync。
        var retryIndex = source.IndexOf("ExecuteOnceAsync(request, cancellationToken)", StringComparison.Ordinal);
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
