using System.Globalization;
using DoSelect.Application.Ai;
using DoSelect.Application.Builds;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Ai;

public sealed class EfAiProductSearchCatalog(
    DoSelectDbContext dbContext,
    IProductSearchService productSearchService,
    ICompatibilityCatalogReader compatibilityCatalogReader,
    EfCompatibilityCheckService compatibilityCheckService) : IAiProductSearchCatalog
{
    private const int MaximumCandidates = 6;
    private const int MaximumComponentCandidates = 3;
    private const int MaximumBuildEvaluations = 64;

    public async Task<AiProductSearchMetadata> ReadMetadataAsync(
        CancellationToken cancellationToken)
    {
        var categoryCodes = await dbContext.Categories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Code)
            .Select(category => category.Code)
            .ToArrayAsync(cancellationToken);
        var brandCodes = await dbContext.Brands.AsNoTracking()
            .Where(brand => brand.IsActive)
            .OrderBy(brand => brand.Code)
            .Select(brand => brand.Code)
            .ToArrayAsync(cancellationToken);
        var storedSemanticKeys = await dbContext.SpecificationDefinitions.AsNoTracking()
            .Where(definition => definition.IsActive)
            .OrderBy(definition => definition.SemanticKey)
            .Select(definition => definition.SemanticKey)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        // SearchIntent and catalog persistence share the formally approved upper-case
        // semantic-key contract. ProductSearchService still normalizes at its query boundary.
        var semanticKeys = storedSemanticKeys
            .Select(key => key.Trim().ToUpperInvariant())
            .ToArray();
        return new AiProductSearchMetadata(categoryCodes, brandCodes, semanticKeys);
    }

    public async Task<AiProductSearchCandidateResult> FindCandidatesAsync(
        AiProductSearchIntent intent,
        IReadOnlyList<AiProductSearchExistingPart> existingParts,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(existingParts);

        var metadata = await ReadMetadataAsync(cancellationToken);
        var allowedCategories = metadata.CategoryCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedBrands = metadata.BrandCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedSemanticKeys = metadata.SemanticKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var safety = AiSearchIntentSafetyValidator.Validate(
            new AiSearchIntentCandidate(intent.Budget, intent.RequiredSpecs),
            allowedSemanticKeys);
        if (!safety.IsValid ||
            (intent.CategoryCode is not null && !allowedCategories.Contains(intent.CategoryCode)) ||
            intent.PreferredBrandCodes.Any(brand => !allowedBrands.Contains(brand)) ||
            intent.ExcludedBrandCodes.Any(brand => !allowedBrands.Contains(brand)) ||
            intent.PreferredBrandCodes.Intersect(
                intent.ExcludedBrandCodes,
                StringComparer.OrdinalIgnoreCase).Any())
        {
            return Invalid(safety.IsValid ? AiSafetyReason.InvalidSearchIntent : safety.Reason);
        }

        if (intent.Intent != AiProductSearchIntentType.CustomBuild &&
            intent.RequiredSpecs.Count > 0 && string.IsNullOrWhiteSpace(intent.CategoryCode))
        {
            return new AiProductSearchCandidateResult(
                IsValid: false,
                AiSafetyReason.InvalidSearchIntent,
                Candidates: [],
                Clarifications: ["請問你要找的商品分類是什麼？"]);
        }

        if (existingParts.Any(part =>
                part.SourceType == "structuredManual" &&
                !TryCreateManualComponent(part, out _)))
        {
            return new AiProductSearchCandidateResult(
                IsValid: false,
                AiSafetyReason.InvalidSearchIntent,
                Candidates: [],
                Clarifications:
                ["手填既有零件缺少該分類的必要相容性規格，請確認完整規格後再試一次。"]);
        }

        if (intent.Intent == AiProductSearchIntentType.CustomBuild)
        {
            return await FindCustomBuildAsync(intent, existingParts, locale, cancellationToken);
        }

        IEnumerable<string?> brandsToQuery = intent.PreferredBrandCodes.Count > 0
            ? intent.PreferredBrandCodes.Cast<string?>()
            : new string?[] { null };
        var merged = new Dictionary<Guid, ProductCardDto>();
        foreach (var brand in brandsToQuery.Take(5))
        {
            PageResult<ProductCardDto> page;
            try
            {
                page = await productSearchService.SearchAsync(
                    new ProductSearchQuery(
                        intent.Keyword,
                        intent.CategoryCode,
                        brand,
                        intent.Budget?.Minimum,
                        intent.Budget?.Maximum,
                        InStock: true,
                        intent.RequiredSpecs.Select(MapSpec).ToArray(),
                        ProductSortOptions.Relevance,
                        PageNumber: 1,
                        PageSize: 20,
                        locale),
                    cancellationToken);
            }
            catch (CatalogSearchException)
            {
                return Invalid(AiSafetyReason.InvalidSearchIntent);
            }

            foreach (var item in page.Items.Where(item =>
                         !intent.ExcludedBrandCodes.Contains(
                             item.Brand.Code,
                             StringComparer.OrdinalIgnoreCase)))
            {
                merged.TryAdd(item.DefaultSkuPublicId, item);
            }
        }

        var candidates = new List<AiProductSearchCandidate>();
        foreach (var product in merged.Values.Take(MaximumCandidates))
        {
            var compatibility = await EvaluateCompatibilityAsync(
                product,
                existingParts,
                cancellationToken);
            if (compatibility is null)
            {
                continue;
            }

            candidates.Add(compatibility);
        }

        return new AiProductSearchCandidateResult(
            IsValid: true,
            AiSafetyReason.None,
            candidates,
            Clarifications: []);
    }

    private async Task<AiProductSearchCandidateResult> FindCustomBuildAsync(
        AiProductSearchIntent intent,
        IReadOnlyList<AiProductSearchExistingPart> existingParts,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (intent.Purposes.Count == 0 || intent.Budget?.Maximum is null)
        {
            return new AiProductSearchCandidateResult(
                IsValid: false,
                AiSafetyReason.InvalidSearchIntent,
                Candidates: [],
                Clarifications: [CustomBuildRequirementsQuestion(locale)]);
        }

        var maximumPurchaseSubtotal = intent.Budget.Maximum.Value - AiCustomBuildPricing.AssemblyFee;
        if (maximumPurchaseSubtotal < 0)
        {
            return new AiProductSearchCandidateResult(
                IsValid: true,
                AiSafetyReason.None,
                Candidates: [],
                Clarifications: []);
        }

        var catalogExistingParts = existingParts.Where(part => part.SkuPublicId.HasValue).ToArray();
        CompatibilityCatalogReadResult existingCatalogResult;
        if (catalogExistingParts.Length == 0)
        {
            existingCatalogResult = new CompatibilityCatalogReadResult([], []);
        }
        else
        {
            existingCatalogResult = await compatibilityCatalogReader.ReadAsync(
                catalogExistingParts.Select(part =>
                    new CompatibilityItemReference(part.SkuPublicId!.Value, part.Quantity)).ToArray(),
                cancellationToken);
            if (existingCatalogResult.MissingSkuPublicIds.Count > 0)
            {
                return Invalid(AiSafetyReason.InvalidSearchIntent);
            }
        }

        var manualComponents = new List<CompatibilityComponent>();
        foreach (var manualPart in existingParts.Where(part => part.SourceType == "structuredManual"))
        {
            if (!TryCreateManualComponent(manualPart, out var component))
            {
                return Invalid(AiSafetyReason.InvalidSearchIntent);
            }

            manualComponents.Add(component!);
        }

        var existingComponents = existingCatalogResult.Components.Concat(manualComponents).ToArray();
        var buildCategories = CompatibilityCatalogContract.Categories.All
            .ToHashSet(StringComparer.Ordinal);
        if (existingComponents.Any(component => !buildCategories.Contains(component.CategoryCode)))
        {
            return new AiProductSearchCandidateResult(
                IsValid: false,
                AiSafetyReason.InvalidSearchIntent,
                Candidates: [],
                Clarifications: [UnsupportedBuildPartQuestion(locale)]);
        }

        var specsByCategory = await ResolveBuildSpecsByCategoryAsync(
            intent.RequiredSpecs,
            cancellationToken);
        if (specsByCategory is null)
        {
            return new AiProductSearchCandidateResult(
                IsValid: false,
                AiSafetyReason.InvalidSearchIntent,
                Candidates: [],
                Clarifications: [AmbiguousBuildSpecQuestion(locale)]);
        }

        var coveredCategories = existingComponents
            .Select(component => component.CategoryCode)
            .ToHashSet(StringComparer.Ordinal);
        var missingCategories = CompatibilityCatalogContract.Categories.All
            .Where(category => !coveredCategories.Contains(category))
            .ToArray();
        var optionsByCategory = new List<(string Category, IReadOnlyList<ProductCardDto> Products)>();
        foreach (var category in missingCategories)
        {
            var products = await FindBuildComponentOptionsAsync(
                category,
                specsByCategory.GetValueOrDefault(category, []),
                intent,
                maximumPurchaseSubtotal,
                locale,
                cancellationToken);
            if (products.Count == 0)
            {
                return new AiProductSearchCandidateResult(
                    IsValid: true,
                    AiSafetyReason.None,
                    Candidates: [],
                    Clarifications: []);
            }

            optionsByCategory.Add((category, products));
        }

        var productComponents = new Dictionary<Guid, CompatibilityComponent>();
        foreach (var (_, products) in optionsByCategory)
        {
            var read = await compatibilityCatalogReader.ReadAsync(
                products.Select(product =>
                    new CompatibilityItemReference(product.DefaultSkuPublicId, 1)).ToArray(),
                cancellationToken);
            foreach (var component in read.Components)
            {
                productComponents[component.SkuPublicId] = component;
            }
        }

        var existingCandidates = await CreateExistingComponentCandidatesAsync(
            existingParts,
            existingCatalogResult.Components,
            cancellationToken);
        var selected = new List<ProductCardDto>();
        AiCustomBuildCandidate? approvedBuild = null;
        var evaluationCount = 0;

        async Task<bool> SelectAsync(int index, decimal subtotal)
        {
            if (subtotal > maximumPurchaseSubtotal || evaluationCount >= MaximumBuildEvaluations)
            {
                return false;
            }

            if (index < optionsByCategory.Count)
            {
                foreach (var product in optionsByCategory[index].Products)
                {
                    var price = CurrentPrice(product);
                    if (!productComponents.ContainsKey(product.DefaultSkuPublicId) ||
                        subtotal + price > maximumPurchaseSubtotal)
                    {
                        continue;
                    }

                    selected.Add(product);
                    evaluationCount++;
                    var partialComponents = existingComponents
                        .Concat(selected.Select(item => productComponents[item.DefaultSkuPublicId]))
                        .ToArray();
                    var partialCompatibility = await compatibilityCheckService.CheckPartialAsync(
                        partialComponents,
                        cancellationToken);
                    if (partialCompatibility.Overall != "blocked" &&
                        await SelectAsync(index + 1, subtotal + price))
                    {
                        return true;
                    }

                    selected.RemoveAt(selected.Count - 1);
                }

                return false;
            }

            var purchaseTotal = subtotal + AiCustomBuildPricing.AssemblyFee;
            if (intent.Budget.Minimum.HasValue && purchaseTotal < intent.Budget.Minimum.Value)
            {
                return false;
            }

            var compatibilityComponents = existingComponents
                .Concat(selected.Select(product => productComponents[product.DefaultSkuPublicId]))
                .ToArray();
            var compatibility = await compatibilityCheckService.CheckCompleteTransientAsync(
                compatibilityComponents,
                cancellationToken);
            if (compatibility.Overall is "blocked" or "insufficientData")
            {
                return false;
            }

            var selectedCandidates = selected.Select(product =>
                new AiCustomBuildComponentCandidate(
                    product,
                    product.DefaultSkuPublicId,
                    "catalogSku",
                    product.Category.Code,
                    product.Name,
                    1,
                    IsExistingPart: false));
            var components = existingCandidates.Concat(selectedCandidates)
                .OrderBy(component => BuildCategoryOrder(component.CategoryCode))
                .ThenBy(component => component.IsExistingPart ? 0 : 1)
                .ThenBy(component => component.DisplayName, StringComparer.Ordinal)
                .ToArray();
            approvedBuild = new AiCustomBuildCandidate(
                components,
                subtotal,
                AiCustomBuildPricing.AssemblyFee,
                purchaseTotal,
                "TWD",
                compatibility.Overall == "warning"
                    ? AiCompatibilityStatus.Warning
                    : AiCompatibilityStatus.Compatible,
                compatibility.Results.Select(result => result.MessageKey).Distinct().ToArray());
            return true;
        }

        await SelectAsync(0, 0m);
        return new AiProductSearchCandidateResult(
            IsValid: true,
            AiSafetyReason.None,
            Candidates: [],
            Clarifications: [],
            approvedBuild);
    }

    private async Task<Dictionary<string, IReadOnlyList<AiRequiredSpec>>?> ResolveBuildSpecsByCategoryAsync(
        IReadOnlyList<AiRequiredSpec> specs,
        CancellationToken cancellationToken)
    {
        if (specs.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<AiRequiredSpec>>(StringComparer.Ordinal);
        }

        var semanticKeys = specs.Select(spec => spec.SemanticKey.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var buildCategories = CompatibilityCatalogContract.Categories.All.ToArray();
        var rows = await (
                from definition in dbContext.SpecificationDefinitions.AsNoTracking()
                join category in dbContext.Categories.AsNoTracking() on definition.CategoryId equals category.Id
                where definition.IsActive && category.IsActive &&
                      semanticKeys.Contains(definition.SemanticKey) &&
                      buildCategories.Contains(category.Code)
                select new { definition.SemanticKey, CategoryCode = category.Code })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, IReadOnlyList<AiRequiredSpec>>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            var normalizedKey = spec.SemanticKey.Trim().ToUpperInvariant();
            var categories = rows.Where(row => row.SemanticKey == normalizedKey)
                .Select(row => row.CategoryCode)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (categories.Length != 1)
            {
                return null;
            }

            var category = categories[0];
            var existing = result.GetValueOrDefault(category, []);
            result[category] = existing.Append(spec).ToArray();
        }

        return result;
    }

    private async Task<IReadOnlyList<ProductCardDto>> FindBuildComponentOptionsAsync(
        string category,
        IReadOnlyList<AiRequiredSpec> specs,
        AiProductSearchIntent intent,
        decimal maximumPrice,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        IEnumerable<string?> brands = intent.PreferredBrandCodes.Count > 0
            ? intent.PreferredBrandCodes.Cast<string?>()
            : new string?[] { null };
        var products = new Dictionary<Guid, ProductCardDto>();
        foreach (var brand in brands.Take(5))
        {
            PageResult<ProductCardDto> page;
            try
            {
                page = await productSearchService.SearchAsync(
                    new ProductSearchQuery(
                        Keyword: null,
                        CategoryCode: category,
                        BrandCode: brand,
                        MinPrice: null,
                        MaxPrice: maximumPrice,
                        InStock: true,
                        Specs: specs.Select(MapSpec).ToArray(),
                        ProductSortOptions.PriceAsc,
                        PageNumber: 1,
                        PageSize: 20,
                        locale),
                    cancellationToken);
            }
            catch (CatalogSearchException)
            {
                return [];
            }

            foreach (var item in page.Items.Where(item =>
                         !intent.ExcludedBrandCodes.Contains(
                             item.Brand.Code,
                             StringComparer.OrdinalIgnoreCase)))
            {
                products.TryAdd(item.DefaultSkuPublicId, item);
            }
        }

        return products.Values
            .OrderBy(CurrentPrice)
            .ThenBy(product => product.DefaultSkuPublicId)
            .Take(MaximumComponentCandidates)
            .ToArray();
    }

    private async Task<IReadOnlyList<AiCustomBuildComponentCandidate>> CreateExistingComponentCandidatesAsync(
        IReadOnlyList<AiProductSearchExistingPart> existingParts,
        IReadOnlyList<CompatibilityComponent> catalogComponents,
        CancellationToken cancellationToken)
    {
        var catalogIds = catalogComponents.Select(component => component.SkuPublicId).ToArray();
        var catalogRows = await (
                from sku in dbContext.Skus.AsNoTracking()
                join product in dbContext.Products.AsNoTracking() on sku.ProductId equals product.Id
                join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
                where catalogIds.Contains(sku.PublicId)
                select new { sku.PublicId, DisplayName = sku.NameZhTw, CategoryCode = category.Code })
            .ToDictionaryAsync(row => row.PublicId, cancellationToken);
        var candidates = new List<AiCustomBuildComponentCandidate>();
        foreach (var part in existingParts)
        {
            if (part.SkuPublicId.HasValue)
            {
                var row = catalogRows[part.SkuPublicId.Value];
                candidates.Add(new AiCustomBuildComponentCandidate(
                    Product: null,
                    row.PublicId,
                    "catalogSku",
                    row.CategoryCode,
                    row.DisplayName,
                    part.Quantity,
                    IsExistingPart: true));
            }
            else
            {
                candidates.Add(new AiCustomBuildComponentCandidate(
                    Product: null,
                    SkuPublicId: null,
                    "structuredManual",
                    part.CategoryCode!.Trim().ToUpperInvariant(),
                    part.DisplayName!,
                    part.Quantity,
                    IsExistingPart: true));
            }
        }

        return candidates;
    }

    private static decimal CurrentPrice(ProductCardDto product) =>
        product.Price.Sale ?? product.Price.List;

    private static int BuildCategoryOrder(string categoryCode)
    {
        for (var index = 0; index < CompatibilityCatalogContract.Categories.All.Count; index++)
        {
            if (CompatibilityCatalogContract.Categories.All[index] == categoryCode)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static string CustomBuildRequirementsQuestion(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "完整組裝需要至少一個用途與最高預算。",
        SupportedLocale.JaJp => "完全な構成には、少なくとも1つの用途と上限予算が必要です。",
        SupportedLocale.KoKr => "전체 구성에는 최소 한 가지 용도와 최대 예산이 필요합니다.",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static string UnsupportedBuildPartQuestion(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "既有零件包含不屬於完整主機組裝的分類，請移除後再試一次。",
        SupportedLocale.JaJp => "既存パーツにPC構成外のカテゴリが含まれています。削除して再試行してください。",
        SupportedLocale.KoKr => "기존 부품에 PC 구성 대상이 아닌 분류가 포함되어 있습니다. 제거 후 다시 시도해 주세요.",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static string AmbiguousBuildSpecQuestion(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "必要規格無法唯一對應到組裝零件分類，請指出該規格屬於哪一類零件。",
        SupportedLocale.JaJp => "必須仕様を構成パーツのカテゴリに一意に対応できません。対象カテゴリを指定してください。",
        SupportedLocale.KoKr => "필수 사양을 구성 부품 분류에 하나로 연결할 수 없습니다. 대상 분류를 지정해 주세요.",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    public async Task<IReadOnlyList<ProductCardDto>> KeywordFallbackAsync(
        string message,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        var keyword = message.Trim();
        if (keyword.Length > 160)
        {
            keyword = keyword[..160];
        }

        var page = await productSearchService.SearchAsync(
            new ProductSearchQuery(
                keyword,
                CategoryCode: null,
                BrandCode: null,
                MinPrice: null,
                MaxPrice: null,
                InStock: true,
                Specs: [],
                ProductSortOptions.Relevance,
                PageNumber: 1,
                PageSize: MaximumCandidates,
                locale),
            cancellationToken);
        return page.Items;
    }

    private async Task<AiProductSearchCandidate?> EvaluateCompatibilityAsync(
        ProductCardDto product,
        IReadOnlyList<AiProductSearchExistingPart> existingParts,
        CancellationToken cancellationToken)
    {
        if (existingParts.Count == 0)
        {
            return new AiProductSearchCandidate(
                product,
                AiCompatibilityStatus.NotRequired,
                CompatibilityMessageKeys: []);
        }

        var catalogItems = existingParts
            .Where(part => part.SkuPublicId.HasValue)
            .Select(part => new CompatibilityItemReference(part.SkuPublicId!.Value, part.Quantity))
            .Append(new CompatibilityItemReference(product.DefaultSkuPublicId, 1))
            .GroupBy(item => item.SkuPublicId)
            .Select(group => new CompatibilityItemReference(group.Key, group.Sum(item => item.Quantity)))
            .ToArray();
        var catalogResult = await compatibilityCatalogReader.ReadAsync(catalogItems, cancellationToken);
        if (catalogResult.MissingSkuPublicIds.Count > 0)
        {
            return null;
        }

        var components = catalogResult.Components.ToList();
        foreach (var manualPart in existingParts.Where(part => part.SourceType == "structuredManual"))
        {
            if (!TryCreateManualComponent(manualPart, out var component))
            {
                return null;
            }

            components.Add(component!);
        }

        var result = await compatibilityCheckService.CheckPartialAsync(components, cancellationToken);
        if (result.Overall is "blocked" or "insufficientData")
        {
            return null;
        }

        return new AiProductSearchCandidate(
            product,
            result.Overall == "warning"
                ? AiCompatibilityStatus.Warning
                : AiCompatibilityStatus.Compatible,
            result.Results.Select(finding => finding.MessageKey).Distinct().ToArray());
    }

    internal static bool TryCreateManualComponent(
        AiProductSearchExistingPart part,
        out CompatibilityComponent? component)
    {
        component = null;
        var category = part.CategoryCode?.Trim().ToUpperInvariant();
        if (category is null ||
            !CompatibilityCatalogContract.RequiredSemanticKeysByCategory.TryGetValue(
                category,
                out var requiredKeys))
        {
            return false;
        }

        var provided = part.Specifications
            .Select(spec => spec.SemanticKey.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (!requiredKeys.SetEquals(provided))
        {
            return false;
        }

        var specifications = new Dictionary<string, CompatibilitySpecification>(StringComparer.Ordinal);
        foreach (var spec in part.Specifications)
        {
            var key = spec.SemanticKey.Trim().ToUpperInvariant();
            if (!TryParseManualSpecification(category, key, spec, out var parsed))
            {
                return false;
            }

            specifications[key] = parsed!;
        }

        component = new CompatibilityComponent(
            Guid.NewGuid(),
            category,
            part.Quantity,
            specifications);
        return true;
    }

    private static bool TryParseManualSpecification(
        string category,
        string key,
        AiRequiredSpec spec,
        out CompatibilitySpecification? parsed)
    {
        parsed = null;
        if (spec.Operator is not ("eq" or "in"))
        {
            return false;
        }

        var isMultiValue =
            (category == CompatibilityCatalogContract.Categories.CpuCooler &&
             key == CompatibilityCatalogContract.SemanticKeys.CpuSocket) ||
            (category == CompatibilityCatalogContract.Categories.Case &&
             key is CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor or
                 CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor);
        if (isMultiValue)
        {
            var values = spec.Value.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0)
            {
                return false;
            }

            parsed = CompatibilitySpecification.FromOptions(values);
            return true;
        }

        if (decimal.TryParse(
                spec.Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var decimalValue))
        {
            parsed = CompatibilitySpecification.FromDecimal(decimalValue);
            return true;
        }

        parsed = CompatibilitySpecification.FromOption(spec.Value);
        return true;
    }

    private static SpecFilter MapSpec(AiRequiredSpec spec) => new(
        spec.SemanticKey,
        ProductSearchQueryValidator.ParseOperator(spec.Operator),
        spec.Operator == SpecFilterOperatorCodes.In ? null : spec.Value,
        spec.Operator == SpecFilterOperatorCodes.In
            ? spec.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : null);

    private static AiProductSearchCandidateResult Invalid(AiSafetyReason reason) =>
        new(false, reason, Candidates: [], Clarifications: []);
}
