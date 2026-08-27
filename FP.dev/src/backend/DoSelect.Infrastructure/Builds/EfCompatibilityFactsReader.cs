using DoSelect.Application.Builds;
using DoSelect.Domain.Builds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Builds;

public sealed record ResolvedBuildSku(long SkuId, Guid SkuPublicId, string CategoryCode, int Quantity);

public sealed record BuildFactsResolution(
    BuildComponentSet Components,
    IReadOnlyList<Guid> UnresolvedSkuPublicIds,
    IReadOnlyList<ResolvedBuildSku> ResolvedSkus);

/// <summary>
/// Assembles a <see cref="BuildComponentSet"/> from Sku specification data for the
/// compatibility rule engine. Slots a Sku into a build-component role by its product's
/// Category.Code matching one of the protected <see cref="BuildComponentCategoryCodes"/> —
/// SKUs in any other category (peripherals, etc.) are resolved but simply ignored by the
/// engine, per "螢幕、鍵盤、滑鼠等周邊不參與整機主要相容性驗證".
/// </summary>
public sealed class EfCompatibilityFactsReader
{
    // These 6 categories are single-instance PC roles (there is exactly one CPU, one motherboard,
    // one GPU, one PSU, one case, one cooler per build) — Memory／StorageDevice are the only roles
    // the engine genuinely evaluates as a collection. Without this check, FirstOfRole silently
    // picked the lowest SkuId and ignored every other SKU in the same role: they still got added
    // to the cart (AddAssemblyGroupsAsync has no per-role awareness) but never participated in
    // compatibility evaluation at all, so e.g. a second, incompatible CPU could ride along
    // undetected. 組長 PR #34 review — reject rather than "evaluate all", since a build genuinely
    // can't have two CPUs.
    private static readonly IReadOnlyCollection<string> SingletonComponentCategoryCodes =
    [
        BuildComponentCategoryCodes.Cpu,
        BuildComponentCategoryCodes.Motherboard,
        BuildComponentCategoryCodes.GraphicsCard,
        BuildComponentCategoryCodes.PowerSupply,
        BuildComponentCategoryCodes.Case,
        BuildComponentCategoryCodes.Cooler,
    ];

    private readonly DoSelectDbContext _dbContext;

