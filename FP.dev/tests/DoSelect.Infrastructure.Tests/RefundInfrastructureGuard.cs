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
/// <b>接收者靠語意判斷，不靠形狀。</b>2026-08-29 alex 又找到一種繞法：
/// <c>this._context.Orders</c>。上一版要求接收者是裸識別字，所以這種寫法
/// 完全不會進入資料表與 bypass 檢查，卻又因為欄位宣告仍在、不會觸發
/// 「找不到入口」的 fail-closed —— 結果是回報零違規。
/// </para>
/// <para>
/// 這已經是第三次補一種形狀，所以改成用 <see cref="SemanticModel"/> 問
/// 「這個運算式的型別是不是 <c>DoSelectDbContext</c>」。<c>this.</c>、<c>base.</c>、
/// 括號、cast、區域別名全部一視同仁，因為判斷的是型別不是寫法。
/// </para>
/// <para>
/// 語意解析失敗時退回語法正規化（遞迴拆掉 <c>this</c>／<c>base</c>／括號／cast），
/// 兩者都認不出來、但接收者裡出現已知的 DbContext 識別字 —— 直接回報違規。
/// </para>
/// <para>
/// <b>fail-closed 是這份分析器的預設立場。</b>任何綁不出來源表的查詢參數、
/// 任何解析不了的 <c>EF.Property</c> 引數、任何看不懂的 DbContext 用法，
/// 都直接回報違規，而不是跳過。守門漏掉一種寫法的代價是白名單形同不存在。
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
        IReadOnlyDictionary<string, string[]> allowedTables,
        IReadOnlyCollection<string>? approvedComponents = null,
        bool useSemanticModel = true)
    {
        var violations = new List<string>();
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();
        // useSemanticModel: false 只給測試用 —— 語意解析失敗時會退回語法正規化，
        // 那條路徑沒有辦法從外面觸發，不強制關掉就等於沒被測過。
        var model = useSemanticModel ? CreateSemanticModel(tree) : null;
        var contextNames = ContextEntryNames(root);
        var context = new GuardContext(model, contextNames);

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

        violations.AddRange(BypassViolations(fileName, root, context));
        violations.AddRange(RawSqlViolations(fileName, root));
        violations.AddRange(UnclassifiedContextUseViolations(
            fileName, root, context, approvedComponents ?? []));

        foreach (var query in QueryExpressions(root, context))
        {
            violations.AddRange(QueryViolations(fileName, query, context, allowedTables));
        }

        return violations;
    }

    /// <summary>
    /// 這份分析器判斷「這是不是 DbContext」時用得到的兩樣東西。
    /// </summary>
    /// <param name="Model">語意模型；解析不出來時退回語法正規化。</param>
    /// <param name="EntryNames">
    /// 語法上宣告成 <c>DoSelectDbContext</c> 的識別字。除了當語意失敗時的備援，
    /// 也是 fail-closed 的網子：接收者裡出現這些名字卻沒被分類，就是看不懂。
    /// </param>
    private sealed record GuardContext(SemanticModel? Model, HashSet<string> EntryNames);

    /// <summary>
    /// 測試專案已經載入了 Domain／Application／Infrastructure 與 EF Core，
    /// 直接拿它們當 metadata reference，就能讓語意模型解析出 <c>DoSelectDbContext</c>。
    /// </summary>
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))]);

    /// <remarks>
    /// 只編這一個檔案。缺少同專案其他型別會產生編譯錯誤，但不影響這裡要問的事：
    /// <c>_context</c> 的型別來自 metadata reference，照樣解析得出來。
    /// 真的解析不出來時回 <c>null</c>，由語法備援與 fail-closed 接手。
    /// </remarks>
    private static SemanticModel? CreateSemanticModel(SyntaxTree tree)
    {
        try
        {
            var compilation = CSharpCompilation.Create(
                "RefundInfrastructureGuard",
                [tree],
                References.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return compilation.GetSemanticModel(tree);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>這個運算式指的是不是一個 <c>DoSelectDbContext</c>。</summary>
    /// <remarks>
    /// 先問語意模型（型別對就算，寫成 <c>this._context</c>、<c>(ctx)</c>、
    /// <c>((DoSelectDbContext)x)</c> 都一樣）；解析不出來才退回語法正規化。
    /// </remarks>
    private static bool IsContextReference(ExpressionSyntax expression, GuardContext context)
    {
        var type = context.Model?.GetTypeInfo(expression).Type;
        if (type is not null)
        {
            return type.Name == ContextTypeName;
        }

        var normalised = NormaliseToIdentifier(expression);
        return normalised is not null && context.EntryNames.Contains(normalised);
    }

    /// <summary>
    /// 遞迴拆掉 <c>this</c>／<c>base</c>／括號／cast／<c>!</c>，取出最裡面的識別字。
    /// </summary>
    private static string? NormaliseToIdentifier(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        ParenthesizedExpressionSyntax parenthesized =>
            NormaliseToIdentifier(parenthesized.Expression),
        CastExpressionSyntax cast => NormaliseToIdentifier(cast.Expression),
        PostfixUnaryExpressionSyntax postfix => NormaliseToIdentifier(postfix.Operand),
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } member =>
            member.Name.Identifier.ValueText,
        _ => null,
    };

    /// <summary>
    /// 接收者裡出現已知的 DbContext 識別字，卻沒有被分類成 DbContext 存取。
    /// </summary>
    /// <remarks>
    /// 這是最後一道網子：語意與語法都認不出來的寫法，一律當成違規而不是跳過。
    /// 沒有它，只要想出一種兩邊都認不得的接收者形狀，整段查詢就會靜默離開白名單。
    /// </remarks>
    private static IEnumerable<string> UnclassifiedContextUseViolations(
        string fileName,
        CompilationUnitSyntax root,
        GuardContext context,
        IReadOnlyCollection<string> approvedComponents)
    {
        // 先找出所有「接收者確實是 DbContext」的存取。掛在它們後面的鏈
        // （.AsNoTracking().Where(...)）接收者是 IQueryable 不是 DbContext，
        // 那是正常寫法，不是看不懂。
        var classified = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(candidate => IsContextReference(candidate.Expression, context))
            .ToHashSet();

        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (classified.Contains(access) ||
                access.Expression.DescendantNodesAndSelf()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Any(classified.Contains))
            {
                continue;
            }

            // 把 DbContext 交給另一個**同樣列在白名單裡**的 Refund 元件是允許的：
            // 那個元件有自己的資料表與欄位清單，會在它自己的檔案裡被檢查。
            // 交給白名單外的型別就不行 —— 那條路徑沒有任何人在看。
            if (access.Expression.DescendantNodesAndSelf()
                .OfType<ObjectCreationExpressionSyntax>()
                .Any(creation => approvedComponents.Contains(
                    creation.Type.ToString().Split('.')[^1], StringComparer.Ordinal)))
            {
                continue;
            }

            // 到這裡代表 DbContext 是被「傳來傳去」而不是被點出來的 ——
            // 例如 Pick(_context).Orders。這種形狀分析器讀不出它查了什麼。
            var mentionsEntry = access.Expression.DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Any(identifier => context.EntryNames.Contains(identifier.Identifier.ValueText));

            if (mentionsEntry)
            {
                yield return
                    $"{fileName} 有一個守門分析器看不懂的 DbContext 用法：{access}。" +
                    "無法確認它存取了哪張表或哪個欄位，因此一律視為違規。";
            }
        }
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
                // 正規化過再比：`var db = this._context;` 也要追得到。
                if (variable.Initializer?.Value is { } initializer &&
                    NormaliseToIdentifier(initializer) is { } source &&
                    names.Contains(source) &&
                    names.Add(variable.Identifier.ValueText))
                {
                    added = true;
                }
            }

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (NormaliseToIdentifier(assignment.Right) is { } right &&
                    names.Contains(right) &&
                    NormaliseToIdentifier(assignment.Left) is { } left &&
                    names.Add(left))
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
        string fileName, CompilationUnitSyntax root, GuardContext context)
    {
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!IsContextReference(access.Expression, context))
            {
                continue;
            }

            var member = access.Name.Identifier.ValueText;
            if (member is "Set" or "Database")
            {
                yield return
                    $"{fileName} 使用了 {access.Expression}.{member}，" +
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
        CompilationUnitSyntax root, GuardContext context)
    {
        var roots = new List<SyntaxNode>();

        foreach (var access in TableAccesses(root, context))
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
        SyntaxNode scope, GuardContext context) =>
        scope.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
            .Where(access =>
                IsContextReference(access.Expression, context) &&
                !NotTables.Contains(access.Name.Identifier.ValueText, StringComparer.Ordinal) &&
                access.Name.Identifier.ValueText is not ("Set" or "Database"));

    private static string TableName(MemberAccessExpressionSyntax access) =>
        access.Name.Identifier.ValueText;

    private static IEnumerable<string> QueryViolations(
        string fileName,
        SyntaxNode query,
        GuardContext context,
        IReadOnlyDictionary<string, string[]> allowedTables)
    {
        var tables = TableAccesses(query, context)
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

        var binder = new AliasBinder(context);
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
    private sealed class AliasBinder(GuardContext context)
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
            [.. TableAccesses(expression, context).Select(TableName)];

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
