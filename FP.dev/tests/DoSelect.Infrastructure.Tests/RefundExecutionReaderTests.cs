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
    /// B1 具名例外的守門測試：掃描整個 Refund Infrastructure，逐元件／資料表／**欄位**白名單。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 白名單來自 alex 於 2026-08-28 的 B1 正式裁定，以 <c>DEC-B1</c> 明列的欄位為上限。
    /// 未來要新增跨模組欄位必須重新 review 並同步更新這份清單與文件。
    /// </para>
    /// <para>
    /// 第一版只比對**資料表**，擋得住新表卻擋不住既有白名單表上的新欄位 ——
    /// 而 DEC-B1 寫的是「欄位為上限」。這一版把每個 lambda 參數綁到它查詢的資料表，
    /// 再檢查該參數存取的每一個成員。
    /// </para>
    /// <para>
    /// <b>查詢鍵也算欄位</b>：<c>Where</c>／<c>Join</c> 用到的 Id 與外鍵一樣要列進白名單。
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryRefundInfrastructureComponentStaysInsideItsNamedException()
    {
        var directory = RefundInfrastructureDirectory();
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);

            // 沒列進白名單的新元件一律失敗 —— 這正是舊版守門漏掉的情況。
            Assert.True(
                Allowed.ContainsKey(name),
                $"{name} 不在 B1 白名單裡。新增 Refund Infrastructure 元件必須先經過 " +
                "review、更新白名單與文件，不能自動取得跨模組存取。");

            var tables = Allowed[name];
            var source = File.ReadAllText(file);

            // DbContext 只能有一個入口，而且不得再指派給別的區域變數。
            // `var db = _context; db.Orders...` 會讓整段查詢連一個 table marker 都掃不到，
            // 白名單形同不存在。
            var contextFields = DbContextFieldNames(source);
            Assert.True(
                contextFields.Length <= 1,
                $"{name} 有多個 DoSelectDbContext 欄位（{string.Join("、", contextFields)}），" +
                "守門只能追一個入口，請收斂成一個。");

            foreach (var field in contextFields)
            {
                Assert.False(
                    Regex.IsMatch(source, $@"=\s*{Regex.Escape(field)}\s*[;)]"),
                    $"{name} 把 DbContext 指派給別的變數。守門靠 {field}. 前綴辨識資料表存取，" +
                    "換個名字之後整段查詢就掃不到了。");
            }

            foreach (var statement in ReadQueryStatements(source))
            {
                // 資料表比對與參數解析分開：即使一個參數都認不出來，
                // 用到白名單以外的表仍然要當場失敗。
                foreach (var table in statement.Tables)
                {
                    Assert.True(
                        tables.ContainsKey(table),
                        $"{name} 存取了白名單以外的資料表：{table}。" +
                        "B1 是具名窄範圍例外，不是跨模組通則。");
                }

                var restricted = statement.Tables
                    .Where(table => !tables[table].Contains("*", StringComparer.Ordinal))
                    .ToArray();

                if (restricted.Length == 0)
                {
                    // 全部是本模組自有的表（欄位不設限）。像
                    // `_context.RefundAllocations.Add(...)` 這種寫入本來就沒有參數可解析。
                    continue;
                }

                // **fail-closed，逐個參數。**
                //
                // 先前只要求整段「至少解析到一個欄位」，於是同一段裡只要有任何一個參數
                // 綁定成功，其他沒認出來的參數就被靜默忽略 —— 例如 Join 的
                // `(outer, inner) =>` result selector 裡的存取。
                Assert.True(
                    statement.UnboundAliases.Length == 0,
                    $"{name} 這段查詢有綁不到資料表的參數（" +
                    $"{string.Join("、", statement.UnboundAliases)}），" +
                    "因此無法確認它們存取的欄位是否在白名單內。" +
                    Environment.NewLine + statement.Text);

                Assert.True(
                    statement.Accesses.Length > 0,
                    $"{name} 這段查詢用到受限的資料表（{string.Join("、", restricted)}），" +
                    "但守門測試解析不出任何可檢查的參數。" +
                    Environment.NewLine + statement.Text);

                foreach (var (table, member, alias) in statement.Accesses)
                {
                    // "*" 代表本模組自有的表，欄位不設限。
                    Assert.True(
                        tables[table].Contains("*", StringComparer.Ordinal) ||
                        tables[table].Contains(member, StringComparer.Ordinal),
                        $"{name} 在 {table} 上存取了未核准的欄位：{member}" +
                        $"（來自 {alias}）。DEC-B1 以目前核准欄位為上限，" +
                        "新增欄位必須先重新 review 並更新 DEC-B1 與這份白名單。");
                }
            }
        }
    }

    /// <summary>
    /// B1 落地要求 5：不得使用可繞過白名單的替代存取形式。
    /// </summary>
    /// <remarks>
    /// <c>_context.Set&lt;T&gt;()</c> 能取得任意實體、<c>_context.Database</c> 與 Raw SQL
    /// 能直接下 SQL，三者都讓逐欄位白名單失效。第一版守門把 <c>Set</c> 與
    /// <c>Database</c> 放進「不是資料表」的排除清單，等於自己開了一個繞過口。
    /// </remarks>
    [Fact]
    public void NoRefundInfrastructureComponentBypassesTheWhitelist()
    {
        string[] bypasses = ["_context.Set<", "_context.Database", "FromSql", "ExecuteSql"];

        foreach (var file in Directory.GetFiles(
            RefundInfrastructureDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (var bypass in bypasses)
            {
                Assert.False(
                    source.Contains(bypass, StringComparison.Ordinal),
                    $"{name} 使用了 {bypass}，那會繞過 B1 的逐欄位白名單。");
            }
        }
    }

    /// <summary>
    /// B1 落地要求 2：Gateway／Reader 不得自行開啟或提交交易。
    /// </summary>
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

    /// <summary>
    /// 逐元件／資料表／欄位白名單，對應 <c>DEC-B1</c> 的欄位表。
    /// </summary>
    /// <remarks>
    /// 本模組自有的表（<c>Refunds</c>、<c>RefundAllocations</c>、<c>PaymentAttempts</c>）
    /// 不在跨模組例外範圍內，欄位以 <c>*</c> 表示不設限。
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>>
        Allowed = new Dictionary<string, IReadOnlyDictionary<string, string[]>>(StringComparer.Ordinal)
        {
            ["RefundExecutionReader.cs"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Refunds"] = ["*"],
                ["PaymentAttempts"] = ["*"],
            },

            // B1-1：退款計算所需的可信快照。
            ["RefundTrustedInputsReader.cs"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Refunds"] = ["*"],
                ["RefundAllocations"] = ["*"],
                ["ReturnRequests"] =
                    ["Id", "ReasonCode", "AssemblyFeeDisposition", "ReturnShippingCost"],
                ["ReturnItems"] = ["ReturnRequestId", "OrderItemId", "Quantity"],
                ["Orders"] =
                [
                    "Id", "ShippingFee", "AssemblyFee",
                    "ShippingFreeThresholdSnapshot", "ShippingMethodBaseFeeSnapshot",
                ],
                ["OrderItems"] =
                [
                    "Id", "OrderId", "PublicId",
                    "Quantity", "FinalUnitPrice", "DiscountAllocation", "IsCouponEligible",
                ],
                ["OrderCoupons"] =
                    ["OrderId", "AppliedAmount", "EligibleSubtotal", "MinimumSpendAmount"],
            },

            // B1-2：分攤寫入解析 OrderItem 內部主鍵。
            // B1-4：管理員身分／授權／Audit Actor。
            ["RefundExecutor.cs"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Refunds"] = ["*"],
                ["RefundAllocations"] = ["*"],
                ["PaymentAttempts"] = ["*"],
                ["OrderItems"] = ["Id", "OrderId", "PublicId"],
                ["Users"] = ["Id", "PublicId", "AccountType", "AccountStatus"],
                ["AdminProfiles"] = ["UserId", "IsActive"],
                ["UserRoles"] = ["UserId", "RoleId"],
                ["Roles"] = ["Id", "Name"],
            },

            // B1-3：正式 RefundDto 的唯讀投影。
            ["RefundReader.cs"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Refunds"] = ["*"],
                ["RefundAllocations"] = ["*"],
                ["Orders"] = ["Id", "PublicId"],
                ["ReturnRequests"] = ["Id", "PublicId"],
                ["OrderItems"] = ["Id", "PublicId"],
                ["Users"] = ["Id", "PublicId", "Email"],
            },

            // 只做 DI 註冊，不碰任何表。
            ["RefundsServiceCollectionExtensions.cs"] =
                new Dictionary<string, string[]>(StringComparer.Ordinal),
        };

    /// <summary>
    /// 一個陳述式裡用到的資料表、解析出來的欄位存取、**沒能綁定的參數**，以及原文。
    /// </summary>
    private sealed record QueryStatement(
        string[] Tables,
        (string Table, string Member, string Alias)[] Accesses,
        string[] UnboundAliases,
        string Text);

    /// <summary>
    /// 找出這個檔案裡 <c>DoSelectDbContext</c> 欄位的名稱。
    /// </summary>
    /// <remarks>
    /// 刻意**不寫死 <c>_context</c>**：把欄位改名成 <c>_dbContext</c> 就能讓整個檔案
    /// 離開守門範圍，那是一個只要改個名字就能繞過的洞。改成依型別找欄位，
    /// 名稱怎麼取都躲不掉。
    /// </remarks>
    private static string[] DbContextFieldNames(string source) =>
        Regex.Matches(source, @"DoSelectDbContext\s+(_[A-Za-z0-9_]+)\s*[;=)]")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 以陳述式為單位，找出用到哪些資料表、每個參數存取的成員，以及綁不出表的參數。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 支援三種寫法，任何一種沒認出來就等於不受白名單管：
    /// </para>
    /// <list type="bullet">
    /// <item>method syntax 單參數：<c>.Where(candidate =&gt; candidate.Id == id)</c></item>
    /// <item>method syntax 多參數：<c>.Join(..., (item, orderItem) =&gt; ...)</c></item>
    /// <item>query syntax：<c>from alias in ctx.Table</c>、<c>join alias in ctx.Table</c></item>
    /// </list>
    /// <para>
    /// 參數綁到它**第一次被宣告**時、位置在它之前的最後一張表。query syntax 的
    /// <c>from</c>／<c>join</c> 把參數寫在表**前面**，所以單獨認。
    /// </para>
    /// <para>
    /// 多參數 lambda（例如 Join 的 result selector）用同一條規則綁定，因此可能把
    /// outer 的參數也綁到 inner 那張表。那個方向是**保守**的：會多擋、不會少擋，
    /// 而且把參數命名成前面已宣告過的名字就會回到正確的綁定。
    /// </para>
    /// </remarks>
    private static IEnumerable<QueryStatement> ReadQueryStatements(string source)
    {
        var contextFields = DbContextFieldNames(source);
        if (contextFields.Length == 0)
        {
            yield break;
        }

        var contextPattern =
            "(?:" + string.Join("|", contextFields.Select(Regex.Escape)) + @")\.([A-Za-z]+)";

        foreach (var statement in source.Split(';'))
        {
            var markers = Regex.Matches(statement, contextPattern)
                .Where(match => !NotTables.Contains(match.Groups[1].Value, StringComparer.Ordinal))
                .ToArray();

            if (markers.Length == 0)
            {
                continue;
            }

            var boundTable = new Dictionary<string, string>(StringComparer.Ordinal);

            // query syntax：`from alias in ctx.Table`、`join alias in ctx.Table`。
            foreach (Match clause in Regex.Matches(
                statement,
                @"\b(?:from|join)\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+" + contextPattern))
            {
                boundTable.TryAdd(clause.Groups[1].Value, clause.Groups[2].Value);
            }

            // method syntax：單參數 `x =>` 與多參數 `(x, y) =>` 都要認。
            // 少認多參數這一種，result selector 裡的存取就完全不會被檢查。
            var declarations = new List<(string Alias, int Index)>();
            foreach (Match single in Regex.Matches(
                statement, @"(?<![\w.])([A-Za-z_][A-Za-z0-9_]*)\s*=>"))
            {
                declarations.Add((single.Groups[1].Value, single.Index));
            }

            foreach (Match tuple in Regex.Matches(
                statement, @"\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\s*,\s*[A-Za-z_][A-Za-z0-9_]*)+)\s*\)\s*=>"))
            {
                foreach (var alias in tuple.Groups[1].Value.Split(','))
                {
                    declarations.Add((alias.Trim(), tuple.Index));
                }
            }

            foreach (var (alias, index) in declarations.OrderBy(entry => entry.Index))
            {
                if (boundTable.ContainsKey(alias))
                {
                    continue;
                }

                var owner = markers
                    .Where(marker => marker.Index < index)
                    .OrderBy(marker => marker.Index)
                    .LastOrDefault();

                if (owner is not null)
                {
                    boundTable[alias] = owner.Groups[1].Value;
                }
            }

            var accesses = new List<(string Table, string Member, string Alias)>();
            foreach (var (alias, table) in boundTable)
            {
                foreach (Match access in Regex.Matches(
                    statement, $@"\b{Regex.Escape(alias)}\.([A-Za-z_][A-Za-z0-9_]*)"))
                {
                    var member = access.Groups[1].Value;
                    if (!LinqMembers.Contains(member, StringComparer.Ordinal))
                    {
                        accesses.Add((table, member, alias));
                    }
                }
            }

            // 綁不出表的參數要單獨回報。先前只看「整段有沒有解析到任何欄位」，
            // 於是同一段裡有一個參數解析成功，其他沒認出來的參數就被忽略了。
            var unbound = declarations
                .Select(entry => entry.Alias)
                .Where(alias => !boundTable.ContainsKey(alias))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            yield return new QueryStatement(
                markers.Select(marker => marker.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray(),
                accesses.ToArray(),
                unbound,
                statement.Trim());
        }
    }

    /// <summary>
    /// <c>_context</c> 上不是 DbSet 的成員。
    /// </summary>
    /// <remarks>
    /// <c>Set</c> 與 <c>Database</c> **刻意不列在這裡** —— 把它們當成「不是資料表」
    /// 而略過，正是第一版守門留下的繞過口。它們由
    /// <see cref="NoRefundInfrastructureComponentBypassesTheWhitelist"/> 直接拒絕。
    /// </remarks>
    private static readonly string[] NotTables =
        ["ChangeTracker", "Entry", "SaveChangesAsync", "SaveChanges"];

    /// <summary>lambda 上呼叫的 LINQ／框架成員，不是資料表欄位。</summary>
    private static readonly string[] LinqMembers =
    [
        "Contains", "Sum", "Count", "Any", "All", "Select", "Where", "Key",
        "GetValueOrDefault", "ToString", "Value", "HasValue", "Equals", "Length",
    ];

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
