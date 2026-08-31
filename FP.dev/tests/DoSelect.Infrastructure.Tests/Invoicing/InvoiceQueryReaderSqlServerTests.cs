using System.Data.Common;
using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Invoicing;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DoSelect.Infrastructure.Tests.Invoicing;

/// <summary>
/// 發票讀取埠，對真實 SQL Server provider 驗證。
/// </summary>
/// <remarks>
/// 重點有二：<b>不碰 Orders／OrderItems</b>（Issue #65 A1），以及<b>往返次數固定</b> ——
/// 一頁上有幾張發票都是「發票一次、明細一次、折讓一次、折讓明細一次」。
/// </remarks>
[Trait("Category", "RequiresSqlServer")]
public sealed class InvoiceQueryReaderSqlServerTests : IClassFixture<InvoiceQueryReaderFixture>
{
    private static readonly DateTime NowUtc = new(2026, 8, 30, 4, 0, 0, DateTimeKind.Utc);

    private readonly InvoiceQueryReaderFixture _fixture;

    public InvoiceQueryReaderSqlServerTests(InvoiceQueryReaderFixture fixture) => _fixture = fixture;

    [SqlServerFact]
    public async Task FindByOrderCarriesTheHeaderAndItems()
    {
        await using var context = _fixture.CreateContext(out _);
        var seeded = await SeedInvoiceAsync(context, itemCount: 2);
        var reader = new InvoiceQueryReader(context);

        var row = await reader.FindByOrderAsync(seeded.OrderId);

        Assert.Equal(seeded.InvoicePublicId, row!.PublicId);
        Assert.Equal(SimulatedInvoiceStatus.Issued, row.Status);
        Assert.Equal(2, row.Items.Count);
        Assert.Equal(1000m, row.GrossAmount);
    }

    [SqlServerFact]
    public async Task FindByOrderReturnsNullForAnOrderWithNoInvoice()
    {
        await using var context = _fixture.CreateContext(out _);
        var reader = new InvoiceQueryReader(context);

        Assert.Null(await reader.FindByOrderAsync(987_654L));
        Assert.Null(await reader.FindByOrderAsync(0L));
    }

    [SqlServerFact]
    public async Task FindByPublicIdReturnsNullForAnUnknownInvoice()
    {
        await using var context = _fixture.CreateContext(out _);
        var reader = new InvoiceQueryReader(context);

        Assert.Null(await reader.FindAsync(Guid.NewGuid()));
        Assert.Null(await reader.FindAsync(Guid.Empty));
    }

    [SqlServerFact]
    public async Task NonMerchandiseLinesComeBackWithTheirKind()
    {
        // 發票明細沒有持久化種類欄位，是靠 SkuCodeSnapshot 的保留值識別（DEC-P299）。
        await using var context = _fixture.CreateContext(out _);
        var seeded = await SeedInvoiceAsync(context, itemCount: 1, withShipping: true);
        var reader = new InvoiceQueryReader(context);

        var row = await reader.FindAsync(seeded.InvoicePublicId);

        Assert.Contains(row!.Items, item => item.Kind == InvoiceLineKind.Merchandise);
        Assert.Contains(row.Items, item => item.Kind == InvoiceLineKind.Shipping);
    }

    [SqlServerFact]
    public async Task APageOfInvoicesTakesTheSameRoundTripsAsASingleOne()
    {
        // Issue #65 驗收條件：後台摘要不得形成 N+1。
        // 一張與五張的往返次數必須一樣，否則列數一多就會退化。
        await using var context = _fixture.CreateContext(out var counter);
        var query = new AdminInvoiceQuery(null, null, null, null, 1, 20);
        var reader = new InvoiceQueryReader(context);

        var single = await SeedInvoiceAsync(context, itemCount: 1);
        counter.Reset();
        await reader.ListAsync(query with { Q = single.InvoiceNumber });
        var oneInvoice = counter.Count;

        var keyword = $"DEMO-2099{Guid.NewGuid():N}"[..14];
        for (var index = 0; index < 5; index++)
        {
            await SeedInvoiceAsync(context, itemCount: 2, invoiceNumberPrefix: keyword);
        }

        counter.Reset();
        var page = await reader.ListAsync(query with { Q = keyword });

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(oneInvoice, counter.Count);
    }

