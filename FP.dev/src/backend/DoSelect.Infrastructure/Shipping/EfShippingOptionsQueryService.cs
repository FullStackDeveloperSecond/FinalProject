using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

public sealed class EfShippingOptionsQueryService : IShippingOptionsQueryService
{
    private readonly DoSelectDbContext _dbContext;

    public EfShippingOptionsQueryService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ShippingOptionsDto> GetShippingOptionsAsync(CancellationToken cancellationToken)
    {
        var methods = await _dbContext.ShippingMethods.AsNoTracking()
            .Where(method => method.IsActive)
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.Code)
            .Select(method => new ShippingMethodOptionDto(
                method.Code, method.NameZhTw, method.BaseFee, method.FreeShippingThreshold,
                method.AllowsCod, method.RequiresPrepayment))
            .ToListAsync(cancellationToken);

        return new ShippingOptionsDto(methods);
    }

    public async Task<PageResult<ConvenienceStoreOptionDto>> SearchConvenienceStoresAsync(
        ConvenienceStoreQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stores = _dbContext.ConvenienceStores.AsNoTracking()
            .Where(store => store.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            stores = stores.Where(store =>
                store.StoreName.Contains(keyword) || store.StoreCode.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            stores = stores.Where(store => store.City == query.City);
        }

        if (!string.IsNullOrWhiteSpace(query.District))
        {
            stores = stores.Where(store => store.District == query.District);
        }

        var totalCount = await stores.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var skip = (long)(pageNumber - 1) * pageSize;
        var entities = skip > int.MaxValue
            ? []
            : await stores
                .OrderBy(store => store.ProviderCode)
                .ThenBy(store => store.StoreCode)
                .Skip((int)skip)
                .Take(pageSize)
                .Select(store => new ConvenienceStoreOptionDto(
                    store.PublicId, store.ProviderCode, store.StoreCode, store.StoreName,
                    store.Address, store.City, store.District))
                .ToListAsync(cancellationToken);

        return new PageResult<ConvenienceStoreOptionDto>(entities, pageNumber, pageSize, totalCount);
    }
}
