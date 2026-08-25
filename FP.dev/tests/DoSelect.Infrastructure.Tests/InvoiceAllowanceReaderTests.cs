using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Invoicing;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// 折讓 Reader 的 SQL Server Provider-backed 測試環境。
/// 沿用 <c>IdempotencyExecutorFixture</c> 的做法：預設連本機具名執行個體，
/// 伺服器可用環境變數覆寫；CI 上未設定連線字串時整組跳過。
/// </summary>
/// <remarks>
/// 環境變數只決定**伺服器**，資料庫名稱一律強制為這組測試專屬的名稱。
/// 多組 SQL Server 測試共用同一個環境變數時，若也共用資料庫，
/// 各自的 <c>EnsureDeleted</c> 會在平行執行時互相把對方的資料庫刪掉。
/// </remarks>
public sealed class InvoiceAllowanceSqlFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";

    /// <summary>
    /// 這組測試專屬的資料庫名稱。共用環境變數指到的資料庫會被別組的
    /// <c>EnsureDeleted</c> 直接刪掉，因此不論連線從哪裡來，一律改指到這個名稱。
    /// </summary>
    private const string DatabaseName = "DoSelectAllowanceReaderTests";

    private const string LocalServer = "Server=.\\SQL2025;";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(GetConfiguredConnectionString()) ||
        (OperatingSystem.IsWindows() &&
         !string.Equals(
             Environment.GetEnvironmentVariable("CI"),
             "true",
             StringComparison.OrdinalIgnoreCase));

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(BuildConnectionString())
            .Options);

    /// <summary>
    /// 取用共用環境變數的**伺服器**設定，但強制換掉資料庫名稱。
    /// 這樣多組 SQL Server 測試指向同一台伺服器時也不會互相刪除資料庫。
    /// </summary>
    private static string BuildConnectionString()
    {
        var configured = GetConfiguredConnectionString();
        var builder = new SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? LocalServer : configured)
        {
            InitialCatalog = DatabaseName,
            TrustServerCertificate = true,
        };

        if (string.IsNullOrWhiteSpace(configured))
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static string? GetConfiguredConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
}

public sealed class InvoiceAllowanceSqlFactAttribute : FactAttribute
{
    public InvoiceAllowanceSqlFactAttribute()
    {
        if (!InvoiceAllowanceSqlFixture.IsEnabled)
        {
            Skip = "Set " + InvoiceAllowanceSqlFixture.ConnectionStringEnvironmentVariable +
                   " to run SQL Server integration tests.";
        }
    }
}

[CollectionDefinition(nameof(InvoiceAllowanceSqlCollection))]
public sealed class InvoiceAllowanceSqlCollection : ICollectionFixture<InvoiceAllowanceSqlFixture>;

