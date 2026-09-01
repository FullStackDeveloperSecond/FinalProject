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
/// 這個配置器改用與資料庫一致的比較規則（<see cref="StringComparer.OrdinalIgnoreCase"/>，合成鍵
/// 全是 ASCII，因此不需要處理 accent-sensitive 的部分）：先把整個資料集實際用到的 business key
/// 全部登記起來，合成鍵再從沒被佔用的名字裡挑。同一套機制同時保護「重複鍵」與「缺鍵」兩種合成鍵
/// ——後者原本也會被使用者的 <c>__ROW5</c> 撞到。
/// </summary>
internal sealed class ImportStorageKeyAllocator
{
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a key the dataset actually uses, so no synthetic key can shadow it.</summary>
    public void Reserve(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            _taken.Add(key);
        }
    }

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
        while (!_taken.Add(candidate))
        {
            attempt++;
            candidate = $"__{prefix}{sourceRowNumber}_{attempt}";
        }

        return candidate;
    }
}
