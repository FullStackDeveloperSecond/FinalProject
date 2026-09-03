using System.Text;
using DoSelect.Application.Auditing;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed partial class EfProductAdminService : IProductAdminService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;

    public EfProductAdminService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
    }

    public async Task<PageResult<AdminProductSummaryDto>> ListAsync(
        AdminProductQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // 匯出與列表共用同一個 Filter（Endpoint 目錄：「匯出沿用目前 Filter」）。共用的是同一段
        // 程式而不是兩份長得像的程式——否則哪天列表加了條件，匯出就會安靜地匯出另一組資料。
        var products = BuildFilteredRows(query);

        var totalCount = await products.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var sort = AdminCatalogQueryValidator.NormalizeSort(query.Sort);
        products = sort switch
        {
            AdminProductSortOptions.UpdatedAsc => products
                .OrderBy(row => row.Product.UpdatedAtUtc)
                .ThenBy(row => row.Product.ProductCode),
            AdminProductSortOptions.CodeAsc => products
                .OrderBy(row => row.Product.ProductCode),
            AdminProductSortOptions.CodeDesc => products
                .OrderByDescending(row => row.Product.ProductCode),
            _ => products
                .OrderByDescending(row => row.Product.UpdatedAtUtc)
                .ThenBy(row => row.Product.ProductCode),
        };

        // (pageNumber - 1) * pageSize can overflow int for a large pageNumber (e.g.
        // int.MaxValue). Compute in long first; a skip beyond int.MaxValue can never land on
        // a real row in this table, so it's a legal empty page rather than an error — no
        // need to round-trip to the database for a page number that could never have data.
        // Mirrors the fix in EfProductSearchService.cs (catalog-search-api PR #22).
        var skip = (long)(pageNumber - 1) * pageSize;
        var page = skip > int.MaxValue
            ? []
            : await products
                .Skip((int)skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        var productIds = page.Select(row => row.Product.Id).ToArray();
        var skusByProduct = await _dbContext.Skus.AsNoTracking()
            .Where(sku => productIds.Contains(sku.ProductId))
            .ToListAsync(cancellationToken);
        var skuIds = skusByProduct.Select(sku => sku.Id).ToArray();
        var balancesBySku = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);
        var primaryImages = await ProductImageProjection.LoadAdminPrimaryAsync(_dbContext, productIds, cancellationToken);

        var items = page.Select(row =>
        {
            var skus = skusByProduct.Where(sku => sku.ProductId == row.Product.Id).ToList();
            var onHand = skus.Sum(sku => balancesBySku.GetValueOrDefault(sku.Id)?.OnHandQuantity ?? 0);

            return new AdminProductSummaryDto(
                row.Product.PublicId,
                row.Product.ProductCode,
                row.Product.NameZhTw,
                new ProductBrandRef(row.Brand.Code, row.Brand.NameZhTw),
                new ProductCategoryRef(row.Category.Code, row.Category.NameZhTw),
                row.Product.Status.ToString(),
                skus.Count,
                skus.Count == 0 ? 0 : skus.Min(sku => sku.ListPrice),
                skus.Count == 0 ? 0 : skus.Max(sku => sku.ListPrice),
                onHand,
                primaryImages.GetValueOrDefault(row.Product.Id),
                row.Product.UpdatedAtUtc,
                row.Product.RowVersion);
        }).ToList();

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

        if (request.DefaultSku is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "A default SKU is required when creating a product.");
        }

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

        // 組長 PR #24 round 4 review, item 6: API DTO與Schema契約.md documents tagPublicIds as
        // 0..20, but nothing enforced it — a caller (or a buggy client) could attach unbounded tags.
        if (request.TagPublicIds.Count > 20)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "A product accepts at most 20 tags.");
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

        // Product tags and SKU specification values both need database-assigned bigint keys,
        // so this use case necessarily spans multiple SaveChanges calls. The outer
        // transaction owns the whole operation; EfSkuAdminService detects the ambient
        // transaction and reuses it instead of committing independently.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            await ReplaceTagsAsync(product.Id, request.TagPublicIds, now, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var skuService = new EfSkuAdminService(_dbContext);
            await skuService.CreateAsync(
                product.PublicId,
                request.DefaultSku with { IsDefault = true },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        // 組長 PR #24 round 10 review, P2: the AnyAsync check above is a plain SELECT before the
        // INSERT — two concurrent creates for the same brand-new ProductCode can both pass it, and
        // only one INSERT actually wins; the loser used to surface as a bare rethrow, which
        // GlobalExceptionHandler maps to an opaque 500 instead of the documented 409
        // product_code_duplicate. Rollback always happens first regardless of exception type; only
        // a genuine UX_Products_ProductCode violation gets translated, so an unrelated
        // DbUpdateException (a different constraint, connectivity failure) still propagates as-is
        // rather than being mislabeled as a duplicate code.
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);

            if (exception is DbUpdateException dbUpdateException &&
                SqlUniqueIndexViolations.Matches(dbUpdateException, "UX_Products_ProductCode"))
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.ProductCodeDuplicate,
                    $"Product code '{request.ProductCode}' already exists.");
            }

            throw;
        }

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

        // 組長 PR #24 round 4+5 review, item 3/1: changing category while existing SKUs still
        // carry the old category's specification values would leave the product detail page
        // showing stale specs while search/filter behavior already switched to the new category
        // — lowest-cost fix per his ruling is to reject outright rather than attempt an atomic
        // spec remap now; that's future work if it's ever actually needed. Round 5 found this
        // alone still let a Published SKU with *empty* specs slip through: switch to a category
        // with a required spec, and the now-Published SKU immediately violates it — so this also
        // rejects when any Published SKU exists and the target category has any required spec,
        // regardless of whether specs are currently populated.
        if (categoryId != product.CategoryId)
        {
            var hasExistingSpecValues = await _dbContext.SkuSpecificationValues.AsNoTracking()
                .AnyAsync(value => _dbContext.Skus
                    .Where(sku => sku.ProductId == product.Id)
                    .Select(sku => sku.Id)
                    .Contains(value.SkuId), cancellationToken);
            hasExistingSpecValues = hasExistingSpecValues ||
                await _dbContext.SkuSpecificationOptionSelections.AsNoTracking()
                    .AnyAsync(selection => _dbContext.Skus
                        .Where(sku => sku.ProductId == product.Id)
                        .Select(sku => sku.Id)
                        .Contains(selection.SkuId), cancellationToken);
            if (hasExistingSpecValues)
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.ValidationFailed,
                    "Cannot change category while this product's SKUs still carry specification values from the old category.");
            }

            // Hard compatibility facts use the canonical multi-value specification model,
            // which is already covered by hasExistingSpecValues above.

            var hasPublishedSku = await _dbContext.Skus.AsNoTracking()
                .AnyAsync(sku => sku.ProductId == product.Id && sku.Status == SkuStatus.Published, cancellationToken);
            if (hasPublishedSku)
            {
                var targetCategoryHasRequiredSpec = await _dbContext.SpecificationDefinitions.AsNoTracking()
                    .AnyAsync(definition => definition.CategoryId == categoryId && definition.IsRequired && definition.IsActive, cancellationToken);
                if (targetCategoryHasRequiredSpec)
                {
                    throw new CatalogWriteException(
                        CatalogWriteException.ErrorCodes.ValidationFailed,
                        "Cannot change category while a Published SKU exists and the target category has required specifications it cannot yet satisfy.");
                }
            }
        }

        if (request.TagPublicIds.Count > 20)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "A product accepts at most 20 tags.");
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

    /// <summary>
    /// 列表與匯出共用的 Filter。回傳具名型別而不是匿名型別，方法之間才傳得過去。
    /// </summary>
    private IQueryable<AdminProductRow> BuildFilteredRows(AdminProductQuery query)
    {
        var products =
            from product in _dbContext.Products.AsNoTracking()
            join brand in _dbContext.Brands.AsNoTracking() on product.BrandId equals brand.Id
            join category in _dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            select new AdminProductRow { Product = product, Brand = brand, Category = category };

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            products = products.Where(row =>
                row.Product.ProductCode.Contains(keyword) || row.Product.NameZhTw.Contains(keyword));
        }

        if (query.BrandCodes is { Count: > 0 })
        {
            var brandCodes = query.BrandCodes.Select(NormalizeCode).ToArray();
            products = products.Where(row => brandCodes.Contains(row.Brand.Code));
        }

        if (query.CategoryCodes is { Count: > 0 })
        {
            var categoryCodes = query.CategoryCodes.Select(NormalizeCode).ToArray();
            products = products.Where(row => categoryCodes.Contains(row.Category.Code));
        }

        var statuses = AdminCatalogQueryValidator.NormalizeStatuses<ProductStatus>(query.Statuses);
        if (statuses.Count > 0)
        {
            products = products.Where(row => statuses.Contains(row.Product.Status));
        }

        var stockState = AdminCatalogQueryValidator.NormalizeStockState(query.StockState);
        if (stockState is AdminStockStates.InStock or AdminStockStates.OutOfStock)
        {
            // Correlated per-product on-hand sum, filtered before Count/Skip/Take — doing
            // this after paging (as the previous version did, in memory on an already-sliced
            // page) made totalCount wrong and could short a page or skip matching products
            // entirely. EF Core translates an empty Sum() to COALESCE(SUM(...),0), matching
            // the "no SKUs at all" == 0 on-hand semantics the old in-memory version had.
            // Local functions can't appear in an expression tree, so the correlated
            // subquery is inlined in both branches rather than shared via a helper.
            products = stockState == AdminStockStates.InStock
                ? products.Where(row => _dbContext.Skus.AsNoTracking()
                    .Where(sku => sku.ProductId == row.Product.Id)
                    .Join(
                        _dbContext.InventoryBalances.AsNoTracking(),
                        sku => sku.Id,
                        balance => balance.SkuId,
                        (sku, balance) => balance.OnHandQuantity)
                    .Sum() > 0)
                : products.Where(row => _dbContext.Skus.AsNoTracking()
                    .Where(sku => sku.ProductId == row.Product.Id)
                    .Join(
                        _dbContext.InventoryBalances.AsNoTracking(),
                        sku => sku.Id,
                        balance => balance.SkuId,
                        (sku, balance) => balance.OnHandQuantity)
                    .Sum() <= 0);
        }

        return products;
    }

    /// <summary>列表／匯出 Filter 的投影型別。</summary>
    private sealed class AdminProductRow
    {
        public required Product Product { get; init; }

        public required Brand Brand { get; init; }

        public required Category Category { get; init; }
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
            await ProductImageProjection.LoadAdminImagesAsync(_dbContext, product.Id, product.PublicId, cancellationToken),
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
        if (!AdminCatalogQueryValidator.TryParseDefinedEnumName<ProductStatus>(status, out var parsed))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"The product status '{status}' is not supported.");
        }

        return parsed;
    }

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
