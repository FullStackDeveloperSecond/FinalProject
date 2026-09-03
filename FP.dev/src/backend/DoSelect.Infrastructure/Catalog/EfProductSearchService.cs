using System.Text;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfProductSearchService : IProductSearchService
{
    private readonly DoSelectDbContext _dbContext;

    public EfProductSearchService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<ProductCardDto>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sort = ProductSearchQueryValidator.NormalizeSort(query.Sort);
        var nowUtc = DateTime.UtcNow;

        var activeSalePrices = _dbContext.SalePrices.AsNoTracking()
            .Where(salePrice =>
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= nowUtc &&
                salePrice.EndsAtUtc > nowUtc);

        IQueryable<CatalogSearchRow> rows =
            from product in _dbContext.Products.AsNoTracking()
            where product.Status == ProductStatus.Published
            join sku in _dbContext.Skus.AsNoTracking()
                    .Where(candidate => candidate.IsDefault && candidate.Status == SkuStatus.Published)
                on product.Id equals sku.ProductId
            join brand in _dbContext.Brands.AsNoTracking() on product.BrandId equals brand.Id
            join category in _dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join balance in _dbContext.InventoryBalances.AsNoTracking()
                on sku.Id equals balance.SkuId into balanceGroup
            from balance in balanceGroup.DefaultIfEmpty()
            join salePrice in activeSalePrices on sku.Id equals salePrice.SkuId into saleGroup
            from salePrice in saleGroup.DefaultIfEmpty()
            select new CatalogSearchRow
            {
                Product = product,
                Sku = sku,
                Brand = brand,
                Category = category,
                Balance = balance,
                SalePrice = salePrice != null ? salePrice.Price : (decimal?)null,
            };

        long? categoryId = null;
        if (!string.IsNullOrWhiteSpace(query.CategoryCode))
        {
            var categoryCode = NormalizeCode(query.CategoryCode);
            categoryId = await _dbContext.Categories.AsNoTracking()
                .Where(category => category.Code == categoryCode)
                .Select(category => (long?)category.Id)
                .FirstOrDefaultAsync(cancellationToken);
            rows = rows.Where(row => row.Category.Code == categoryCode);
        }

        if (!string.IsNullOrWhiteSpace(query.BrandCode))
        {
            var brandCode = NormalizeCode(query.BrandCode);
            rows = rows.Where(row => row.Brand.Code == brandCode);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            rows = rows.Where(row =>
                row.Product.NameZhTw.Contains(keyword) ||
                row.Product.ProductCode.Contains(keyword) ||
                row.Sku.NameZhTw.Contains(keyword) ||
                row.Sku.SkuCode.Contains(keyword));
        }

        if (query.MinPrice.HasValue)
        {
            var minPrice = query.MinPrice.Value;
            rows = rows.Where(row => (row.SalePrice ?? row.Sku.ListPrice) >= minPrice);
        }

        if (query.MaxPrice.HasValue)
        {
            var maxPrice = query.MaxPrice.Value;
            rows = rows.Where(row => (row.SalePrice ?? row.Sku.ListPrice) <= maxPrice);
        }

        // UC-SEARCH-01: a delisted/disabled/unsellable item must never appear in
        // purchasable results, so out-of-stock is excluded whenever InStock isn't
        // explicitly false. InStock=false is the caller's explicit opt-out (e.g. a
        // future "notify me when back in stock" view) — it does not mean "show only
        // out-of-stock items".
        if (query.InStock != false)
        {
            rows = rows.Where(row => row.Balance != null && row.Balance.AvailableQuantity > 0);
        }

        if (query.Specs.Count > 0 && categoryId is null)
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                "Specification filters require a category to be selected.");
        }

        foreach (var filter in query.Specs)
        {
            rows = await ApplySpecFilterAsync(rows, filter, categoryId!.Value, cancellationToken);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        rows = ApplySort(rows, sort, query.Keyword);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 50 ? 20 : query.PageSize;

        // (pageNumber - 1) * pageSize can overflow int for a large pageNumber (e.g.
        // int.MaxValue). Compute in long first; a skip beyond int.MaxValue can never land on
        // a real row in this table, so it's a legal empty page rather than an error — no
        // need to round-trip to the database for a page number that could never have data.
        var skip = (long)(pageNumber - 1) * pageSize;
        var pageRows = skip > int.MaxValue
            ? []
            : await rows
                .Skip((int)skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        var items = await MapToCardsAsync(pageRows, query.Locale, cancellationToken);

        return new PageResult<ProductCardDto>(items, pageNumber, pageSize, totalCount);
    }

    private async Task<IQueryable<CatalogSearchRow>> ApplySpecFilterAsync(
        IQueryable<CatalogSearchRow> source,
        SpecFilter filter,
        long categoryId,
        CancellationToken cancellationToken)
    {
        var semanticKey = NormalizeCode(filter.SemanticKey);
        // Scoped to the selected category's own public (IsActive) definitions — this is
        // the same whitelist EfCatalogFilterOptionsService.GetSpecificationFiltersAsync
        // already exposes to the UI, so a caller can only ever filter on a field it was
        // actually offered for that category.
        var definitions = await _dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(definition =>
                definition.CategoryId == categoryId &&
                definition.SemanticKey == semanticKey &&
                definition.IsActive)
            .Select(definition => new
            {
                definition.Id,
                definition.ValueType,
                definition.AllowsMultiple,
            })
            .ToListAsync(cancellationToken);

        if (definitions.Count == 0)
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                $"The specification '{filter.SemanticKey}' is not recognized.");
        }

        // Scoped to categoryId above, so a semantic key resolves to at most one
        // definition here — no cross-category ambiguity to resolve.
        var valueType = definitions[0].ValueType;
        var definitionIds = definitions.Select(definition => definition.Id).ToArray();

        switch (filter.Operator)
        {
            case SpecFilterOperator.Eq when valueType == SpecificationValueType.String:
                RequireValue(filter);
                return source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                    value.SkuId == row.Sku.Id &&
                    definitionIds.Contains(value.SpecificationDefinitionId) &&
                    value.StringValue == filter.Value));

            case SpecFilterOperator.Eq when valueType == SpecificationValueType.Decimal:
                var equalsDecimal = ParseDecimal(filter.Value, filter.SemanticKey);
                return source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                    value.SkuId == row.Sku.Id &&
                    definitionIds.Contains(value.SpecificationDefinitionId) &&
                    value.DecimalValue == equalsDecimal));

            case SpecFilterOperator.Eq when valueType == SpecificationValueType.Boolean:
                var equalsBoolean = ParseBoolean(filter.Value, filter.SemanticKey);
                return source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                    value.SkuId == row.Sku.Id &&
                    definitionIds.Contains(value.SpecificationDefinitionId) &&
                    value.BooleanValue == equalsBoolean));

            case SpecFilterOperator.Eq when valueType == SpecificationValueType.Option:
                var optionId = await ResolveOptionIdAsync(definitions[0].Id, filter.Value, cancellationToken);
                return definitions[0].AllowsMultiple
                    ? source.Where(row => _dbContext.SkuSpecificationOptionSelections.Any(selection =>
                        selection.SkuId == row.Sku.Id &&
                        selection.SpecificationOptionId == optionId))
                    : source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                        value.SkuId == row.Sku.Id &&
                        definitionIds.Contains(value.SpecificationDefinitionId) &&
                        value.OptionId == optionId));

            case SpecFilterOperator.Gte when valueType == SpecificationValueType.Decimal:
                var gteDecimal = ParseDecimal(filter.Value, filter.SemanticKey);
                return source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                    value.SkuId == row.Sku.Id &&
                    definitionIds.Contains(value.SpecificationDefinitionId) &&
                    value.DecimalValue >= gteDecimal));

            case SpecFilterOperator.Lte when valueType == SpecificationValueType.Decimal:
                var lteDecimal = ParseDecimal(filter.Value, filter.SemanticKey);
                return source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                    value.SkuId == row.Sku.Id &&
                    definitionIds.Contains(value.SpecificationDefinitionId) &&
                    value.DecimalValue <= lteDecimal));

            case SpecFilterOperator.In when valueType == SpecificationValueType.Option:
                var optionCodes = filter.Values ?? [];
                if (optionCodes.Count is 0 or > 10)
                {
                    throw new CatalogSearchException(
                        CatalogSearchException.ErrorCodes.FilterUnsupported,
                        $"The specification '{filter.SemanticKey}' requires 1 to 10 values for 'in'.");
                }

                var optionIds = new List<long>(optionCodes.Count);
                foreach (var code in optionCodes)
                {
                    optionIds.Add(await ResolveOptionIdAsync(definitions[0].Id, code, cancellationToken));
                }

                return definitions[0].AllowsMultiple
                    ? source.Where(row => _dbContext.SkuSpecificationOptionSelections.Any(selection =>
                        selection.SkuId == row.Sku.Id &&
                        optionIds.Contains(selection.SpecificationOptionId)))
                    : source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                        value.SkuId == row.Sku.Id &&
                        definitionIds.Contains(value.SpecificationDefinitionId) &&
                        value.OptionId != null &&
                        optionIds.Contains(value.OptionId.Value)));

            case SpecFilterOperator.In when valueType == SpecificationValueType.String:
                var stringValues = filter.Values ?? [];
                if (stringValues.Count is 0 or > 10)
                {
                    throw new CatalogSearchException(
                        CatalogSearchException.ErrorCodes.FilterUnsupported,
                        $"The specification '{filter.SemanticKey}' requires 1 to 10 values for 'in'.");
                }

                return source.Where(row => _dbContext.SkuSpecificationValues.Any(value =>
                    value.SkuId == row.Sku.Id &&
                    definitionIds.Contains(value.SpecificationDefinitionId) &&
                    value.StringValue != null &&
                    stringValues.Contains(value.StringValue)));

            default:
                throw new CatalogSearchException(
                    CatalogSearchException.ErrorCodes.FilterUnsupported,
                    $"The operator is not supported for specification '{filter.SemanticKey}'.");
        }
    }

    private async Task<long> ResolveOptionIdAsync(
        long definitionId,
        string? code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                "A specification option value is required.");
        }

        var normalized = NormalizeCode(code);
        var optionId = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(option =>
                option.SpecificationDefinitionId == definitionId &&
                option.Code == normalized &&
                option.IsActive)
            .Select(option => (long?)option.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (optionId is null)
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                $"The specification option '{code}' is not recognized.");
        }

        return optionId.Value;
    }

    private static void RequireValue(SpecFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Value))
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                $"A value is required for specification '{filter.SemanticKey}'.");
        }
    }

    private static decimal ParseDecimal(string? value, string semanticKey)
    {
        if (!decimal.TryParse(value, out var parsed))
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                $"The value for specification '{semanticKey}' must be a decimal.");
        }

        return parsed;
    }

    private static bool ParseBoolean(string? value, string semanticKey)
    {
        if (!bool.TryParse(value, out var parsed))
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                $"The value for specification '{semanticKey}' must be true or false.");
        }

        return parsed;
    }

    private static IQueryable<CatalogSearchRow> ApplySort(
        IQueryable<CatalogSearchRow> source,
        string sort,
        string? keyword)
    {
        return sort switch
        {
            ProductSortOptions.PriceAsc => source
                .OrderBy(row => row.SalePrice ?? row.Sku.ListPrice)
                .ThenByDescending(row => row.Product.CreatedAtUtc)
                .ThenBy(row => row.Sku.SkuCode),
            ProductSortOptions.PriceDesc => source
                .OrderByDescending(row => row.SalePrice ?? row.Sku.ListPrice)
                .ThenByDescending(row => row.Product.CreatedAtUtc)
                .ThenBy(row => row.Sku.SkuCode),
            ProductSortOptions.Newest => source
                .OrderByDescending(row => row.Product.CreatedAtUtc)
                .ThenBy(row => row.Sku.SkuCode),
            _ => ApplyRelevanceSort(source, keyword),
        };
    }

    private static IQueryable<CatalogSearchRow> ApplyRelevanceSort(
        IQueryable<CatalogSearchRow> source,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            // KNOWN GAP — UC-SEARCH-01 is not fully implemented here: it asks for "近期
            // 銷售熱度" (recent sales heat), but that needs a queryable order-history
            // aggregate that doesn't exist yet — the Orders module has no such summary to
            // read from. Deliberately narrowing this PR's scope to a featured/recency proxy
            // (same treatment as the SH-06 image-service gaps elsewhere in this file) rather
            // than guessing a formula; swap this for the real sales-heat ordering once that
            // data is queryable. Do not treat this PR as a complete UC-SEARCH-01 delivery.
            return source
                .OrderByDescending(row => row.Product.IsFeatured)
                .ThenByDescending(row => row.Product.CreatedAtUtc)
                .ThenBy(row => row.Sku.SkuCode);
        }

        var trimmed = keyword.Trim();
        return source
            .OrderByDescending(row =>
                row.Product.ProductCode == trimmed || row.Sku.SkuCode == trimmed
                    ? 2
                    : row.Product.NameZhTw.StartsWith(trimmed) || row.Sku.NameZhTw.StartsWith(trimmed)
                        ? 1
                        : 0)
            .ThenByDescending(row => row.Product.CreatedAtUtc)
            .ThenBy(row => row.Sku.SkuCode);
    }

    private async Task<IReadOnlyList<ProductCardDto>> MapToCardsAsync(
        IReadOnlyList<CatalogSearchRow> rows,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        var productNames = await ResolveProductNamesAsync(
            rows.Select(row => row.Product.Id),
            locale,
            cancellationToken);
        var brandNames = await ResolveBrandNamesAsync(
            rows.Select(row => row.Brand.Id),
            locale,
            cancellationToken);
        var categoryNames = await ResolveCategoryNamesAsync(
            rows.Select(row => row.Category.Id),
            locale,
            cancellationToken);
        var primaryImages = await ProductImageProjection.LoadPublishedPrimaryAsync(
            _dbContext,
            rows.Select(row => row.Product.Id).Distinct().ToArray(),
            cancellationToken);

        return rows
            .Select(row => new ProductCardDto(
                row.Product.PublicId,
                row.Sku.PublicId,
                row.Product.ProductCode,
                row.Sku.SkuCode,
                productNames.GetValueOrDefault(row.Product.Id, row.Product.NameZhTw),
                new ProductBrandRef(
                    row.Brand.Code,
                    brandNames.GetValueOrDefault(row.Brand.Id, row.Brand.NameZhTw)),
                new ProductCategoryRef(
                    row.Category.Code,
                    categoryNames.GetValueOrDefault(row.Category.Id, row.Category.NameZhTw)),
                new ProductPrice(row.Sku.ListPrice, row.SalePrice, "TWD"),
                ResolveAvailability(row.Balance),
                primaryImages.GetValueOrDefault(row.Product.Id),
                row.Product.IsFeatured ? ["featured"] : Array.Empty<string>()))
            .ToList();
    }

    private async Task<Dictionary<long, string>> ResolveProductNamesAsync(
        IEnumerable<long> productIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw)
        {
            return new Dictionary<long, string>();
        }

        var ids = productIds.Distinct().ToArray();
        return await _dbContext.ProductTranslations.AsNoTracking()
            .Where(translation =>
                ids.Contains(translation.ProductId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(translation => translation.ProductId, translation => translation.Name, cancellationToken);
    }

    private async Task<Dictionary<long, string>> ResolveBrandNamesAsync(
        IEnumerable<long> brandIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw)
        {
            return new Dictionary<long, string>();
        }

        var ids = brandIds.Distinct().ToArray();
        return await _dbContext.BrandTranslations.AsNoTracking()
            .Where(translation =>
                ids.Contains(translation.BrandId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(translation => translation.BrandId, translation => translation.Name, cancellationToken);
    }

    private async Task<Dictionary<long, string>> ResolveCategoryNamesAsync(
        IEnumerable<long> categoryIds,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (locale == SupportedLocale.ZhTw)
        {
            return new Dictionary<long, string>();
        }

        var ids = categoryIds.Distinct().ToArray();
        return await _dbContext.CategoryTranslations.AsNoTracking()
            .Where(translation =>
                ids.Contains(translation.CategoryId) &&
                translation.Locale == locale &&
                translation.TranslationStatus == TranslationStatus.Published)
            .ToDictionaryAsync(translation => translation.CategoryId, translation => translation.Name, cancellationToken);
    }

    private static string ResolveAvailability(InventoryBalance? balance)
    {
        if (balance is null || balance.AvailableQuantity <= 0)
        {
            return ProductAvailabilityCodes.OutOfStock;
        }

        return balance.AvailableQuantity <= balance.ReorderLevel
            ? ProductAvailabilityCodes.LowStock
            : ProductAvailabilityCodes.InStock;
    }

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private sealed class CatalogSearchRow
    {
        public required Product Product { get; init; }

        public required Sku Sku { get; init; }

        public required Brand Brand { get; init; }

        public required Category Category { get; init; }

        public InventoryBalance? Balance { get; init; }

        public decimal? SalePrice { get; init; }
    }
}