    public EfCompatibilityFactsReader(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BuildFactsResolution> ResolveAsync(
        IReadOnlyList<BuildItemInput> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var requestedPublicIds = items.Select(item => item.SkuPublicId).Distinct().ToArray();

        var skuRows = await (
            from sku in _dbContext.Skus
            join product in _dbContext.Products on sku.ProductId equals product.Id
            join category in _dbContext.Categories on product.CategoryId equals category.Id
            where requestedPublicIds.Contains(sku.PublicId)
            select new { sku.Id, sku.PublicId, category.Code })
            .ToListAsync(cancellationToken);

        var resolvedSkus = skuRows
            .Join(items, row => row.PublicId, item => item.SkuPublicId,
                (row, item) => new ResolvedBuildSku(row.Id, row.PublicId, row.Code, item.Quantity))
            .ToList();

        var unresolved = requestedPublicIds.Except(skuRows.Select(row => row.PublicId)).ToList();

        ValidateSingletonRoles(resolvedSkus);

        var skuIds = skuRows.Select(row => row.Id).ToArray();
        var specValues = await (
            from value in _dbContext.SkuSpecificationValues
            join definition in _dbContext.SpecificationDefinitions
                on value.SpecificationDefinitionId equals definition.Id
            where skuIds.Contains(value.SkuId)
            select new { value.SkuId, definition.SemanticKey, value.StringValue, value.DecimalValue })
            .ToListAsync(cancellationToken);

        var attributeRows = await _dbContext.SkuCompatibilityAttributes
            .Where(attribute => skuIds.Contains(attribute.SkuId))
            .Select(attribute => new { attribute.SkuId, attribute.AttributeKey, attribute.AttributeValue })
            .ToListAsync(cancellationToken);

        var storagePortRows = await _dbContext.SkuStorageInterfacePorts
            .Where(port => skuIds.Contains(port.SkuId))
            .Select(port => new { port.SkuId, port.InterfaceCode, port.PortCount })
            .ToListAsync(cancellationToken);

        string? GetString(long skuId, string semanticKey) => specValues
            .FirstOrDefault(value => value.SkuId == skuId && value.SemanticKey == semanticKey)
            ?.StringValue;

        decimal? GetDecimal(long skuId, string semanticKey) => specValues
            .FirstOrDefault(value => value.SkuId == skuId && value.SemanticKey == semanticKey)
            ?.DecimalValue;

        int? GetInt(long skuId, string semanticKey) =>
            GetDecimal(skuId, semanticKey) is { } value ? (int)value : null;

        IReadOnlyCollection<string> GetAttributes(long skuId, string attributeKey) => attributeRows
            .Where(attribute => attribute.SkuId == skuId && attribute.AttributeKey == attributeKey)
            .Select(attribute => attribute.AttributeValue)
            .ToList();

        // Backed by the dedicated SkuStorageInterfacePort table (組長 PR #34 round-4 review, item
        // 1) — its unique index on (SkuId, InterfaceCode) makes "two rows for the same interface"
        // unrepresentable at the schema level, so there's no ambiguity to resolve here.
        IReadOnlyDictionary<string, int> GetStoragePortsByInterface(long skuId) => storagePortRows
            .Where(row => row.SkuId == skuId)
            .ToDictionary(row => row.InterfaceCode, row => row.PortCount);

        ResolvedBuildSku? FirstOfRole(string categoryCode) => resolvedSkus
            .Where(sku => sku.CategoryCode == categoryCode)
            .OrderBy(sku => sku.SkuId)
            .FirstOrDefault();

        var cpuSku = FirstOfRole(BuildComponentCategoryCodes.Cpu);
        var cpu = cpuSku is null ? null : new CpuFacts(
            cpuSku.SkuPublicId,
            GetString(cpuSku.SkuId, CompatibilitySemanticKeys.CpuSocket),
            GetString(cpuSku.SkuId, CompatibilitySemanticKeys.CpuGeneration),
            GetDecimal(cpuSku.SkuId, CompatibilitySemanticKeys.CpuPowerWatts));

        var boardSku = FirstOfRole(BuildComponentCategoryCodes.Motherboard);
        var motherboard = boardSku is null ? null : new MotherboardFacts(
            boardSku.SkuPublicId,
            GetString(boardSku.SkuId, CompatibilitySemanticKeys.BoardSocket),
            GetString(boardSku.SkuId, CompatibilitySemanticKeys.BoardChipset),
            GetString(boardSku.SkuId, CompatibilitySemanticKeys.BoardMemoryGeneration),
            GetInt(boardSku.SkuId, CompatibilitySemanticKeys.BoardMemorySlotCount),
            GetDecimal(boardSku.SkuId, CompatibilitySemanticKeys.BoardMaxMemoryCapacityGb),
            GetString(boardSku.SkuId, CompatibilitySemanticKeys.BoardFormFactor),
            GetStoragePortsByInterface(boardSku.SkuId));

        var memory = resolvedSkus
            .Where(sku => sku.CategoryCode == BuildComponentCategoryCodes.Memory)
            .Select(sku => new MemoryModuleFacts(
                sku.SkuPublicId,
                GetString(sku.SkuId, CompatibilitySemanticKeys.MemoryGeneration),
                GetDecimal(sku.SkuId, CompatibilitySemanticKeys.MemoryCapacityGbPerModule),
                sku.Quantity))
            .ToList();

        var gpuSku = FirstOfRole(BuildComponentCategoryCodes.GraphicsCard);
        var gpu = gpuSku is null ? null : new GraphicsCardFacts(
            gpuSku.SkuPublicId,
            GetDecimal(gpuSku.SkuId, CompatibilitySemanticKeys.GpuLengthMm),
            GetDecimal(gpuSku.SkuId, CompatibilitySemanticKeys.GpuRecommendedPsuWatts),
            GetDecimal(gpuSku.SkuId, CompatibilitySemanticKeys.GpuPowerWatts),
            GetAttributes(gpuSku.SkuId, CompatibilityAttributeKeys.GpuRequiredConnectors));

        var storage = resolvedSkus
            .Where(sku => sku.CategoryCode == BuildComponentCategoryCodes.StorageDevice)
            .Select(sku => new StorageDeviceFacts(
                sku.SkuPublicId,
                GetString(sku.SkuId, CompatibilitySemanticKeys.StorageInterface),
                GetDecimal(sku.SkuId, CompatibilitySemanticKeys.StoragePowerWatts),
                sku.Quantity))
            .ToList();

        var psuSku = FirstOfRole(BuildComponentCategoryCodes.PowerSupply);
        var psu = psuSku is null ? null : new PowerSupplyFacts(
            psuSku.SkuPublicId,
            GetDecimal(psuSku.SkuId, CompatibilitySemanticKeys.PsuWattage),
            GetAttributes(psuSku.SkuId, CompatibilityAttributeKeys.PsuAvailableConnectors));

        var caseSku = FirstOfRole(BuildComponentCategoryCodes.Case);
        var caseFacts = caseSku is null ? null : new CaseFacts(
            caseSku.SkuPublicId,
            GetAttributes(caseSku.SkuId, CompatibilityAttributeKeys.CaseSupportedFormFactors),
            GetDecimal(caseSku.SkuId, CompatibilitySemanticKeys.CaseMaxGpuLengthMm),
            GetDecimal(caseSku.SkuId, CompatibilitySemanticKeys.CaseMaxCoolerHeightMm));

        var coolerSku = FirstOfRole(BuildComponentCategoryCodes.Cooler);
        var cooler = coolerSku is null ? null : new CoolerFacts(
            coolerSku.SkuPublicId,
            GetDecimal(coolerSku.SkuId, CompatibilitySemanticKeys.CoolerHeightMm),
            GetAttributes(coolerSku.SkuId, CompatibilityAttributeKeys.CoolerSupportedSockets),
            GetDecimal(coolerSku.SkuId, CompatibilitySemanticKeys.CoolerPowerWatts));

        var components = new BuildComponentSet(cpu, motherboard, memory, gpu, storage, psu, caseFacts, cooler);
        return new BuildFactsResolution(components, unresolved, resolvedSkus);
    }

    private static void ValidateSingletonRoles(IReadOnlyList<ResolvedBuildSku> resolvedSkus)
    {
        foreach (var categoryCode in SingletonComponentCategoryCodes)
        {
            var skusInRole = resolvedSkus.Where(sku => sku.CategoryCode == categoryCode).ToList();
            var distinctSkuCount = skusInRole.Select(sku => sku.SkuPublicId).Distinct().Count();
            if (distinctSkuCount > 1)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"Only one {categoryCode} SKU is allowed per build.");
            }

            if (skusInRole.Any(sku => sku.Quantity != 1))
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"{categoryCode} quantity must be exactly 1.");
            }
        }
    }
}