    [SqlServerFact]
    public async Task TheListFiltersByStatus()
    {
        await using var context = _fixture.CreateContext(out _);
        var keyword = $"DEMO-2098{Guid.NewGuid():N}"[..14];
        var issued = await SeedInvoiceAsync(context, itemCount: 1, invoiceNumberPrefix: keyword);
        await SeedInvoiceAsync(context, itemCount: 1, invoiceNumberPrefix: keyword, voided: true);
        var reader = new InvoiceQueryReader(context);

        var page = await reader.ListAsync(
            new AdminInvoiceQuery([SimulatedInvoiceStatus.Issued], null, null, keyword, 1, 20));

        Assert.Equal(issued.InvoicePublicId, Assert.Single(page.Items).PublicId);
    }

    [SqlServerFact]
    public async Task AnEmptyStatusFilterMeansTheSameAsNoFilter()
    {
        // 全站的後台清單端點都是這個語意（EfAdminCouponService、ReturnStore、
        // SupportTicketStore）。這條測試把它釘住，免得日後有人單獨改掉 Invoicing。
        await using var context = _fixture.CreateContext(out _);
        var keyword = $"DEMO-2097{Guid.NewGuid():N}"[..14];
        await SeedInvoiceAsync(context, itemCount: 1, invoiceNumberPrefix: keyword);
        var reader = new InvoiceQueryReader(context);

        var withNone = await reader.ListAsync(new AdminInvoiceQuery([], null, null, keyword, 1, 20));
        var withNull = await reader.ListAsync(new AdminInvoiceQuery(null, null, null, keyword, 1, 20));

        // 先確定這個 keyword 真的查得到東西，否則兩邊都是零筆也會「相等」。
        Assert.Equal(1, withNull.TotalCount);
        Assert.Equal(withNull.TotalCount, withNone.TotalCount);
        Assert.Equal(withNull.Items[0].PublicId, withNone.Items[0].PublicId);
    }

