using System.Data.Common;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DoSelect.Infrastructure.Tests.Catalog;

/// <summary>
/// 優惠券挑選器的目錄查詢，對真實 SQL Server provider 驗證。
/// </summary>
/// <remarks>
/// <para>
/// alex 2026-08-29 PR #64 D1 的驗收條件之一：<b>多筆輸入必須維持固定、有限的 SQL 往返，
/// 不得隨選取數量形成 N+1。</b>所以這裡用 <see cref="CommandCounter"/> 數實際命令數 ——
/// 只斷言「回傳內容正確」是不夠的，逐筆查也會回傳正確內容。
/// </para>
/// <para>
/// 整個類別共用一個資料庫（<see cref="IClassFixture{T}"/>）。每條測試各自用新的
/// <c>PublicId</c> 與新的 code，不能依賴「表是空的」—— 那不是斷言，是排程假設。
/// </para>
/// </remarks>
[Trait("Category", "RequiresSqlServer")]
public sealed class CouponCatalogOptionsReaderSqlServerTests
    : IClassFixture<CouponCatalogOptionsFixture>
{
    private readonly CouponCatalogOptionsFixture _fixture;

    public CouponCatalogOptionsReaderSqlServerTests(CouponCatalogOptionsFixture fixture) =>
        _fixture = fixture;

    [SqlServerFact]
    public async Task CategoriesComeBackInOneRoundTripWithTheirFullPath()
    {
        await using var context = _fixture.CreateContext(out var counter);
        var seeded = await SeedCategoryTreeAsync(context);
        var reader = new CouponCatalogOptionsReader(context);

        counter.Reset();
        var options = await reader.ListCategoriesAsync();

        // D1：分類一次批次取得，不得按樹節點逐一呼叫。
        Assert.Equal(1, counter.Count);

        var child = options.Single(option => option.PublicId == seeded.ChildPublicId);
        Assert.Equal($"{seeded.ParentName} / {seeded.ChildName}", child.Path);
    }

    [SqlServerFact]
    public async Task AnInactiveCategoryIsStillOfferedButMarked()
    {
        // C1：分類啟用與停用皆可選，但清單必須清楚標示狀態 ——
        // 不標示的話，管理員會把券綁在一個已經不對外的分類上而不自知。
        await using var context = _fixture.CreateContext(out _);
        var seeded = await SeedCategoryTreeAsync(context, childIsActive: false);
        var reader = new CouponCatalogOptionsReader(context);

        var options = await reader.ListCategoriesAsync();

        var child = options.Single(option => option.PublicId == seeded.ChildPublicId);
        Assert.False(child.IsActive);
    }

    [SqlServerFact]
    public async Task IsSelectableFollowsTheRulingForEveryProductStatus()
    {
        // C1：Draft／Published／Unpublished 可以新增選取（支援新品或重新上架前先排優惠）；
        // Discontinued 不可以。四種狀態全部列出來，之後 Domain 新增狀態時，
        // ToOption 的 switch 會丟 ArgumentOutOfRangeException 而不是預設成可選。
        (ProductStatus Status, bool Selectable)[] cases =
        [
            (ProductStatus.Draft, true),
            (ProductStatus.Published, true),
            (ProductStatus.Unpublished, true),
            (ProductStatus.Discontinued, false),
        ];

        await using var context = _fixture.CreateContext(out _);
        var reader = new CouponCatalogOptionsReader(context);

        foreach (var (status, selectable) in cases)
        {
            var product = await SeedProductAsync(context, status);

            var resolved = Assert.Single(await reader.ResolveProductsAsync([product.PublicId]));

            Assert.Equal(selectable, resolved.IsSelectable);
        }
    }

    [SqlServerFact]
    public async Task ADiscontinuedProductStillResolvesSoAnExistingRuleDoesNotVanish()
    {
        // C1：已存在於優惠券規則、之後才失效的參考，不得因 picker 查不到就靜默遺失。
        await using var context = _fixture.CreateContext(out _);
        var product = await SeedProductAsync(context, ProductStatus.Discontinued);
        var reader = new CouponCatalogOptionsReader(context);

        var resolved = Assert.Single(await reader.ResolveProductsAsync([product.PublicId]));

        Assert.Equal(ProductOptionStatus.Discontinued, resolved.Status);
        Assert.False(resolved.IsSelectable);
        Assert.Equal(product.Code, resolved.Code);
    }

    [SqlServerFact]
    public async Task ADiscontinuedProductNeverShowsUpInTheSearchResults()
    {
        // 搜尋結果是「可以加進來的東西」——停售商品能解析、但不能被搜出來新增。
        await using var context = _fixture.CreateContext(out _);
        var live = await SeedProductAsync(context, ProductStatus.Published);
        var dead = await SeedProductAsync(context, ProductStatus.Discontinued, live.Keyword);
        var reader = new CouponCatalogOptionsReader(context);

        var found = await reader.SearchProductsAsync(live.Keyword, pageSize: 20);

        Assert.Contains(found.Items, item => item.PublicId == live.PublicId);
        Assert.DoesNotContain(found.Items, item => item.PublicId == dead.PublicId);
    }

    [SqlServerFact]
    public async Task ResolvingManyProductsTakesTheSameOneRoundTripAsResolvingOne()
    {
        // D1 的核心驗收：往返次數不隨選取數量成長。
        await using var context = _fixture.CreateContext(out var counter);
        var products = new List<Guid>();
        for (var index = 0; index < 25; index++)
        {
            products.Add((await SeedProductAsync(context, ProductStatus.Published)).PublicId);
        }

        var reader = new CouponCatalogOptionsReader(context);

        counter.Reset();
        var one = await reader.ResolveProductsAsync([products[0]]);
        var oneCommand = counter.Count;

        counter.Reset();
        var many = await reader.ResolveProductsAsync(products);

        Assert.Single(one);
        Assert.Equal(25, many.Count);
        Assert.Equal(1, oneCommand);
        Assert.Equal(1, counter.Count);
    }

    [SqlServerFact]
    public async Task AnEmptyBatchDoesNotQueryAtAll()
    {
        await using var context = _fixture.CreateContext(out var counter);
        var reader = new CouponCatalogOptionsReader(context);

        counter.Reset();
        var resolved = await reader.ResolveProductsAsync([]);

        Assert.Empty(resolved);
        Assert.Equal(0, counter.Count);
    }

    [SqlServerFact]
    public async Task AnOversizedBatchIsRejectedRatherThanTruncated()
    {
        // 靜默截斷會讓被切掉的那幾筆看起來像「這個商品不存在」，
        // 而呼叫端正要用它來顯示既有規則。
        await using var context = _fixture.CreateContext(out _);
        var reader = new CouponCatalogOptionsReader(context);
        var tooMany = Enumerable
            .Range(0, CouponCatalogOptionRules.MaximumBatchSize + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => reader.ResolveProductsAsync(tooMany));
    }

    [SqlServerFact]
    public async Task ABatchExactlyAtTheLimitIsAccepted()
    {
        // 上限與優惠券規則的 200 筆一致；差一筆就存得進去卻讀不回來。
        await using var context = _fixture.CreateContext(out _);
        var reader = new CouponCatalogOptionsReader(context);
        var atLimit = Enumerable
            .Range(0, CouponCatalogOptionRules.MaximumBatchSize)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        Assert.Empty(await reader.ResolveProductsAsync(atLimit));
    }

    [SqlServerFact]
    public async Task TheSearchCapsItsOwnPageSizeAndSaysWhenThereIsMore()
    {
        await using var context = _fixture.CreateContext(out _);
        var keyword = $"CAP{Guid.NewGuid():N}"[..12];
        for (var index = 0; index < 4; index++)
        {
            await SeedProductAsync(context, ProductStatus.Published, keyword);
        }

        var reader = new CouponCatalogOptionsReader(context);

        var page = await reader.SearchProductsAsync(keyword, pageSize: 2);

        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
    }

    private sealed record SeededCategories(
        Guid ChildPublicId, string ParentName, string ChildName);

    private static async Task<SeededCategories> SeedCategoryTreeAsync(
        DoSelectDbContext context, bool childIsActive = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var parentName = $"父分類-{suffix}";
        var childName = $"子分類-{suffix}";

        var parent = new Category(
            Guid.CreateVersion7(), $"P-{suffix}", $"p-{suffix.ToLowerInvariant()}",
            parentName, null, DateTime.UtcNow);
        context.Categories.Add(parent);
        await context.SaveChangesAsync();

        var child = new Category(
            Guid.CreateVersion7(), $"C-{suffix}", $"c-{suffix.ToLowerInvariant()}",
            childName, parent.Id, DateTime.UtcNow);
        if (!childIsActive)
        {
            child.SetActive(false, DateTime.UtcNow);
        }

        context.Categories.Add(child);
        await context.SaveChangesAsync();

        return new SeededCategories(child.PublicId, parentName, childName);
    }

    private sealed record SeededProduct(Guid PublicId, string Code, string Keyword);

    private static async Task<SeededProduct> SeedProductAsync(
        DoSelectDbContext context, ProductStatus status, string? keyword = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var word = keyword ?? $"KW{suffix}";

        var brand = new Brand(Guid.CreateVersion7(), $"B-{suffix}", $"品牌-{suffix}", DateTime.UtcNow);
        var category = new Category(
            Guid.CreateVersion7(), $"CT-{suffix}", $"ct-{suffix.ToLowerInvariant()}",
            $"分類-{suffix}", null, DateTime.UtcNow);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var code = $"{word}-{suffix}";
        var product = new Product(
            Guid.CreateVersion7(), code, brand.Id, category.Id, $"{word} 商品", DateTime.UtcNow);

        // Draft 是初始狀態；其餘要走狀態機，Discontinued 必須先上架過。
        if (status is ProductStatus.Published or ProductStatus.Unpublished or ProductStatus.Discontinued)
        {
            product.ChangeStatus(ProductStatus.Published, DateTime.UtcNow);
        }

        if (status is ProductStatus.Unpublished or ProductStatus.Discontinued)
        {
            product.ChangeStatus(ProductStatus.Unpublished, DateTime.UtcNow);
        }

        if (status is ProductStatus.Discontinued)
        {
            product.ChangeStatus(ProductStatus.Discontinued, DateTime.UtcNow);
        }

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return new SeededProduct(product.PublicId, code, word);
    }
}

/// <summary>整個類別共用一個遷移過的資料庫。</summary>
/// <remarks>
/// 每條測試各建一個資料庫要跑一次 <c>MigrateAsync</c>，十幾條就是好幾十分鐘 ——
/// 代價是測試之間不再天然隔離，所以每筆種子都用新的 <c>PublicId</c> 與新的 code，
/// 不依賴「表是空的」。
/// </remarks>
public sealed class CouponCatalogOptionsFixture : IAsyncLifetime
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
            InitialCatalog = $"DoSelectCouponCatalogOptions_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var context = new DoSelectDbContext(Options(null));
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = new DoSelectDbContext(Options(null));
        await context.Database.EnsureDeletedAsync();
    }

    public DoSelectDbContext CreateContext(out CommandCounter counter)
    {
        counter = new CommandCounter();
        return new DoSelectDbContext(Options(counter));
    }

    private DbContextOptions<DoSelectDbContext> Options(CommandCounter? counter)
    {
        var builder = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(_connectionString);

        if (counter is not null)
        {
            builder.AddInterceptors(counter);
        }

        return builder.Options;
    }
}

/// <summary>數這個 DbContext 實際往資料庫送了幾個命令。</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => _count;

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
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
