using DoSelect.Application.Favorites;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Favorites;

public sealed class EfFavoriteService(DoSelectDbContext dbContext, TimeProvider timeProvider) : IFavoriteService
{
    public async Task<IReadOnlyList<FavoriteDto>> ListMineAsync(
        string memberUserId,
        CancellationToken cancellationToken)
    {
        var rows = await QueryRows(dbContext, memberUserId)
            .OrderByDescending(row => row.Favorite.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<FavoriteDto> AddAsync(
        string memberUserId,
        Guid productPublicId,
        CancellationToken cancellationToken)
    {
        var productId = await ResolveProductIdAsync(productPublicId, cancellationToken);

        var existing = await dbContext.Favorites.AsNoTracking()
            .AnyAsync(
                favorite => favorite.MemberUserId == memberUserId && favorite.ProductId == productId,
                cancellationToken);

        if (!existing)
        {
            dbContext.Favorites.Add(new Favorite(memberUserId, productId, timeProvider.GetUtcNow().UtcDateTime));
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // 兩個併發請求同時加入同一筆收藏會撞複合 PK；這跟「重複加入視為成功」是同一種
                // 使用者可重試情境，不視為錯誤（沒有獨立稽核價值，見 FavoriteContracts 開頭備註）。
                dbContext.ChangeTracker.Clear();
            }
        }

        var row = await QueryRows(dbContext, memberUserId)
            .SingleAsync(row => row.Product.Id == productId, cancellationToken);
        return ToDto(row);
    }

    public async Task RemoveAsync(
        string memberUserId,
        Guid productPublicId,
        CancellationToken cancellationToken)
    {
        var productId = await dbContext.Products.AsNoTracking()
            .Where(product => product.PublicId == productPublicId)
            .Select(product => (long?)product.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (productId is null)
        {
            return;
        }

        var favorite = await dbContext.Favorites
            .SingleOrDefaultAsync(
                favorite => favorite.MemberUserId == memberUserId && favorite.ProductId == productId,
                cancellationToken);
        if (favorite is null)
        {
            return;
        }

        dbContext.Favorites.Remove(favorite);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<long> ResolveProductIdAsync(Guid productPublicId, CancellationToken cancellationToken)
    {
        var productId = await dbContext.Products.AsNoTracking()
            .Where(product => product.PublicId == productPublicId)
            .Select(product => (long?)product.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return productId ?? throw new FavoriteWriteException(
            FavoriteWriteException.ErrorCodes.ProductNotFound,
            "The product was not found.");
    }

    private static IQueryable<FavoriteRow> QueryRows(DoSelectDbContext context, string memberUserId)
    {
        var nowUtc = DateTime.UtcNow;
        var activeSalePrices = context.SalePrices.AsNoTracking()
            .Where(salePrice =>
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= nowUtc &&
                salePrice.EndsAtUtc > nowUtc);

        return
            from favorite in context.Favorites.AsNoTracking()
            where favorite.MemberUserId == memberUserId
            join product in context.Products.AsNoTracking() on favorite.ProductId equals product.Id
            join defaultSku in context.Skus.AsNoTracking().Where(candidate => candidate.IsDefault)
                on product.Id equals defaultSku.ProductId into skuGroup
            from sku in skuGroup.DefaultIfEmpty()
            join defaultBalance in context.InventoryBalances.AsNoTracking()
                on sku.Id equals defaultBalance.SkuId into balanceGroup
            from balance in balanceGroup.DefaultIfEmpty()
            join defaultSalePrice in activeSalePrices on sku.Id equals defaultSalePrice.SkuId into saleGroup
            from salePrice in saleGroup.DefaultIfEmpty()
            select new FavoriteRow
            {
                Favorite = favorite,
                Product = product,
                Sku = sku,
                Balance = balance,
                SalePrice = salePrice != null ? salePrice.Price : (decimal?)null,
            };
    }

    private static FavoriteDto ToDto(FavoriteRow row)
    {
        var product = new FavoriteProductDto(
            row.Product.PublicId,
            row.Product.ProductCode,
            row.Product.NameZhTw,
            row.Sku?.ListPrice ?? 0m,
            row.SalePrice,
            "TWD",
            ResolveAvailability(row.Product, row.Sku, row.Balance));

        return new FavoriteDto(product, row.Favorite.CreatedAtUtc);
    }

    private static string ResolveAvailability(Product product, Sku? sku, Domain.Inventory.InventoryBalance? balance)
    {
        if (product.Status != ProductStatus.Published || sku is null || sku.Status != SkuStatus.Published)
        {
            return FavoriteAvailabilityCodes.Unlisted;
        }

        return balance is not null && balance.AvailableQuantity > 0
            ? FavoriteAvailabilityCodes.Available
            : FavoriteAvailabilityCodes.OutOfStock;
    }

    private sealed class FavoriteRow
    {
        public required Favorite Favorite { get; init; }

        public required Product Product { get; init; }

        public Sku? Sku { get; init; }

        public Domain.Inventory.InventoryBalance? Balance { get; init; }

        public decimal? SalePrice { get; init; }
    }
}
