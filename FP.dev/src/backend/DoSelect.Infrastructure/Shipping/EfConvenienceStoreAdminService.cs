using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// No delete action exists anywhere in this service or its controller — 購物車、訂單、付款與物流.md's
/// "已被購物車或訂單引用的門市不得實體刪除；停用後不可供新訂單選擇" is enforced by construction (deactivate,
/// via Update, is the only removal path) rather than by a runtime reference check.
/// </summary>
public sealed class EfConvenienceStoreAdminService : IConvenienceStoreAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfConvenienceStoreAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<ConvenienceStoreDto>> ListAsync(
        AdminConvenienceStoreQuery query,
        CancellationToken cancellationToken)
    {
        var stores = _dbContext.ConvenienceStores.AsNoTracking().AsQueryable();

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

        if (query.IsActive.HasValue)
        {
            stores = stores.Where(store => store.IsActive == query.IsActive.Value);
        }

        var totalCount = await stores.CountAsync(cancellationToken);
        var items = await stores
            .OrderByDescending(store => store.UpdatedAtUtc)
            .ThenByDescending(store => store.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(store => ToDto(store))
            .ToListAsync(cancellationToken);

        return new PageResult<ConvenienceStoreDto>(items, query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<ConvenienceStoreDto> CreateAsync(
        CreateConvenienceStoreRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var duplicate = await _dbContext.ConvenienceStores.AnyAsync(
            store => store.ProviderCode == request.ProviderCode && store.StoreCode == request.StoreCode,
            cancellationToken);
        if (duplicate)
        {
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.StoreCodeDuplicate);
        }

        var store = new ConvenienceStore(
            Guid.CreateVersion7(),
            request.ProviderCode,
            request.StoreCode,
            request.StoreName,
            request.Address,
            request.City,
            request.District,
            // v1 has no real carrier API integration at all (see 購物車、訂單、付款與物流.md's 超商門市維護 —
            // "無真實物流商API整合"), so every store this admin CRUD can create is inherently demo/
            // showcase data, not an import from a real provider feed.
            isDemoData: true,
            DateTime.UtcNow);

        try
        {
            _dbContext.ConvenienceStores.Add(store);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raceLostToDuplicate = await _dbContext.ConvenienceStores.AnyAsync(
                existing => existing.ProviderCode == request.ProviderCode && existing.StoreCode == request.StoreCode,
                cancellationToken);
            if (!raceLostToDuplicate)
            {
                throw;
            }

            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.StoreCodeDuplicate);
        }

        return ToDto(store);
    }

    public async Task<ConvenienceStoreDto> UpdateAsync(
        Guid publicId,
        UpdateConvenienceStoreRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var store = await _dbContext.ConvenienceStores.SingleOrDefaultAsync(
            candidate => candidate.PublicId == publicId, cancellationToken)
            ?? throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ResourceNotFound);

        _dbContext.Entry(store).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;
        store.UpdateDetails(request.StoreName, request.Address, request.City, request.District, request.IsActive, DateTime.UtcNow);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ConcurrencyConflict);
        }

        return ToDto(store);
    }

    private static ConvenienceStoreDto ToDto(ConvenienceStore store) => new(
        store.PublicId,
        store.ProviderCode,
        store.StoreCode,
        store.StoreName,
        store.Address,
        store.City,
        store.District,
        store.IsDemoData,
        store.IsActive,
        store.RowVersion);
}
