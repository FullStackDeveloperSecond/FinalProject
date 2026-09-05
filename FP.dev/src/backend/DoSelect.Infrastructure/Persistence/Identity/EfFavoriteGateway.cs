using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Application.Favorites;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class EfFavoriteGateway : IFavoriteGateway
{
    private const string FavoritesPrimaryKeyIndexName = "PK_Favorites";

    private readonly DoSelectDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EfFavoriteGateway(DoSelectDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<AddFavoriteResult> AddAsync(
        string memberUserId,
        Guid productPublicId,
        CancellationToken cancellationToken)
    {
        var productId = await _dbContext.Products.AsNoTracking()
            .Where(product => product.PublicId == productPublicId)
            .Select(product => (long?)product.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (productId is null)
        {
            return AddFavoriteResult.ProductNotFound;
        }

        var alreadyFavorited = await _dbContext.Favorites.AsNoTracking()
            .AnyAsync(
                favorite => favorite.MemberUserId == memberUserId && favorite.ProductId == productId.Value,
                cancellationToken);

        // 評價收藏檢舉與模擬發票規格.md: MemberId + ProductId 唯一，重複加入視為成功且不建立第二筆。
        if (alreadyFavorited)
        {
            return AddFavoriteResult.Success;
        }

        _dbContext.Favorites.Add(new Favorite(
            memberUserId,
            productId.Value,
            _timeProvider.GetUtcNow().UtcDateTime));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            SqlUniqueIndexViolations.Matches(exception, FavoritesPrimaryKeyIndexName))
        {
            // Two concurrent "add" calls for the same member/product both passed the AnyAsync
            // check above; the loser hits the (MemberUserId, ProductId) primary key here. The
            // row exists either way, which is still the idempotent success the spec asks for.
            _dbContext.ChangeTracker.Clear();
        }

        return AddFavoriteResult.Success;
    }

    public async Task RemoveAsync(
        string memberUserId,
        Guid productPublicId,
        CancellationToken cancellationToken)
    {
        var productId = await _dbContext.Products.AsNoTracking()
            .Where(product => product.PublicId == productPublicId)
            .Select(product => (long?)product.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (productId is null)
        {
            // Nothing could ever have been favorited under a PublicId that doesn't resolve to a
            // product; removal of a non-existent favorite is still success (see below).
            return;
        }

        // 評價收藏檢舉與模擬發票規格.md: 移除收藏可直接刪除沒有獨立稽核價值的 Join Row；不適用敏感或
        // 高風險稽核規則。A no-op delete (already removed, or never favorited) is success, not
        // 404 — this keeps DELETE idempotent and avoids using existence as an oracle.
        await _dbContext.Favorites
            .Where(favorite => favorite.MemberUserId == memberUserId && favorite.ProductId == productId.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<PageResult<FavoriteItemDto>> ListAsync(
        string memberUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var favoritesQuery = _dbContext.Favorites.AsNoTracking()
            .Where(favorite => favorite.MemberUserId == memberUserId);

        var totalCount = await favoritesQuery.CountAsync(cancellationToken);

        var page = await favoritesQuery
            .OrderByDescending(favorite => favorite.CreatedAtUtc)
            .ThenByDescending(favorite => favorite.ProductId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(favorite => new { favorite.ProductId, favorite.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return new PageResult<FavoriteItemDto>([], pageNumber, pageSize, totalCount);
        }

        var productIds = page.Select(entry => entry.ProductId).ToArray();

        var rows = await (
            from product in _dbContext.Products.AsNoTracking()
            where productIds.Contains(product.Id)
            join brand in _dbContext.Brands.AsNoTracking() on product.BrandId equals brand.Id
            join category in _dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            select new { Product = product, Brand = brand, Category = category })
            .ToListAsync(cancellationToken);
        var rowsByProductId = rows.ToDictionary(row => row.Product.Id);

        var skusByProductId = await _dbContext.Skus.AsNoTracking()
            .Where(sku => productIds.Contains(sku.ProductId))
            .OrderByDescending(sku => sku.IsDefault)
            .ThenBy(sku => sku.SkuCode)
            .GroupBy(sku => sku.ProductId)
            .ToDictionaryAsync(group => group.Key, group => group.First(), cancellationToken);

        var skuIds = skusByProductId.Values.Select(sku => sku.Id).ToArray();

        var balancesBySkuId = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var salePricesBySkuId = await _dbContext.SalePrices.AsNoTracking()
            .Where(salePrice =>
                skuIds.Contains(salePrice.SkuId) &&
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= nowUtc &&
                salePrice.EndsAtUtc > nowUtc)
            .ToDictionaryAsync(salePrice => salePrice.SkuId, salePrice => salePrice.Price, cancellationToken);

        var items = new List<FavoriteItemDto>(page.Count);
        foreach (var entry in page)
        {
            // A favorite can outlive its product only through hard deletion, which this schema's
            // Restrict FK (see FavoriteConfiguration) never allows — so a missing row here would
            // mean the two queries above ran against different data, not a real orphan. Still
            // skip rather than throw: a favorites list is not the place to fail a whole page over
            // one row, and this can only be a same-request race with a concurrent hard delete
            // that Restrict is supposed to prevent in the first place.
            if (!rowsByProductId.TryGetValue(entry.ProductId, out var row))
            {
                continue;
            }

            items.Add(BuildItem(row.Product, row.Brand, row.Category, entry.CreatedAtUtc, skusByProductId, balancesBySkuId, salePricesBySkuId));
        }

        return new PageResult<FavoriteItemDto>(items, pageNumber, pageSize, totalCount);
    }

    private static FavoriteItemDto BuildItem(
        Product product,
        Brand brand,
        Category category,
        DateTime createdAtUtc,
        IReadOnlyDictionary<long, Sku> skusByProductId,
        IReadOnlyDictionary<long, InventoryBalance> balancesBySkuId,
        IReadOnlyDictionary<long, decimal> salePricesBySkuId)
    {
        skusByProductId.TryGetValue(product.Id, out var sku);
        var isPublished = product.Status == ProductStatus.Published &&
            sku is not null &&
            sku.Status == SkuStatus.Published;

        ProductPrice? price = sku is null
            ? null
            : new ProductPrice(sku.ListPrice, salePricesBySkuId.GetValueOrDefault(sku.Id), "TWD");

        if (!isPublished)
        {
            return new FavoriteItemDto(
                product.PublicId,
                product.ProductCode,
                product.NameZhTw,
                new ProductBrandRef(brand.Code, brand.NameZhTw),
                new ProductCategoryRef(category.Code, category.NameZhTw),
                price,
                null,
                FavoriteAvailabilityCodes.Delisted,
                false,
                createdAtUtc);
        }

        balancesBySkuId.TryGetValue(sku!.Id, out var balance);
        var availability = ResolveStockAvailability(balance);

        return new FavoriteItemDto(
            product.PublicId,
            product.ProductCode,
            product.NameZhTw,
            new ProductBrandRef(brand.Code, brand.NameZhTw),
            new ProductCategoryRef(category.Code, category.NameZhTw),
            price,
            // Public image URLs depend on the shared file/image service (SH-06), which is not
            // available yet — same deferral EfProductSearchService/EfProductDetailService use.
            null,
            availability,
            availability != ProductAvailabilityCodes.OutOfStock,
            createdAtUtc);
    }

    private static string ResolveStockAvailability(InventoryBalance? balance)
    {
        if (balance is null || balance.AvailableQuantity <= 0)
        {
            return ProductAvailabilityCodes.OutOfStock;
        }

        return balance.AvailableQuantity <= balance.ReorderLevel
            ? ProductAvailabilityCodes.LowStock
            : ProductAvailabilityCodes.InStock;
    }
}
