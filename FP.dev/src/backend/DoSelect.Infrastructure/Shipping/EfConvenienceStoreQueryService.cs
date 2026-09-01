using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// PageNumber/PageSize bounds are enforced by <c>ListConvenienceStoresRequest</c>'s
/// [Range] attributes at the Api layer (400 on violation) — this trusts the query it's given
/// rather than silently clamping an out-of-range value, per the standing PR review ruling
/// against silent pageSize truncation (see AdminInventoryController's history).
/// </summary>
public sealed class EfConvenienceStoreQueryService : IConvenienceStoreQueryService
{
    private readonly DoSelectDbContext _dbContext;

    public EfConvenienceStoreQueryService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<ConvenienceStoreOptionDto>> ListAsync(
        ConvenienceStoreQuery query,
        CancellationToken cancellationToken)
    {
        var pageNumber = query.PageNumber;
        var pageSize = query.PageSize;

        var stores = _dbContext.ConvenienceStores.AsNoTracking().Where(store => store.IsActive);

        if (!string.IsNullOrWhiteSpace(query.ProviderCode))
        {
            stores = stores.Where(store => store.ProviderCode == query.ProviderCode);
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            stores = stores.Where(store => store.City == query.City);
        }

        if (!string.IsNullOrWhiteSpace(query.District))
        {
            stores = stores.Where(store => store.District == query.District);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            stores = stores.Where(store =>
                EF.Functions.Like(store.StoreName, $"%{term}%") ||
                EF.Functions.Like(store.StoreCode, $"%{term}%"));
        }

        var totalCount = await stores.CountAsync(cancellationToken);

        // Same int-overflow guard as the admin list (組長 PR #73 round-3, item 4).
        var skip = (long)(pageNumber - 1) * pageSize;
        var items = skip > int.MaxValue
            ? []
            : await stores
            .OrderBy(store => store.City)
            .ThenBy(store => store.District)
            .ThenBy(store => store.StoreCode)
            .Skip((int)skip)
            .Take(pageSize)
            .Select(store => new ConvenienceStoreOptionDto(
                store.PublicId,
                store.ProviderCode,
                store.StoreCode,
                store.StoreName,
                store.City,
                store.District,
                store.Address,
                store.IsDemoData))
            .ToListAsync(cancellationToken);

        return new PageResult<ConvenienceStoreOptionDto>(items, pageNumber, pageSize, totalCount);
    }
}
