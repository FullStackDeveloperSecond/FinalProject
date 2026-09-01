namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Allocates the keys an <see cref="DoSelect.Domain.Imports.ImportRow"/> is stored under, so that
/// no two rows of one dataset can collide on
/// <c>UX_ImportRows_ImportBatchId_Dataset_ImportKey</c>.
///
/// 組長 PR #74 round-4 review (P2)：合成鍵不能只靠「使用者不會剛好用到這個字串」。上傳的
/// business key 沒有保留字限制，而 CI 的 SQL Server container 沒有設定 <c>MSSQL_COLLATION</c>，
/// 預設 <c>SQL_Latin1_General_CP1_CI_AS</c> 不分大小寫——應用層用 Ordinal 比會覺得 <c>__dup4</c>
/// 與使用者的 <c>__DUP4</c> 是兩個鍵，資料庫卻認為是同一個，於是又變成未映射的 500。
///
/// 這個配置器先把整個資料集實際用到的 business key 全部登記起來，合成鍵再從沒被佔用的名字裡挑。
/// 同一套機制同時保護「重複鍵」「缺鍵」與「超長鍵」三種需要合成鍵的情況。
///
/// 組長 PR #74 round-5 review (P2)：比較規則不能只用 <c>OrdinalIgnoreCase</c>。
/// <c>SQL_Latin1_General_CP1_CI_AS</c> 沒有 <c>_WS</c>，屬於 width-insensitive，而
/// <c>NormalizeKey</c> 刻意不做 NFKC，所以全形的 <c>＿＿ＤＵＰ３</c> 與合成鍵 <c>__dup3</c> 在
/// 應用層是兩個字串、在資料庫卻可能是同一個。因此登記與查詢都改用「碰撞比較用的 canonical key」
/// （NFKC ＋ 大寫），**只**影響配置器的判斷，實際存進 ImportKey 的business key 一個字都不動。
/// </summary>
internal sealed class ImportStorageKeyAllocator
{
    /// <summary>ImportRow.ImportKey 的欄位長度上限；超過就不可能安全存進去。</summary>
    public const int MaxKeyLength = 64;

    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <summary>Registers a key the dataset actually uses, so no synthetic key can shadow it.</summary>
    public void Reserve(string? key)
    {
        if (CanStore(key))
        {
            _taken.Add(Canonicalize(key!));
        }
    }

    /// <summary>
    /// Whether a business key can be stored as-is: present and within the column's 64-character
    /// limit. 組長 PR #74 round-5 review (P2)：超長的 key 先前只加了列級錯誤卻仍被當成 ImportKey，
    /// 於是 65 個字元的 key 讓整批 Preview 死在欄位長度限制上，變成 500 而不是 Invalid batch。
    /// </summary>
    public static bool CanStore(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength;

    /// <summary>
    /// The form used only for collision checks: NFKC folds full-width/compatibility characters the
    /// way SQL Server's width-insensitive collation does, and upper-casing covers its
    /// case-insensitivity. 組長 PR #74 round-6 review (裁定 A1)：兩個 business key 之間的碰撞也用
    /// 同一個形式判斷，所以 parser 也需要它——絕不能出現「配置器用一套、重複偵測用另一套」的落差。
    /// 這只是比較用的投影，實際存進 ImportKey 的字串永遠是原值。
    /// </summary>
    public static string Canonicalize(string key) =>
        key.Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant();

    /// <summary>
    /// A row-scoped key for a row that cannot be stored under its own business key (a duplicate,
    /// or a row whose key column was missing). Source row numbers are unique within a dataset, so
    /// the first candidate is almost always free; the suffix loop only runs when the upload itself
    /// contains a string that would collide under the database's comparison rules.
    /// </summary>
    public string Allocate(string prefix, int sourceRowNumber)
    {
        var candidate = $"__{prefix}{sourceRowNumber}";
        var attempt = 0;
        while (!_taken.Add(Canonicalize(candidate)))
        {
            attempt++;
            candidate = $"__{prefix}{sourceRowNumber}_{attempt}";
        }

        return candidate;
    }
}
