using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// <c>DEC-B1</c> 具名例外的靜態守門：檢查一個 Refund Infrastructure 檔案有沒有踩出
/// 白名單，回傳所有違規訊息（空集合代表通過）。
/// </summary>
/// <remarks>
/// <para>
/// <b>為什麼從 regex 換成語法樹。</b>alex 在 PR #16 2026-08-29 的 review 指出，
/// regex 版本每輪只補一種例外，而合法的 C# 寫法還有很多種：欄位不加底線
/// （<c>DoSelectDbContext context;</c>）就辨識不到任何入口、整個檔案靜默略過；
/// 禁止清單寫死 <c>_context.Set&lt;</c> 所以改個欄位名就繞過；有型別的多參數 lambda
/// 與 <c>EF.Property</c> 都認不出來。這些不是 regex 補一條就會結束的問題。
/// </para>
/// <para>
/// 語法樹版本改成：<b>入口靠型別找、禁止規則由找到的入口名稱產生、認不出來的
/// 一律當成違規</b>。只用語法不用語意模型（不需要編譯整個專案），因為要判斷的
/// 是「這個識別字宣告成 DoSelectDbContext 嗎」「這個 lambda 參數叫什麼」，
/// 都是語法層就有的資訊。
/// </para>
/// <para>
/// <b>fail-closed 是這份分析器的預設立場。</b>任何綁不出來源表的查詢參數、
/// 任何解析不了的 <c>EF.Property</c> 引數，都直接回報違規，而不是跳過。
/// 守門漏掉一種寫法的代價是白名單形同不存在。
/// </para>
/// </remarks>
internal static class RefundInfrastructureGuard
{
    private const string ContextTypeName = "DoSelectDbContext";

    /// <summary><c>DbContext</c> 上不是 DbSet 的成員。</summary>
    /// <remarks>
    /// <c>Set</c> 與 <c>Database</c> 刻意不列在這裡 —— 把它們當成「不是資料表」
    /// 而略過，正是第一版守門留下的繞過口。它們由 <see cref="BypassViolations"/> 直接拒絕。
    /// </remarks>
    private static readonly string[] NotTables =
        ["ChangeTracker", "Entry", "SaveChangesAsync", "SaveChanges"];

    /// <summary>lambda 上呼叫的 LINQ／框架成員，不是資料表欄位。</summary>
    private static readonly string[] LinqMembers =
    [
        "Contains", "Sum", "Count", "Any", "All", "Select", "Where", "Key",
        "GetValueOrDefault", "ToString", "Value", "HasValue", "Equals", "Length",
    ];

    /// <summary>Raw SQL 入口，出現即違規（不論掛在哪個物件上）。</summary>
    private static readonly string[] RawSqlMethods =
    [
        "FromSql", "FromSqlRaw", "FromSqlInterpolated",
        "ExecuteSql", "ExecuteSqlRaw", "ExecuteSqlInterpolated",
        "ExecuteSqlAsync", "ExecuteSqlRawAsync", "ExecuteSqlInterpolatedAsync",
    ];

    /// <summary>
    /// 檢查一個檔案。<paramref name="allowedTables"/> 是這個元件核准的
    /// 「資料表 → 欄位」清單；欄位為 <c>"*"</c> 代表本模組自有的表、欄位不設限。
    /// </summary>
    public static IReadOnlyList<string> Violations(
        string fileName,
        string source,
        IReadOnlyDictionary<string, string[]> allowedTables)
    {
        var violations = new List<string>();
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var contextNames = ContextEntryNames(root);

        // fail closed：檔案明明用了 DoSelectDbContext，卻一個入口都辨識不到，
        // 代表這份分析器看不懂它 —— 不能當成「沒有存取資料庫」。
        if (contextNames.Count == 0)
        {
            if (MentionsContextType(root))
            {
                violations.Add(
                    $"{fileName} 用到 {ContextTypeName}，但守門分析器辨識不出任何 DbContext 入口。" +
                    "無法確認它存取了哪些資料表，因此一律視為違規。");
            }

            return violations;
        }

        violations.AddRange(BypassViolations(fileName, root, contextNames));
        violations.AddRange(RawSqlViolations(fileName, root));

        foreach (var query in QueryExpressions(root, contextNames))
        {
            violations.AddRange(QueryViolations(fileName, query, contextNames, allowedTables));
        }

        return violations;
    }

