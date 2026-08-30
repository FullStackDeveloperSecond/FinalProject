using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

/// <summary>
/// <see cref="ICouponCatalogOptionsReader"/> 的 Catalog 側實作。
/// </summary>
/// <remarks>
/// <para>
/// 三個查詢都是<b>單次往返</b>，這是契約的一部分而不是最佳化：先前的做法對分類樹
/// 每個節點各打一次公開端點、對每個已選商品各查一次明細，一次編輯就會放大成
/// 上百次 HTTP 與 SQL（alex 2026-08-29 PR #64 P2#3）。
/// </para>
/// <para>唯讀。這個 Reader 沒有任何寫入路徑。</para>
/// </remarks>
public sealed class CouponCatalogOptionsReader : ICouponCatalogOptionsReader
{
    private readonly DoSelectDbContext _context;

    public CouponCatalogOptionsReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <remarks>
    /// 整棵樹一次撈回來再在記憶體裡接路徑。分類是參考資料、量級是數十筆，
    /// 用遞迴 CTE 或逐層查詢換來的複雜度買不到什麼。
    /// </remarks>
    public async Task<IReadOnlyList<CouponCategoryOption>> ListCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Categories.AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Id)
            .Select(category => new
            {
                category.Id,
                category.PublicId,
                category.Code,
                category.NameZhTw,
                category.IsActive,
                category.ParentCategoryId,
            })
            .ToArrayAsync(cancellationToken);

        var byId = rows.ToDictionary(row => row.Id);
        var options = new List<CouponCategoryOption>(rows.Length);

        foreach (var row in rows)
        {
            var segments = new List<string>();
            var cursor = row;
            // 資料若出現環狀 parent，沒有這個上限會在這裡無限繞。
            var guard = 0;

            while (guard++ < rows.Length)
            {
                segments.Insert(0, cursor.NameZhTw);
                if (cursor.ParentCategoryId is not { } parentId ||
                    !byId.TryGetValue(parentId, out var parent))
                {
                    break;
                }

                cursor = parent;
            }

            options.Add(new CouponCategoryOption(
                row.PublicId,
                row.Code,
                row.NameZhTw,
                string.Join(" / ", segments),
                row.IsActive));
        }

        return options;
    }

    public async Task<CouponProductSearchResult> SearchProductsAsync(
        string? keyword,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(pageSize, 1, CouponCatalogOptionRules.MaximumSearchPageSize);
        var page = Math.Max(pageNumber, 1);

        // 契約允許 pageNumber 到 int.MaxValue，但 (page - 1) * size 用 int 會溢位成
        // 負的 offset —— 本該回空頁的請求會變成 SQL 查詢失敗。先用 long 算，
        // 超過 int.MaxValue 就直接回空頁、不送 SQL。
        // 專案既有的 EfProductSearchService 也是這樣處理同一個問題。
        var skip = ((long)page - 1) * size;
        if (skip > int.MaxValue)
        {
            return new CouponProductSearchResult([], HasMore: false);
        }
        var trimmed = keyword?.Trim();

        var query = _context.Products.AsNoTracking()
            // 搜尋結果是「可以加進來的東西」，停售商品不該出現在這裡。
            .Where(product => product.Status != ProductStatus.Discontinued);

        if (!string.IsNullOrEmpty(trimmed))
        {
            query = query.Where(product =>
                product.NameZhTw.Contains(trimmed) ||
                product.ProductCode.Contains(trimmed));
        }

        // 多取一筆來判斷還有沒有更多，不另外打一次 COUNT。
        //
        // 排序用 ProductCode（唯一索引）：排序鍵不唯一的話，SQL Server 對相同鍵值的
        // 回傳順序沒有保證，同一筆可能同時出現在兩頁、也可能兩頁都漏掉。
        var rows = await query
            .OrderBy(product => product.ProductCode)
            .Skip((int)skip)
            .Take(size + 1)
            .Select(product => new
            {
                product.PublicId,
                product.ProductCode,
                product.NameZhTw,
                product.Status,
            })
            .ToArrayAsync(cancellationToken);

        var hasMore = rows.Length > size;
        var items = rows.Take(size)
            .Select(row => ToOption(row.PublicId, row.ProductCode, row.NameZhTw, row.Status))
            .ToArray();

        return new CouponProductSearchResult(items, hasMore);
    }

    /// <remarks>
    /// 一次 <c>WHERE PublicId IN (...)</c>，不論幾筆都只有一次往返 ——
    /// 逐筆查會隨已選數量形成 N+1。
    /// </remarks>
    public async Task<IReadOnlyList<CouponProductOption>> ResolveProductsAsync(
        IReadOnlyCollection<Guid> publicIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicIds);

        var wanted = publicIds.Where(publicId => publicId != Guid.Empty).Distinct().ToArray();
        if (wanted.Length == 0)
        {
            // 空集合不要往下打一個 `IN ()` 的查詢。
            return [];
        }

        if (wanted.Length > CouponCatalogOptionRules.MaximumBatchSize)
        {
            // 靜默截斷會讓被切掉的那幾筆看起來像「這個商品不存在」，
            // 而呼叫端正要用它來顯示既有規則。
            throw new ArgumentException(
                $"At most {CouponCatalogOptionRules.MaximumBatchSize} product ids can be resolved at once.",
                nameof(publicIds));
        }

        // 停售商品也要回得出來：已經寫在券上的參考不能因為 picker 查不到就消失。
        var rows = await _context.Products.AsNoTracking()
            .Where(product => wanted.Contains(product.PublicId))
            .OrderBy(product => product.ProductCode)
            .Select(product => new
            {
                product.PublicId,
                product.ProductCode,
                product.NameZhTw,
                product.Status,
            })
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => ToOption(row.PublicId, row.ProductCode, row.NameZhTw, row.Status))];
    }

    private static CouponProductOption ToOption(
        Guid publicId, string code, string name, ProductStatus status)
    {
        var mapped = status switch
        {
            ProductStatus.Draft => ProductOptionStatus.Draft,
            ProductStatus.Published => ProductOptionStatus.Published,
            ProductStatus.Unpublished => ProductOptionStatus.Unpublished,
            ProductStatus.Discontinued => ProductOptionStatus.Discontinued,
            // 之後新增的狀態不能預設成「可選」—— 那會讓一個沒人審過的狀態
            // 自動取得排進優惠券的資格。
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        return new CouponProductOption(
            publicId,
            code,
            name,
            mapped,
            CouponCatalogOptionRules.IsSelectable(mapped));
    }
}
