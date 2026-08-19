using System.Text;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfProductAdminService : IProductAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfProductAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<AdminProductSummaryDto>> ListAsync(
        AdminProductQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var products =
            from product in _dbContext.Products.AsNoTracking()
            join brand in _dbContext.Brands.AsNoTracking() on product.BrandId equals brand.Id
            join category in _dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            select new { product, brand, category };

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            products = products.Where(row =>
                row.product.ProductCode.Contains(keyword) || row.product.NameZhTw.Contains(keyword));
        }

        if (query.BrandCodes is { Count: > 0 })
        {
            var brandCodes = query.BrandCodes.Select(NormalizeCode).ToArray();
            products = products.Where(row => brandCodes.Contains(row.brand.Code));
        }

        if (query.CategoryCodes is { Count: > 0 })
        {
            var categoryCodes = query.CategoryCodes.Select(NormalizeCode).ToArray();
            products = products.Where(row => categoryCodes.Contains(row.category.Code));
        }

        if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses
                .Select(status => Enum.Parse<ProductStatus>(status, ignoreCase: true))
                .ToArray();
            products = products.Where(row => statuses.Contains(row.product.Status));
        }

        var totalCount = await products.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var page = await products
            .OrderByDescending(row => row.product.UpdatedAtUtc)
            .ThenBy(row => row.product.ProductCode)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var productIds = page.Select(row => row.product.Id).ToArray();
        var skusByProduct = await _dbContext.Skus.AsNoTracking()
            .Where(sku => productIds.Contains(sku.ProductId))
            .ToListAsync(cancellationToken);
        var skuIds = skusByProduct.Select(sku => sku.Id).ToArray();
        var balancesBySku = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

        var items = page.Select(row =>
        {
            var skus = skusByProduct.Where(sku => sku.ProductId == row.product.Id).ToList();
            var onHand = skus.Sum(sku => balancesBySku.GetValueOrDefault(sku.Id)?.OnHandQuantity ?? 0);

            return new AdminProductSummaryDto(
                row.product.PublicId,
                row.product.ProductCode,
                row.product.NameZhTw,
                new ProductBrandRef(row.brand.Code, row.brand.NameZhTw),
                new ProductCategoryRef(row.category.Code, row.category.NameZhTw),
                row.product.Status.ToString(),
                skus.Count,
                skus.Count == 0 ? 0 : skus.Min(sku => sku.ListPrice),
                skus.Count == 0 ? 0 : skus.Max(sku => sku.ListPrice),
                onHand,
                // Deferred with the shared file service (SH-06); see EfProductSearchService.
                null,
                row.product.UpdatedAtUtc,
                row.product.RowVersion);
        }).ToList();

        if (query.StockState is not (null or "any"))
        {
            items = FilterByStockState(items, query.StockState).ToList();
        }

        return new PageResult<AdminProductSummaryDto>(items, pageNumber, pageSize, totalCount);
    }

    public async Task<AdminProductDetailDto?> GetByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        return await BuildDetailAsync(product, cancellationToken);
    }

    public async Task<AdminProductDetailDto> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedCode = NormalizeCode(request.ProductCode);
        var codeExists = await _dbContext.Products.AsNoTracking()
            .AnyAsync(candidate => candidate.ProductCode == normalizedCode, cancellationToken);
        if (codeExists)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ProductCodeDuplicate,
                $"Product code '{request.ProductCode}' already exists.");
        }

        var brandId = await ResolveIdAsync<Brand>(request.BrandPublicId, "Brand", cancellationToken);
        var categoryId = await ResolveIdAsync<Category>(request.CategoryPublicId, "Category", cancellationToken);
        var status = ParseStatus(request.Status);

        if (request.WarrantyMonths is < 0 or > 120)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "Warranty months must be between 0 and 120.");
        }

        var now = DateTime.UtcNow;
        var product = new Product(
            Guid.CreateVersion7(),
            request.ProductCode,
            brandId,
            categoryId,
            request.NameZhTw,
            now);
        product.UpdateDetails(
            brandId,
            categoryId,
            request.NameZhTw,
            request.DescriptionZhTw,
            request.WarrantyMonths,
            isFeatured: false,
            now);
        product.ChangeStatus(status, now);

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await ReplaceTagsAsync(product.Id, request.TagPublicIds, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await BuildDetailAsync(product, cancellationToken))!;
    }

    public async Task<AdminProductDetailDto> UpdateAsync(
        Guid publicId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        if (product is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"Product '{publicId}' was not found.");
        }

        var brandId = await ResolveIdAsync<Brand>(request.BrandPublicId, "Brand", cancellationToken);
        var categoryId = await ResolveIdAsync<Category>(request.CategoryPublicId, "Category", cancellationToken);
        var status = ParseStatus(request.Status);

        if (request.WarrantyMonths is < 0 or > 120)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "Warranty months must be between 0 and 120.");
        }

        _dbContext.Entry(product).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        product.UpdateDetails(
            brandId,
            categoryId,
            request.NameZhTw,
            request.DescriptionZhTw,
            request.WarrantyMonths,
            product.IsFeatured,
            now);
        product.ChangeStatus(status, now);

        await ReplaceTagsAsync(product.Id, request.TagPublicIds, now, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The product was updated by someone else. Reload and try again.");
        }

        return (await BuildDetailAsync(product, cancellationToken))!;
    }

    private async Task ReplaceTagsAsync(
        long productId,
        IReadOnlyList<Guid> tagPublicIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var distinctTagPublicIds = tagPublicIds.Distinct().ToArray();
        var tagIds = await _dbContext.Tags.AsNoTracking()
            .Where(tag => distinctTagPublicIds.Contains(tag.PublicId))
            .Select(tag => new { tag.Id, tag.PublicId })
            .ToListAsync(cancellationToken);
        if (tagIds.Count != distinctTagPublicIds.Length)
        {
            var missing = distinctTagPublicIds.Except(tagIds.Select(tag => tag.PublicId));
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ReferenceNotFound,
                $"Unknown tag reference(s): {string.Join(", ", missing)}.");
        }

        var existingLinks = await _dbContext.ProductTags
            .Where(link => link.ProductId == productId)
            .ToListAsync(cancellationToken);
        _dbContext.ProductTags.RemoveRange(existingLinks);

        foreach (var tag in tagIds)
        {
            _dbContext.ProductTags.Add(new ProductTag(productId, tag.Id, now));
        }
    }

    private async Task<AdminProductDetailDto> BuildDetailAsync(Product product, CancellationToken cancellationToken)
    {
        var brand = await _dbContext.Brands.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == product.BrandId, cancellationToken);
        var category = await _dbContext.Categories.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == product.CategoryId, cancellationToken);
        var tags = await (
            from productTag in _dbContext.ProductTags.AsNoTracking()
            join tag in _dbContext.Tags.AsNoTracking() on productTag.TagId equals tag.Id
            where productTag.ProductId == product.Id
            orderby tag.SortOrder
            select new TagRef(tag.Code, tag.NameZhTw)).ToListAsync(cancellationToken);

        var skus = await _dbContext.Skus.AsNoTracking()
            .Where(sku => sku.ProductId == product.Id)
            .OrderByDescending(sku => sku.IsDefault)
            .ThenBy(sku => sku.SkuCode)
            .ToListAsync(cancellationToken);
        var skuDtos = new List<SkuDto>(skus.Count);
        foreach (var sku in skus)
        {
            skuDtos.Add(await SkuAdminMapping.ToDtoAsync(_dbContext, sku, product, cancellationToken));
        }

        return new AdminProductDetailDto(
            product.PublicId,
            product.ProductCode,
            product.NameZhTw,
            new ProductBrandRef(brand.Code, brand.NameZhTw),
            new ProductCategoryRef(category.Code, category.NameZhTw),
            product.DescriptionZhTw,
            product.WarrantyMonths,
            product.Status.ToString(),
            product.IsFeatured,
            tags,
            // Deferred with the shared file service (SH-06); see EfProductSearchService.
            [],
            skuDtos,
            product.CreatedAtUtc,
            product.UpdatedAtUtc,
            product.RowVersion);
    }

    private async Task<long> ResolveIdAsync<TEntity>(
        Guid publicId,
        string label,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var id = await _dbContext.Set<TEntity>().AsNoTracking()
            .Where(entity => EF.Property<Guid>(entity, "PublicId") == publicId)
            .Select(entity => EF.Property<long>(entity, "Id"))
            .Cast<long?>()
            .FirstOrDefaultAsync(cancellationToken);

        if (id is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ReferenceNotFound,
                $"{label} '{publicId}' was not found.");
        }

        return id.Value;
    }

    private static ProductStatus ParseStatus(string status)
    {
        if (!Enum.TryParse<ProductStatus>(status, ignoreCase: true, out var parsed))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"The product status '{status}' is not supported.");
        }

        return parsed;
    }

    private static IEnumerable<AdminProductSummaryDto> FilterByStockState(
        IEnumerable<AdminProductSummaryDto> items,
        string? stockState) => stockState switch
        {
            "outOfStock" => items.Where(item => item.TotalOnHandQuantity <= 0),
            "inStock" => items.Where(item => item.TotalOnHandQuantity > 0),
            _ => items,
        };

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