    [SqlServerFact]
    public async Task TheLargestLegalPageNumberComesBackEmptyInsteadOfOverflowing()
    {
        // pageNumber 到 int.MaxValue 時 (page - 1) * size 用 int 會溢位成負 offset。
        await using var context = _fixture.CreateContext(out _);
        var keyword = $"DEMO-2096{Guid.NewGuid():N}"[..14];
        await SeedInvoiceAsync(context, itemCount: 1, invoiceNumberPrefix: keyword);
        var reader = new InvoiceQueryReader(context);

        var page = await reader.ListAsync(
            new AdminInvoiceQuery(null, null, null, keyword, int.MaxValue, 50));

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    private sealed record SeededInvoice(long OrderId, Guid InvoicePublicId, string InvoiceNumber);

    /// <summary>種一張已付款的訂單，只為了讓發票的外鍵成立。</summary>
    /// <summary>種一張已付款的訂單，只為了讓發票的外鍵成立。</summary>
    /// <remarks>
    /// SimulatedInvoices.OrderId 對 Orders 有外鍵。外鍵存在正是「窄內部 Key 例外」的理由，
    /// 它不代表 Reader 可以去讀 Orders —— Reader 的查詢裡一次都沒有出現 Orders 或 OrderItems，
    /// 訂單只是為了讓 INSERT 過得去。
    /// </remarks>
    private static async Task<long> SeedOrderAsync(DoSelectDbContext context)
    {
        var profile = new ShippingProviderProfile(
            Guid.NewGuid(),
            $"INV{Guid.NewGuid():N}"[..16],
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            NowUtc);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.NewGuid(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m, null, null, NowUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"INV-{Guid.NewGuid():N}"[..32],
                null,
                "guest@example.test",
                OrderStatus.Confirmed,
                PaymentStatus.Paid,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                1000m,
                0m,
                0m,
                0m,
                1000m,
                "[[SYNTHETIC_NAME]]",
                "0912345678",
                "recipient@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "[[SYNTHETIC_ADDRESS]]",
                null,
                "HOME",
                profile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                null,
                $"inv-{Guid.NewGuid():N}",
                null,
                1,
                1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    "buyer@example.test",
                    null,
                    null,
                    null,
                    null),
                1_000m,
                null,
                new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m)),
            NowUtc);
        order.ApplyPaymentProjection(PaymentStatus.Paid, 1000m, NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order.Id;
    }


    private static async Task<SeededInvoice> SeedInvoiceAsync(
        DoSelectDbContext context,
        int itemCount,
        bool withShipping = false,
        bool voided = false,
        string? invoiceNumberPrefix = null)
    {
        // SimulatedInvoices.OrderId 對 Orders 有外鍵，所以要先種一張訂單。
        // 外鍵存在正是「窄內部 Key 例外」的理由；它不代表 Reader 可以去讀 Orders ——
        // 這個 Reader 的查詢裡一次都沒有出現 Orders 或 OrderItems。
        var orderId = await SeedOrderAsync(context);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var invoiceNumber = $"{invoiceNumberPrefix ?? "DEMO-202608"}-{suffix}";

        var invoice = new SimulatedInvoice(
            Guid.CreateVersion7(),
            new SimulatedInvoiceCreation(
                orderId,
                invoiceNumber,
                SimulatedInvoiceBuyerType.Individual,
                "buyer@example.test",
                null,
                null,
                null,
                null,
                952m,
                48m,
                1000m),
            NowUtc);
        invoice.Issue(NowUtc);
        if (voided)
        {
            invoice.Void(NowUtc.AddHours(1));
        }

        context.SimulatedInvoices.Add(invoice);
        await context.SaveChangesAsync();

        for (var index = 0; index < itemCount; index++)
        {
            context.SimulatedInvoiceItems.Add(new SimulatedInvoiceItem(
                Guid.CreateVersion7(), invoice.Id, null,
                $"測試商品 {index}", $"SKU-{suffix}-{index}", 1, 1000m, 0m, 952m, 48m, 1000m, NowUtc));
        }

        if (withShipping)
        {
            context.SimulatedInvoiceItems.Add(new SimulatedInvoiceItem(
                Guid.CreateVersion7(), invoice.Id, null,
                "運費", InvoiceLineSkuCodes.Shipping, 1, 60m, 0m, 57m, 3m, 60m, NowUtc));
        }

        await context.SaveChangesAsync();
        return new SeededInvoice(orderId, invoice.PublicId, invoiceNumber);
    }
}

/// <summary>整個類別共用一個遷移過的資料庫。</summary>
/// <remarks>
/// 每條測試各建一個資料庫要跑一次 <c>MigrateAsync</c>，代價是分鐘等級。
/// 共用的代價是測試之間不再天然隔離，所以每筆種子都用新的 <c>OrderId</c>
/// 與新的發票號碼前綴，不依賴「表是空的」—— 那不是斷言，是排程假設。
/// </remarks>
public sealed class InvoiceQueryReaderFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = $"DoSelectInvoiceQuery_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var context = new DoSelectDbContext(Options(null));
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = new DoSelectDbContext(Options(null));
        await context.Database.EnsureDeletedAsync();
    }

    public DoSelectDbContext CreateContext(out InvoiceCommandCounter counter)
    {
        counter = new InvoiceCommandCounter();
        return new DoSelectDbContext(Options(counter));
    }

    private DbContextOptions<DoSelectDbContext> Options(InvoiceCommandCounter? counter)
    {
        var builder = new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(_connectionString);
        if (counter is not null)
        {
            builder.AddInterceptors(counter);
        }

        return builder.Options;
    }
}

/// <summary>數這個 DbContext 實際往資料庫送了幾個命令。</summary>
public sealed class InvoiceCommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => _count;

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
