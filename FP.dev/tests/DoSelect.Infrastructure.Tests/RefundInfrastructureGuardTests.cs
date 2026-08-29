namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// 反向驗證 <see cref="RefundInfrastructureGuard"/>：用合成程式碼證明它真的擋得住。
/// </summary>
/// <remarks>
/// <para>
/// 守門測試對真實檔案是綠的，只代表「目前的程式碼沒踩線」，不代表「踩線會被抓到」。
/// alex 在 PR #16 2026-08-29 review 第 4 點要求補這一組 —— 前幾輪的 regex 守門正是
/// 一路綠燈，直到他自己找出四種繞得過去的合法寫法。
/// </para>
/// <para>
/// 每個案例都用 <c>Orders.GrandTotal</c>：<c>Orders</c> 在白名單裡，但 <c>GrandTotal</c>
/// 不在，所以只要分析器「看得到」這個存取就一定會回報。看不到才是 bug。
/// </para>
/// </remarks>
public sealed class RefundInfrastructureGuardTests
{
    /// <summary>與 <c>RefundTrustedInputsReader</c> 相同形狀的窄白名單。</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Allowed =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Refunds"] = ["*"],
            ["Orders"] = ["Id", "ShippingFee"],
            ["OrderItems"] = ["Id", "OrderId"],
        };

    private static IReadOnlyList<string> Analyze(string body) =>
        RefundInfrastructureGuard.Violations(
            "Synthetic.cs",
            $$"""
              using DoSelect.Infrastructure.Persistence;
              using Microsoft.EntityFrameworkCore;

              namespace Synthetic;

              internal sealed class Probe
              {
              {{body}}
              }
              """,
            Allowed);

    [Fact]
    public void AllowsAnApprovedFieldOnAnApprovedTable()
    {
        // 先確認這組合成程式碼本身是乾淨的 —— 否則下面每一條都可能是為了別的理由變紅。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => _context.Orders.Select(order => order.ShippingFee);
            """);

        Assert.Empty(violations);
    }

    [Fact]
    public void CatchesAnUnapprovedFieldOnAContextFieldWithoutAnUnderscore()
    {
        // alex 2026-08-29 第 1 點：舊 regex 只收 `_` 開頭的欄位名，所以這個合法宣告
        // 會得到零個 DbContext 入口，整個檔案靜默跳過資料表與欄位檢查。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext context = null!;

                public object Run() => context.Orders.Select(order => order.GrandTotal);
            """);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesSetOnAContextFieldThatIsNotCalledContext()
    {
        // alex 2026-08-29 第 2 點：舊禁止清單寫死 `_context.Set<`，改個欄位名就繞過。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _dbContext = null!;

                public object Run() => _dbContext.Set<object>();
            """);

        Assert.Contains(violations, violation => violation.Contains("_dbContext.Set", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesDatabaseOnAContextFieldThatIsNotCalledContext()
    {
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _dbContext = null!;

                public object Run() => _dbContext.Database;
            """);

        Assert.Contains(
            violations,
            violation => violation.Contains("_dbContext.Database", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesAnUnapprovedFieldInsideATypedMultiParameterLambda()
    {
        // alex 2026-08-29 第 3 點：有寫型別的多參數 lambda。語法樹拿的是參數識別字，
        // 跟型別註記無關，所以這種寫法跟沒寫型別的一樣認得。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() =>
                    _context.OrderItems.Join(
                        _context.Orders,
                        (OrderItem item) => item.OrderId,
                        (Order order) => order.Id,
                        (OrderItem item, Order order) => order.GrandTotal);
            """);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesAnUnapprovedFieldReadThroughEfProperty()
    {
        // alex 2026-08-29 第 3 點：EF.Property 是合法的 shadow property 存取，
        // 語法上根本不是成員存取，regex 看不到它。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() =>
                    _context.Orders.Select(order => EF.Property<decimal>(order, "GrandTotal"));
            """);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void FailsClosedOnAnEfPropertyItCannotResolve()
    {
        // 欄位名不是字面值就解析不出來 —— 這種情況必須失敗，不能靜默略過。
        var violations = Analyze(
            """
                private const string Field = "GrandTotal";

                private readonly DoSelectDbContext _context = null!;

                public object Run() =>
                    _context.Orders.Select(order => EF.Property<decimal>(order, Field));
            """);

        Assert.Contains(violations, violation => violation.Contains("EF.Property", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesAnUnapprovedFieldReachedThroughALocalAliasOfTheContext()
    {
        // `var db = _context;` 之後整段查詢都掛在 db 上。舊版是「偵測到再指派就失敗」，
        // 這版改成把別名一起追蹤，查詢照樣受檢。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run()
                {
                    var db = _context;
                    return db.Orders.Select(order => order.GrandTotal);
                }
            """);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesATableThatIsNotOnTheWhitelistAtAll()
    {
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => _context.Skus.Select(sku => sku.Id);
            """);

        Assert.Contains(violations, violation => violation.Contains("Skus", StringComparison.Ordinal));
    }

    [Fact]
    public void FailsClosedOnAMultiParameterLambdaItCannotBind()
    {
        // 不是 Join 的多參數 lambda 綁不出來源表。認不出來就必須失敗 ——
        // 靜默略過的話，裡面存取什麼欄位都沒人知道。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() =>
                    _context.Orders.Zip(_context.OrderItems, (left, right) => left.GrandTotal);
            """);

        Assert.Contains(violations, violation => violation.Contains("綁不到資料表", StringComparison.Ordinal));
    }

    [Fact]
    public void FailsClosedWhenItCannotFindAnyContextEntryPoint()
    {
        // 分析器看不懂這個檔案怎麼取得 DbContext —— 不能當成「沒有存取資料庫」。
        var violations = Analyze(
            """
                public object Run(object source)
                {
                    var context = (DoSelectDbContext)source;
                    return context;
                }
            """);

        Assert.Contains(
            violations,
            violation => violation.Contains("辨識不出任何 DbContext 入口", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesRawSqlOnAnyReceiver()
    {
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _dbContext = null!;

                public object Run() => _dbContext.Orders.FromSqlRaw("select * from Orders");
            """);

        Assert.Contains(violations, violation => violation.Contains("FromSqlRaw", StringComparison.Ordinal));
    }
}
