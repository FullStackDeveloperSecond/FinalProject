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

    private static IReadOnlyList<string> Analyze(
        string body,
        bool useSemanticModel = true,
        IReadOnlyCollection<string>? approvedComponentTypeNames = null,
        string header = "") =>
        RefundInfrastructureGuard.Violations(
            "Synthetic.cs",
            $$"""
              using DoSelect.Infrastructure.Persistence;
              using Microsoft.EntityFrameworkCore;
              {{header}}

              namespace Synthetic;

              internal static class SneakyHelper
              {
                  public static void ThrowIfNull(object? candidate) { }
              }

              internal sealed class SneakyReader
              {
                  public SneakyReader(DoSelectDbContext context) { }
              }

              internal sealed class Probe
              {
              {{body}}
              }
              """,
            Allowed,
            approvedComponentTypeNames,
            useSemanticModel);

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
    public void CatchesAnUnapprovedFieldReachedThroughThis()
    {
        // alex 2026-08-29 第三輪：上一版要求接收者是裸識別字，所以 this._context
        // 完全不會進入資料表檢查；而欄位宣告仍在，「找不到入口」的 fail-closed
        // 也不會觸發 —— 結果是回報零違規。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => this._context.Orders.Select(order => order.GrandTotal);
            """);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesSetReachedThroughThis()
    {
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => this._context.Set<object>();
            """);

        // 斷言的是**bypass 規則**抓到它，不只是「訊息裡有 Set 這三個字」——
        // 後者連 fail-closed 那道網子的訊息（含原文 this._context.Set）都會通過，
        // 等於分不出是哪一條規則生效。
        Assert.Contains(
            violations,
            violation => violation.Contains("繞過 B1 的逐欄位白名單", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesAnUnapprovedFieldThroughALocalAliasOfThisContext()
    {
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run()
                {
                    var db = this._context;
                    return db.Orders.Select(order => order.GrandTotal);
                }
            """);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesAnUnapprovedFieldThroughAParenthesisedOrCastReceiver()
    {
        // 型別對就算，寫法不重要 —— 這正是換成語意判斷要買到的東西。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => ((DoSelectDbContext)_context).Orders
                    .Select(order => order.GrandTotal);
            """);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void FailsClosedOnAContextReceiverItCannotClassify()
    {
        // 語意與語法都認不出來的接收者形狀，一律當成違規 —— 沒有這道網子，
        // 只要想出一種兩邊都不認得的寫法，整段查詢就會靜默離開白名單。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                private static object Pick(object candidate) => candidate;

                public object Run() => Pick(_context).ToString();
            """);

        Assert.Contains(
            violations,
            violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatchesAThisReceiverWithOrWithoutTheSemanticModel(bool useSemanticModel)
    {
        // 語意解析失敗時會退回語法正規化。那條路徑平常摸不到，所以這裡強制關掉
        // 語意模型再跑一次 —— 否則備援等於沒被測過，真的需要它的時候才會發現壞了。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => this._context.Orders.Select(order => order.GrandTotal);
            """,
            useSemanticModel);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatchesALocalAliasWithOrWithoutTheSemanticModel(bool useSemanticModel)
    {
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run()
                {
                    var db = this._context;
                    return db.Orders.Select(order => order.GrandTotal);
                }
            """,
            useSemanticModel);

        Assert.Contains(violations, violation => violation.Contains("GrandTotal", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesTheContextBeingHandedToAnUnknownHelper()
    {
        // alex 2026-08-29 第四輪：上一版的網子只巡 MemberAccess 並看 receiver，
        // 但這裡的 member access 是 ExternalHelper.Read，_context 在引數裡。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public void Run() => ExternalHelper.Read(_context);
            """);

        Assert.Contains(violations, violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesTheContextBeingStoredOnAnUnknownObject()
    {
        // 這裡的 member access receiver 是 holder，_context 在指派右手邊。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public void Run(Holder holder) => holder.Context = _context;

                public sealed class Holder
                {
                    public DoSelectDbContext? Context { get; set; }
                }
            """);

        Assert.Contains(violations, violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesTheContextBeingReturnedOutOfTheComponent()
    {
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public DoSelectDbContext Run() => _context;
            """);

        Assert.Contains(violations, violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowsTheContextToBeHandedToAWhitelistedRefundComponent()
    {
        // 交給白名單內的元件是允許的：那個元件有自己的資料表與欄位清單，
        // 會在它自己的檔案裡被檢查。這一條確認上面三條不是把所有傳遞都擋掉。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => new RefundReader(_context);

                public sealed class RefundReader
                {
                    public RefundReader(DoSelectDbContext context) => Context = context;

                    public DoSelectDbContext Context { get; }
                }
            """,
            // 完整型別名稱：合成程式碼包在 namespace Synthetic 的 class Probe 裡。
            approvedComponentTypeNames: ["Synthetic.Probe.RefundReader"]);

        Assert.DoesNotContain(
            violations,
            violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowsTheConstructorNullGuardAndFieldAssignment()
    {
        // 每個 Reader 的建構子都長這樣；擋掉它等於守門對所有真實檔案變紅。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context;

                public Probe(DoSelectDbContext context)
                {
                    ArgumentNullException.ThrowIfNull(context);
                    _context = context;
                }
            """);

        Assert.Empty(violations);
    }

    [Fact]
    public void CatchesAnAliasPretendingToBeAWhitelistedComponent()
    {
        // alex 2026-08-29 第五輪：核准清單原本比對原始碼上的簡單名稱，
        // 所以一個 using alias 就能讓白名單外的型別冒充核准元件 ——
        // 拼字完全正確，指的卻是別人。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public object Run() => new RefundReader(_context);
            """,
            approvedComponentTypeNames: ["Synthetic.Probe.RefundReader"],
            header: "using RefundReader = Synthetic.SneakyReader;");

        Assert.Contains(violations, violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchesAnAliasPretendingToBeTheNullGuard()
    {
        // 同一招用在 null guard 上：ArgumentNullException.ThrowIfNull 的拼字對，
        // 但那是別的型別的方法，可以拿 context 去做任何事。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public void Run() => ArgumentNullException.ThrowIfNull(_context);
            """,
            header: "using ArgumentNullException = Synthetic.SneakyHelper;");

        Assert.Contains(violations, violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
    }

    [Fact]
    public void StillAllowsTheRealNullGuard()
    {
        // 正向對照：沒有 alias 時，真正的 System.ArgumentNullException 仍然放行。
        var violations = Analyze(
            """
                private readonly DoSelectDbContext _context = null!;

                public void Run() => ArgumentNullException.ThrowIfNull(_context);
            """);

        Assert.DoesNotContain(
            violations,
            violation => violation.Contains("逃出守門範圍", StringComparison.Ordinal));
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

        // 這裡驗的性質是「這個檔案不會乾淨地通過」，不是哪一條規則先攔到它：
        // 語意模型解得出 context 的型別時是逃逸分析攔下 return，解不出來時
        // 才輪到「找不到入口」那一條。兩條都是 fail-closed，指定其中一條反而
        // 會在另一條生效時假性變紅。
        Assert.NotEmpty(violations);
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
