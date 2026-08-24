using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

public sealed class EfConvenienceStoreAdminService : IConvenienceStoreAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfConvenienceStoreAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<ConvenienceStoreDto>> ListAsync(
        ConvenienceStoreAdminQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stores = _dbContext.ConvenienceStores.AsNoTracking().AsQueryable();

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

        if (query.IsActive.HasValue)
        {
            stores = stores.Where(store => store.IsActive == query.IsActive.Value);
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
                .ToListAsync(cancellationToken);

        return new PageResult<ConvenienceStoreDto>(entities.Select(ToDto).ToList(), pageNumber, pageSize, totalCount);
    }

    public async Task<ConvenienceStoreDto> CreateAsync(
        CreateConvenienceStoreRequest request, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerCode = request.ProviderCode.Trim();
        var storeCode = request.StoreCode.Trim();
        var exists = await _dbContext.ConvenienceStores.AsNoTracking()
            .AnyAsync(store => store.ProviderCode == providerCode && store.StoreCode == storeCode, cancellationToken);
        if (exists)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.StoreCodeDuplicate,
                $"Store code '{request.StoreCode}' already exists for provider '{request.ProviderCode}'.");
        }

        var store = new ConvenienceStore(
            Guid.CreateVersion7(), providerCode, storeCode, request.StoreName.Trim(),
            request.Address.Trim(), request.City.Trim(), request.District.Trim(),
            isDemoData: false, now);

        _dbContext.ConvenienceStores.Add(store);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(store);
    }

    public async Task<ConvenienceStoreDto> UpdateAsync(
        Guid publicId, UpdateConvenienceStoreRequest request, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var store = await _dbContext.ConvenienceStores
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        if (store is null)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ResourceNotFound,
                $"Convenience store '{publicId}' was not found.");
        }

        _dbContext.Entry(store).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        store.UpdateDetails(request.StoreName, request.Address, request.City, request.District, now);
        store.SetActive(request.IsActive, now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ConcurrencyConflict,
                "The convenience store was updated by someone else. Reload and try again.");
        }

        return ToDto(store);
    }

    private static ConvenienceStoreDto ToDto(ConvenienceStore store) => new(
        store.PublicId, store.ProviderCode, store.StoreCode, store.StoreName, store.Address,
        store.City, store.District, store.IsDemoData, store.IsActive, store.RowVersion);
}