/// <summary>
/// 實際對 SQL Server 執行查詢的折讓 Reader 測試。
/// </summary>
[Collection(nameof(InvoiceAllowanceSqlCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class InvoiceAllowanceReaderSqlServerTests
{
    private static readonly DateTime IssuedAtUtc = new(2026, 8, 24, 2, 0, 0, DateTimeKind.Utc);

    [InvoiceAllowanceSqlFact]
    public async Task TakingASequenceOutsideATransactionIsRejected()
    {
        // 取號與寫入不在同一交易內時，號碼沒有任何保證，因此必須直接拒絕，
        // 而不是回一個看起來可用的數字。
        await using var context = InvoiceAllowanceSqlFixture.CreateContext();
        var reader = new InvoiceAllowanceReader(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.NextAllowanceSequenceAsync(IssuedAtUtc));

        Assert.Contains("transaction", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [InvoiceAllowanceSqlFact]
    public async Task TheFirstSequenceOfAMonthIsOne()
    {
        await using var context = InvoiceAllowanceSqlFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var reader = new InvoiceAllowanceReader(context);

        Assert.Equal(1, await reader.NextAllowanceSequenceAsync(IssuedAtUtc));

        await transaction.RollbackAsync();
    }

    [InvoiceAllowanceSqlFact]
    public async Task ASecondCallerCannotTakeANumberWhileTheFirstTransactionHoldsTheLock()
    {
        // 這是取號原子性的實際證明：先前的 CountAsync + 1 沒有任何互斥，
        // 兩個並行請求會拿到同一個號碼。應用程式鎖必須讓第二個呼叫拿不到號。
        await using var first = InvoiceAllowanceSqlFixture.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        Assert.Equal(1, await new InvoiceAllowanceReader(first).NextAllowanceSequenceAsync(IssuedAtUtc));

        await using var second = InvoiceAllowanceSqlFixture.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InvoiceAllowanceReader(second).NextAllowanceSequenceAsync(IssuedAtUtc));

        await secondTransaction.RollbackAsync();
        await firstTransaction.RollbackAsync();
    }

    [InvoiceAllowanceSqlFact]
    public async Task TheLockIsReleasedWhenTheTransactionEnds()
    {
        await using (var first = InvoiceAllowanceSqlFixture.CreateContext())
        {
            await using var transaction = await first.Database.BeginTransactionAsync();
            await new InvoiceAllowanceReader(first).NextAllowanceSequenceAsync(IssuedAtUtc);
            await transaction.RollbackAsync();
        }

        // 前一個交易結束後鎖必須自動釋放，否則整個月份的取號會永久卡死。
        await using var second = InvoiceAllowanceSqlFixture.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();

        Assert.Equal(1, await new InvoiceAllowanceReader(second).NextAllowanceSequenceAsync(IssuedAtUtc));

        await secondTransaction.RollbackAsync();
    }

    [InvoiceAllowanceSqlFact]
    public async Task DifferentMonthsDoNotBlockEachOther()
    {
        await using var first = InvoiceAllowanceSqlFixture.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await new InvoiceAllowanceReader(first).NextAllowanceSequenceAsync(IssuedAtUtc);

        await using var second = InvoiceAllowanceSqlFixture.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var nextMonth = new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc);

        Assert.Equal(1, await new InvoiceAllowanceReader(second).NextAllowanceSequenceAsync(nextMonth));

        await secondTransaction.RollbackAsync();
        await firstTransaction.RollbackAsync();
    }

    [InvoiceAllowanceSqlFact]
    public async Task AnUnknownRefundResolvesToNullAgainstTheRealDatabase()
    {
        await using var context = InvoiceAllowanceSqlFixture.CreateContext();
        var reader = new InvoiceAllowanceReader(context);

        Assert.Null(await reader.FindByRefundAsync(Guid.NewGuid()));
    }

    [InvoiceAllowanceSqlFact]
    public async Task ThisFixtureNeverSharesADatabaseWithAnotherSuite()
    {
        // 這組會 EnsureDeleted。指到別組的資料庫就會把對方的測試資料刪掉，
        // 因此資料庫名稱必須固定，不隨環境變數改變。
        await using var context = InvoiceAllowanceSqlFixture.CreateContext();

        Assert.Equal("DoSelectAllowanceReaderTests", context.Database.GetDbConnection().Database);
    }
}

public sealed class InvoiceAllowanceReaderTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    [Fact]
    public void AddDoSelectInvoicing_ResolvesTheAllowanceServiceAndItsReader()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<IInvoiceAllowanceReader>();
        var service = scope.ServiceProvider.GetRequiredService<IssueInvoiceAllowanceService>();

        Assert.IsType<InvoiceAllowanceReader>(reader);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task FindByRefundAsync_ReturnsNullForAnEmptyPublicIdWithoutQuerying()
    {
        await using var context = CreateContext();
        var reader = new InvoiceAllowanceReader(context);

        Assert.Null(await reader.FindByRefundAsync(Guid.Empty));
    }

    [Fact]
    public async Task NextAllowanceSequenceAsync_RejectsNonUtcInput()
    {
        await using var context = CreateContext();
        var reader = new InvoiceAllowanceReader(context);

        await Assert.ThrowsAsync<ArgumentException>(() => reader.NextAllowanceSequenceAsync(
            new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void ReaderQueriesAreCoveredByTheDocumentedIndexes()
    {
        using var context = CreateContext();

        var allowance = context.Model.FindEntityType(typeof(SimulatedInvoiceAllowance))!;
        var invoice = context.Model.FindEntityType(typeof(SimulatedInvoice))!;
        var allocation = context.Model.FindEntityType(typeof(RefundAllocation))!;

        Assert.Contains(
            allowance.GetIndexes(),
            index => index.Properties.Any(p => p.Name == nameof(SimulatedInvoiceAllowance.RefundId)));
        Assert.Contains(
            invoice.GetIndexes(),
            index => index.Properties.Any(p => p.Name == nameof(SimulatedInvoice.OrderId)));
        Assert.Contains(
            allocation.GetIndexes(),
            index => index.Properties.Any(p => p.Name == nameof(RefundAllocation.RefundId)));
    }

    [Fact]
    public void TheAllowanceNumberIsGuardedByAUniqueIndex()
    {
        // 應用程式鎖是第一道防線，唯一索引是最後一道：鎖若失效，
        // 重複號碼仍必須在寫入時被資料庫擋下。
        using var context = CreateContext();
        var allowance = context.Model.FindEntityType(typeof(SimulatedInvoiceAllowance))!;

        Assert.Contains(
            allowance.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Any(p =>
                         p.Name == nameof(SimulatedInvoiceAllowance.AllowanceNumber)));
    }

    [Fact]
    public void TheReaderOnlyTouchesTablesThisModuleOwns()
    {
        // OrderItemId 只當對應鍵使用，不查 OrderItems，符合工程包「不得讀取其他模組底層表」。
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.Contains("_context.SimulatedInvoices", source, StringComparison.Ordinal);
        Assert.Contains("_context.RefundAllocations", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Orders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.OrderItems", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.ReturnItems", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Skus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReaderNeverDerivesAllowanceQuantityFromAnAmount()
    {
        // DEC-P286：折讓數量只能取自 RefundAllocations.Quantity，
        // 禁止以金額比例、固定值或 ReturnItems.Quantity 反推。
        // DES-21 已落地；這裡同時防止日後又加入任何比例或固定值推導。
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.DoesNotContain("DeriveQuantity", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemainingQuantity", source, StringComparison.Ordinal);
        Assert.Contains("RefundAllocations.Quantity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSequenceIsNotDerivedFromTheExistingRowCount()
    {
        // CountAsync + 1 在刪除後會重發已用過的號碼，而且沒有任何互斥。
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.DoesNotContain("CountAsync", source, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", source, StringComparison.Ordinal);
    }

    private static string ReaderSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src", "backend", "DoSelect.Infrastructure", "Invoicing", "InvoiceAllowanceReader.cs");
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
            .AddDoSelectInvoicing()
            .BuildServiceProvider();
    }

    private static DoSelectDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);
}
