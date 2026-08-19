using System.Text;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfBrandAdminService : IBrandAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfBrandAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<BrandDto>> ListAsync(
        CatalogLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var brands = _dbContext.Brands.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            brands = brands.Where(brand => brand.Code.Contains(keyword) || brand.NameZhTw.Contains(keyword));
        }

        if (query.IsActive.HasValue)
        {
            brands = brands.Where(brand => brand.IsActive == query.IsActive.Value);
        }

        var totalCount = await brands.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var entities = await brands
            .OrderBy(brand => brand.SortOrder)
            .ThenBy(brand => brand.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<BrandDto>(entities.Select(ToDto).ToList(), pageNumber, pageSize, totalCount);
    }

    public async Task<BrandDto> CreateAsync(
        CreateBrandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedCode = NormalizeCode(request.Code);
        var exists = await _dbContext.Brands.AsNoTracking()
            .AnyAsync(brand => brand.Code == normalizedCode, cancellationToken);
        if (exists)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.BrandCodeDuplicate,
                $"Brand code '{request.Code}' already exists.");
        }

        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), request.Code, request.NameZhTw, now);
        brand.UpdateDetails(request.NameZhTw, request.Description, request.WebsiteUrl, request.SortOrder, now);
        if (!request.IsActive)
        {
            brand.SetActive(false, now);
        }

        _dbContext.Brands.Add(brand);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(brand);
    }

    public async Task<BrandDto> UpdateAsync(
        Guid publicId,
        UpdateBrandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var brand = await _dbContext.Brands
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        if (brand is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"Brand '{publicId}' was not found.");
        }

        _dbContext.Entry(brand).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        brand.UpdateDetails(request.NameZhTw, request.Description, request.WebsiteUrl, request.SortOrder, now);
        brand.SetActive(request.IsActive, now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The brand was updated by someone else. Reload and try again.");
        }

        return ToDto(brand);
    }

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static BrandDto ToDto(Brand brand) => new(
        brand.PublicId,
        brand.Code,
        brand.NameZhTw,
        brand.Description,
        brand.WebsiteUrl,
        brand.IsActive,
        brand.SortOrder,
        brand.RowVersion);
}