    private static bool MentionsContextType(CompilationUnitSyntax root) =>
        root.DescendantNodes().OfType<SimpleNameSyntax>()
            .Any(name => name.Identifier.ValueText == ContextTypeName);

    /// <summary>
    /// 這個檔案裡所有指向 <c>DoSelectDbContext</c> 的識別字。
    /// </summary>
    /// <remarks>
    /// 靠<b>宣告型別</b>找，不管名字長什麼樣 —— 欄位、屬性、參數（含 primary constructor）、
    /// 區域變數都算。另外把 <c>var db = _context;</c> 這種再指派也收進來：舊版是直接
    /// 禁止再指派，但那只擋得住它自己想得到的一種形狀；收進來繼續追蹤，查詢照樣會被檢查。
    /// </remarks>
    private static HashSet<string> ContextEntryNames(CompilationUnitSyntax root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaration in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            if (!IsContextType(declaration.Type))
            {
                continue;
            }

            foreach (var variable in declaration.Variables)
            {
                names.Add(variable.Identifier.ValueText);
            }
        }

        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (IsContextType(parameter.Type))
            {
                names.Add(parameter.Identifier.ValueText);
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (IsContextType(property.Type))
            {
                names.Add(property.Identifier.ValueText);
            }
        }

        // 別名可以串接（var a = _context; var b = a;），所以跑到不動點為止。
        bool added;
        do
        {
            added = false;

            foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer?.Value is IdentifierNameSyntax initializer &&
                    names.Contains(initializer.Identifier.ValueText) &&
                    names.Add(variable.Identifier.ValueText))
                {
                    added = true;
                }
            }

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Right is IdentifierNameSyntax right &&
                    names.Contains(right.Identifier.ValueText) &&
                    assignment.Left is IdentifierNameSyntax left &&
                    names.Add(left.Identifier.ValueText))
                {
                    added = true;
                }
            }
        }
        while (added);

        return names;
    }

    private static bool IsContextType(TypeSyntax? type) =>
        type is not null &&
        type.ToString().Split('.')[^1].Trim() == ContextTypeName;

    /// <summary>
    /// <c>Set&lt;T&gt;()</c> 與 <c>Database</c>：兩者都能取得白名單以外的資料，
    /// 讓逐欄位檢查失效。
    /// </summary>
    /// <remarks>
    /// 禁止規則由<b>實際辨識出來的入口名稱</b>產生，不寫死 <c>_context</c> ——
    /// 舊版寫死之後，把欄位改名成 <c>_dbContext</c> 就完全繞過這一層。
    /// </remarks>
    private static IEnumerable<string> BypassViolations(
        string fileName, CompilationUnitSyntax root, HashSet<string> contextNames)
    {
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (access.Expression is not IdentifierNameSyntax owner ||
                !contextNames.Contains(owner.Identifier.ValueText))
            {
                continue;
            }

            var member = access.Name.Identifier.ValueText;
            if (member is "Set" or "Database")
            {
                yield return
                    $"{fileName} 使用了 {owner.Identifier.ValueText}.{member}，" +
                    "那會繞過 B1 的逐欄位白名單。";
            }
        }
    }

    private static IEnumerable<string> RawSqlViolations(string fileName, CompilationUnitSyntax root)
    {
        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (RawSqlMethods.Contains(name.Identifier.ValueText, StringComparer.Ordinal))
            {
                yield return
                    $"{fileName} 使用了 {name.Identifier.ValueText}，" +
                    "Raw SQL 會繞過 B1 的逐欄位白名單。";
            }
        }
    }

    /// <summary>
    /// 把每一組「碰到 DbContext 的運算式」收斂成一個檢查單位。
    /// </summary>
    /// <remarks>
    /// 單位取<b>最外層的運算式</b>：LINQ 是一路串下來的，<c>Where</c> 的 lambda 要靠它
    /// 前面的 <c>_context.OrderItems</c> 才知道在查哪張表，切太細就綁不出來源。
    /// </remarks>
    private static List<SyntaxNode> QueryExpressions(
        CompilationUnitSyntax root, HashSet<string> contextNames)
    {
        var roots = new List<SyntaxNode>();

        foreach (var access in TableAccesses(root, contextNames))
        {
            SyntaxNode outermost = access;

            // 一路爬到陳述式為止。只爬 ExpressionSyntax 是不夠的：
            // `Join(_context.Roles.AsNoTracking(), ...)` 的引數，父節點是 ArgumentSyntax
            // 而不是運算式，會在那裡斷開、自成一個查詢單位，於是那張表就變成
            // 「用到受限資料表卻解析不出欄位」，而它其實只是同一段 Join 的一半。
            while (outermost.Parent is { } parent && !IsUnitBoundary(parent))
            {
                outermost = parent;
            }

            if (!roots.Contains(outermost))
            {
                roots.Add(outermost);
            }
        }

        return roots;
    }

    /// <summary>查詢單位到此為止：再往上就不是同一段運算式了。</summary>
    private static bool IsUnitBoundary(SyntaxNode node) =>
        node is StatementSyntax
            or MemberDeclarationSyntax
            or EqualsValueClauseSyntax
            or ArrowExpressionClauseSyntax
            or AccessorDeclarationSyntax;

    private static IEnumerable<MemberAccessExpressionSyntax> TableAccesses(
        SyntaxNode scope, HashSet<string> contextNames) =>
        scope.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
            .Where(access =>
                access.Expression is IdentifierNameSyntax owner &&
                contextNames.Contains(owner.Identifier.ValueText) &&
                !NotTables.Contains(access.Name.Identifier.ValueText, StringComparer.Ordinal) &&
                access.Name.Identifier.ValueText is not ("Set" or "Database"));

    private static string TableName(MemberAccessExpressionSyntax access) =>
        access.Name.Identifier.ValueText;

    private static IEnumerable<string> QueryViolations(
        string fileName,
        SyntaxNode query,
        HashSet<string> contextNames,
        IReadOnlyDictionary<string, string[]> allowedTables)
    {
        var tables = TableAccesses(query, contextNames)
            .Select(TableName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var unknown = tables.Where(table => !allowedTables.ContainsKey(table)).ToArray();
        foreach (var table in unknown)
        {
            yield return
                $"{fileName} 存取了白名單以外的資料表：{table}。" +
                "B1 是具名窄範圍例外，不是跨模組通則。";
        }

        if (unknown.Length > 0)
        {
            yield break;
        }

        var restricted = tables
            .Where(table => !allowedTables[table].Contains("*", StringComparer.Ordinal))
            .ToArray();

        if (restricted.Length == 0)
        {
            // 全部是本模組自有的表，欄位不設限。像 _context.RefundAllocations.Add(...)
            // 這種寫入本來就沒有參數可解析。
            yield break;
        }

        var binder = new AliasBinder(contextNames);
        binder.Bind(query);

        foreach (var alias in binder.UnboundAliases)
        {
            yield return
                $"{fileName} 這段查詢有綁不到資料表的參數（{alias}），" +
                "因此無法確認它存取的欄位是否在白名單內。" +
                Environment.NewLine + Excerpt(query);
        }

        var accesses = MemberAccesses(fileName, query, binder, out var unresolved);

        foreach (var problem in unresolved)
        {
            yield return problem;
        }

        if (accesses.Count == 0 && binder.UnboundAliases.Count == 0)
        {
            yield return
                $"{fileName} 這段查詢用到受限的資料表（{string.Join("、", restricted)}），" +
                "但守門分析器解析不出任何可檢查的欄位存取。" +
                Environment.NewLine + Excerpt(query);
        }

        foreach (var (alias, member) in accesses)
        {
            // 來源表無法唯一決定時（例如 Join 之後再 Where 的匿名投影），
            // 要求該成員在每一張候選表的白名單內 —— 取交集，寧可多擋。
            foreach (var table in binder.TablesOf(alias))
            {
                if (!allowedTables.TryGetValue(table, out var fields))
                {
                    continue;
                }

                if (fields.Contains("*", StringComparer.Ordinal) ||
                    fields.Contains(member, StringComparer.Ordinal))
                {
                    continue;
                }

                yield return
                    $"{fileName} 在 {table} 上存取了未核准的欄位：{member}（來自 {alias}）。" +
                    "DEC-B1 以目前核准欄位為上限，新增欄位必須先重新 review 並更新 " +
                    "DEC-B1 與這份白名單。";
            }
        }
    }

    /// <summary>
    /// 找出所有「已綁定參數的成員存取」，包含 <c>EF.Property</c> 這種以字串指定欄位的形式。
    /// </summary>
    private static List<(string Alias, string Member)> MemberAccesses(
        string fileName,
        SyntaxNode query,
        AliasBinder binder,
        out List<string> unresolved)
    {
        var found = new List<(string Alias, string Member)>();
        unresolved = [];

        foreach (var access in query.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (access.Expression is not IdentifierNameSyntax owner ||
                !binder.IsBound(owner.Identifier.ValueText))
            {
                continue;
            }

            var member = access.Name.Identifier.ValueText;
            if (!LinqMembers.Contains(member, StringComparer.Ordinal))
            {
                found.Add((owner.Identifier.ValueText, member));
            }
        }

        // EF.Property<T>(alias, "Field") 是合法的 shadow property 存取，語法上完全
        // 不是成員存取 —— regex 版本看不到它。
        foreach (var invocation in query.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax callee ||
                callee.Expression is not IdentifierNameSyntax { Identifier.ValueText: "EF" } ||
                callee.Name.Identifier.ValueText != "Property")
            {
                continue;
            }

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count == 2 &&
                arguments[0].Expression is IdentifierNameSyntax target &&
                binder.IsBound(target.Identifier.ValueText) &&
                arguments[1].Expression is LiteralExpressionSyntax literal &&
                literal.Token.Value is string field)
            {
                found.Add((target.Identifier.ValueText, field));
                continue;
            }

            unresolved.Add(
                $"{fileName} 使用了守門分析器解析不了的 EF.Property：{invocation}。" +
                "無法確認它存取的欄位是否在白名單內。");
        }

        return found;
    }

    private static string Excerpt(SyntaxNode query)
    {
        var text = query.ToString();
        return text.Length <= 400 ? text : string.Concat(text.AsSpan(0, 400), "…");
    }

    /// <summary>
    /// 把查詢參數綁到它查的資料表。
    /// </summary>
    /// <remarks>
    /// <para>認得三種宣告形式，其餘一律留在 <see cref="UnboundAliases"/> 讓呼叫端失敗：</para>
    /// <list type="bullet">
    /// <item>query syntax 的 <c>from x in ctx.Table</c>、<c>join y in ctx.Table</c></item>
    /// <item>單參數 lambda：綁到它所屬呼叫的接收者所查的表</item>
    /// <item><c>Join</c> 的 key selector 與 result selector：分別綁 outer 與 inner</item>
    /// </list>
    /// <para>
    /// lambda 參數<b>有沒有寫型別都一樣認得</b> —— 語法樹給的是
    /// <c>ParameterSyntax.Identifier</c>，跟型別註記無關。這正是 regex 版本漏掉的
    /// <c>(IdentityUserRole&lt;string&gt; outer, IdentityRole inner) =&gt;</c>。
    /// </para>
    /// </remarks>
    private sealed class AliasBinder(HashSet<string> contextNames)
    {
        private readonly Dictionary<string, HashSet<string>> _bound = new(StringComparer.Ordinal);
        private readonly HashSet<string> _unbound = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> UnboundAliases => _unbound;

        public bool IsBound(string alias) => _bound.ContainsKey(alias);

        public IEnumerable<string> TablesOf(string alias) =>
            _bound.TryGetValue(alias, out var tables) ? tables : [];

        public void Bind(SyntaxNode query)
        {
            foreach (var clause in query.DescendantNodesAndSelf().OfType<FromClauseSyntax>())
            {
                Declare(clause.Identifier.ValueText, SourceTables(clause.Expression));
            }

            foreach (var clause in query.DescendantNodesAndSelf().OfType<JoinClauseSyntax>())
            {
                Declare(clause.Identifier.ValueText, SourceTables(clause.InExpression));

                if (clause.Into is { } into)
                {
                    // join ... into g 的 g 是一個群組，不是某張表的資料列。
                    Declare(into.Identifier.ValueText, []);
                }
            }

            foreach (var lambda in query.DescendantNodesAndSelf().OfType<LambdaExpressionSyntax>())
            {
                BindLambda(lambda);
            }
        }

        private void BindLambda(LambdaExpressionSyntax lambda)
        {
            ParameterSyntax[] parameters = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => [simple.Parameter],
                ParenthesizedLambdaExpressionSyntax parenthesized =>
                    [.. parenthesized.ParameterList.Parameters],
                _ => [],
            };

            if (parameters.Length == 0)
            {
                return;
            }

            var invocation = lambda.Ancestors().OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.ArgumentList.Arguments.Any(argument => argument.Expression == lambda));

            if (invocation?.Expression is not MemberAccessExpressionSyntax callee)
            {
                DeclareAll(parameters, []);
                return;
            }

            var outer = SourceTables(callee.Expression);
            var method = callee.Name.Identifier.ValueText;
            var arguments = invocation.ArgumentList.Arguments;

            if (method is "Join" or "GroupJoin" && arguments.Count >= 1)
            {
                var inner = SourceTables(arguments[0].Expression);
                var position = arguments.IndexOf(
                    arguments.First(argument => argument.Expression == lambda));

                // Join(inner, outerKeySelector, innerKeySelector, resultSelector)
                switch (position)
                {
                    case 1:
                        DeclareAll(parameters, outer);
                        return;
                    case 2:
                        DeclareAll(parameters, inner);
                        return;
                    case 3 when parameters.Length == 2:
                        Declare(parameters[0].Identifier.ValueText, outer);
                        Declare(parameters[1].Identifier.ValueText, inner);
                        return;
                    default:
                        DeclareAll(parameters, []);
                        return;
                }
            }

            if (parameters.Length == 1)
            {
                DeclareAll(parameters, outer);
                return;
            }

            // 認不出來的多參數 lambda：留成未綁定，讓呼叫端失敗。
            DeclareAll(parameters, []);
        }

        /// <summary>接收者運算式裡查到的資料表。</summary>
        private HashSet<string> SourceTables(ExpressionSyntax expression) =>
            [.. TableAccesses(expression, contextNames).Select(TableName)];

        private void DeclareAll(IReadOnlyList<ParameterSyntax> parameters, HashSet<string> tables)
        {
            foreach (var parameter in parameters)
            {
                Declare(parameter.Identifier.ValueText, tables);
            }
        }

        private void Declare(string alias, HashSet<string> tables)
        {
            if (tables.Count == 0)
            {
                if (!_bound.ContainsKey(alias))
                {
                    _unbound.Add(alias);
                }

                return;
            }

            _unbound.Remove(alias);

            if (_bound.TryGetValue(alias, out var existing))
            {
                existing.UnionWith(tables);
                return;
            }

            _bound[alias] = [.. tables];
        }
    }
}
