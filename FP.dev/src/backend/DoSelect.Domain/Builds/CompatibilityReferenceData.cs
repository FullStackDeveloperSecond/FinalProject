namespace DoSelect.Domain.Builds;

/// <summary>
/// <see cref="BuildList.Status"/> values. Terry-商品庫存物流組裝與報表最終Schema.md describes the
/// column only as "有效／停用狀態" (active/deactivated) with no fixed enum list, so DELETE
/// /build-lists/{id} is a soft status flip (not a row delete) — BuildListItems/BuildShareTokens
/// reference BuildLists with Restrict/Cascade FKs that assume the row keeps existing.
/// </summary>
public static class BuildListStatusCodes
{
    public const string Active = "Active";
    public const string Deleted = "Deleted";
}

/// <summary>Wire tokens for a per-finding <c>CompatibilityFinding.Severity</c> string.</summary>
public static class CompatibilitySeverityTokens
{
    public const string Compatible = "compatible";
    public const string Warning = "warning";
    public const string Blocked = "blocked";
    public const string InsufficientData = "insufficientData";

    /// <summary>相容性規則後台設計.md: a rule an admin disabled still reports here (never silently vanishes), but never wins the top-level Overall rollup.</summary>
    public const string RuleDisabled = "ruleDisabled";
}

/// <summary>Setting code an admin uses to disable one rule entirely (see CompatibilityRuleSetting.BooleanValue), independent of the 5 decimal warning thresholds below.</summary>
public static class CompatibilityRuleActivationSettingCodes
{
    public const string IsActive = "IsActive";
}

/// <summary>
/// Protected semantic keys the compatibility engine reads from <c>SkuSpecificationValue</c>
/// (single-value facts) and <c>SkuCompatibilityAttribute</c> (multi-value facts, e.g. a
/// motherboard supporting several storage interfaces). Per 商品、組裝與相容性.md "受保護語意鍵":
/// these keys and their value types are fixed by the rule engine and must never be deleted or
/// retyped by the (still-blocked) specification-definitions admin surface.
/// Uppercase (not the more common lowercase-snake-case spec-key style) because
/// <see cref="DoSelect.Domain.Catalog.SpecificationDefinition"/>'s constructor runs every
/// SemanticKey through <c>CatalogCode.Normalize</c>, which upper-cases it — these constants are
/// pre-normalized so a plain string comparison against a value read back from the database
/// actually matches.
/// </summary>
public static class CompatibilitySemanticKeys
{
    public const string CpuSocket = "CPU_SOCKET";
    public const string CpuGeneration = "CPU_GENERATION";
    public const string CpuPowerWatts = "CPU_POWER_WATTS";

    public const string BoardSocket = "BOARD_SOCKET";
    public const string BoardChipset = "BOARD_CHIPSET";
    public const string BoardMemoryGeneration = "BOARD_MEMORY_GENERATION";
    public const string BoardMemorySlotCount = "BOARD_MEMORY_SLOT_COUNT";
    public const string BoardMaxMemoryCapacityGb = "BOARD_MAX_MEMORY_CAPACITY_GB";
    public const string BoardFormFactor = "BOARD_FORM_FACTOR";

    public const string MemoryGeneration = "MEMORY_GENERATION";
    public const string MemoryCapacityGbPerModule = "MEMORY_CAPACITY_GB_PER_MODULE";

    public const string GpuLengthMm = "GPU_LENGTH_MM";
    public const string GpuRecommendedPsuWatts = "GPU_RECOMMENDED_PSU_WATTS";
    public const string GpuPowerWatts = "GPU_POWER_WATTS";

    public const string StorageInterface = "STORAGE_INTERFACE";
    public const string StoragePowerWatts = "STORAGE_POWER_WATTS";

    public const string PsuWattage = "PSU_WATTAGE";

    public const string CaseMaxGpuLengthMm = "CASE_MAX_GPU_LENGTH_MM";
    public const string CaseMaxCoolerHeightMm = "CASE_MAX_COOLER_HEIGHT_MM";

    public const string CoolerHeightMm = "COOLER_HEIGHT_MM";
    public const string CoolerPowerWatts = "COOLER_POWER_WATTS";
}

/// <summary>
/// Protected multi-value attribute keys stored as one <c>SkuCompatibilityAttribute</c> row per
/// value (per 商品、組裝與相容性.md: "標籤、介面類型等真正多值資料使用明確 Join Entity，不把多值塞入同一規格值").
/// </summary>
public static class CompatibilityAttributeKeys
{
    // BoardSupportedStorageInterfaces used to live here as a packed "{interface}:{portCount}"
    // string value — split out to the dedicated SkuStorageInterfacePort entity (組長 PR #34
    // round-4 review, item 1) since this key needed a real count alongside it, not just a bare
    // multi-value string like the ones below.
    public const string CaseSupportedFormFactors = "case_supported_form_factors";
    public const string CoolerSupportedSockets = "cooler_supported_sockets";
    public const string PsuAvailableConnectors = "psu_available_connectors";
    public const string GpuRequiredConnectors = "gpu_required_connectors";
}

/// <summary>Program-fixed safe bounds for compatibility fact values that need one (DEC-P97-style scope, not admin-configurable).</summary>
public static class CompatibilityAttributeLimits
{
    // No real motherboard shape gets remotely close to this; it exists to reject obviously-bad
    // seed/import data rather than to model a genuine hardware ceiling.
    public const int MaxStorageInterfacePortCount = 32;
}

/// <summary>
/// Category is admin-managed Lookup data (not a hardcoded enum per 商品、組裝與相容性.md), but the
/// compatibility engine still needs a stable way to tell "this Sku is the CPU slot" from
/// "this Sku is the case slot". These 8 <c>Category.Code</c> values are the protected,
/// developer-fixed anchor the seed data creates real Category rows against — analogous in
/// spirit to a protected specification semantic key, just for categories instead of specs.
/// </summary>
public static class BuildComponentCategoryCodes
{
    public const string Cpu = "CPU";
    public const string Motherboard = "MOTHERBOARD";
    public const string Memory = "MEMORY";
    public const string GraphicsCard = "GPU";
    public const string StorageDevice = "STORAGE";
    public const string PowerSupply = "PSU";
    public const string Case = "CASE";
    public const string Cooler = "COOLER";

    public static readonly IReadOnlyList<string> All =
    [
        Cpu, Motherboard, Memory, GraphicsCard, StorageDevice, PowerSupply, Case, Cooler,
    ];
}

public static class BuildCompatibilityRuleCodes
{
    public const string CpuSocket = "CPU_SOCKET";
    public const string ChipsetCpuGeneration = "CHIPSET_CPU_GENERATION";
    public const string RamGeneration = "RAM_GENERATION";
    public const string RamSlotCount = "RAM_SLOT_COUNT";
    public const string RamCapacity = "RAM_CAPACITY";
    public const string CaseFormFactor = "CASE_FORM_FACTOR";
    public const string GpuLength = "GPU_LENGTH";
    public const string CoolerSocket = "COOLER_SOCKET";
    public const string CoolerHeight = "COOLER_HEIGHT";
    public const string StorageInterface = "STORAGE_INTERFACE";
    public const string PsuCapacity = "PSU_CAPACITY";
    public const string PsuConnectors = "PSU_CONNECTORS";

    public static readonly IReadOnlyList<string> All =
    [
        CpuSocket, ChipsetCpuGeneration, RamGeneration, RamSlotCount, RamCapacity,
        CaseFormFactor, GpuLength, CoolerSocket, CoolerHeight, StorageInterface,
        PsuCapacity, PsuConnectors,
    ];
}

/// <summary>
/// Setting codes admins may tune within a program-fixed safe range (相容性規則後台設計.md
/// "可調警告門檻"). Hard-blocking thresholds (BIOS mapping, socket, DDR, size caps, connectors,
/// the PSU 30% floor) are never admin-editable.
/// </summary>
public static class CompatibilityWarningSettingCodes
{
    public const string GpuClearanceWarningMm = "GpuClearanceWarningMm";
    public const string CoolerClearanceWarningMm = "CoolerClearanceWarningMm";
    public const string PsuReserveWarningPercent = "PsuReserveWarningPercent";
    public const string RemainingRamSlotWarningCount = "RemainingRamSlotWarningCount";
    public const string RemainingStoragePortWarningCount = "RemainingStoragePortWarningCount";
}

public readonly record struct CompatibilityWarningSettingRange(decimal Default, decimal Min, decimal Max);

public static class CompatibilityWarningSettingRanges
{
    public static readonly IReadOnlyDictionary<string, CompatibilityWarningSettingRange> ByCode =
        new Dictionary<string, CompatibilityWarningSettingRange>
        {
            [CompatibilityWarningSettingCodes.GpuClearanceWarningMm] = new(20m, 10m, 50m),
            [CompatibilityWarningSettingCodes.CoolerClearanceWarningMm] = new(10m, 5m, 30m),
            [CompatibilityWarningSettingCodes.PsuReserveWarningPercent] = new(35m, 30m, 50m),
            [CompatibilityWarningSettingCodes.RemainingRamSlotWarningCount] = new(0m, 0m, 2m),
            [CompatibilityWarningSettingCodes.RemainingStoragePortWarningCount] = new(0m, 0m, 2m),
        };

    public static bool IsInRange(string settingCode, decimal value) =>
        ByCode.TryGetValue(settingCode, out var range) && value >= range.Min && value <= range.Max;
}

/// <summary>
/// Each of the 5 adjustable warning thresholds belongs to exactly one rule (相容性規則後台設計.md's
/// table); the other 7 rules are pure hard-blocking checks with no admin-adjustable threshold.
/// </summary>
public static class CompatibilityRuleWarningSettingMap
{
    public static readonly IReadOnlyDictionary<string, string> RuleCodeToSettingCode =
        new Dictionary<string, string>
        {
            [BuildCompatibilityRuleCodes.GpuLength] = CompatibilityWarningSettingCodes.GpuClearanceWarningMm,
            [BuildCompatibilityRuleCodes.CoolerHeight] = CompatibilityWarningSettingCodes.CoolerClearanceWarningMm,
            [BuildCompatibilityRuleCodes.PsuCapacity] = CompatibilityWarningSettingCodes.PsuReserveWarningPercent,
            [BuildCompatibilityRuleCodes.RamSlotCount] = CompatibilityWarningSettingCodes.RemainingRamSlotWarningCount,
            [BuildCompatibilityRuleCodes.StorageInterface] = CompatibilityWarningSettingCodes.RemainingStoragePortWarningCount,
        };

    public static string? TryGetSettingCode(string ruleCode) => RuleCodeToSettingCode.GetValueOrDefault(ruleCode);
}

/// <summary>
/// The fixed common PSU wattage tiers (商品、組裝與相容性.md 第一版 PSU 常見級距). Values above the
/// top tier have no suitable PSU and must block, not silently round to a nonexistent tier.
/// </summary>
public static class PsuTierLadder
{
    public static readonly IReadOnlyList<int> WattTiers = [450, 550, 650, 750, 850, 1000, 1200, 1500];

    public static int? RoundUpToTier(decimal requiredWatts)
    {
        foreach (var tier in WattTiers)
        {
            if (requiredWatts <= tier)
            {
                return tier;
            }
        }

        return null;
    }
}

/// <summary>
/// Developer-maintained chipset/CPU-generation support matrix (規則程式由開發者維護，管理員不能新增修改).
/// Illustrative dataset for common current-generation Intel LGA1700 and AMD AM5/AM4 platforms,
/// compiled from public vendor guidance (Intel BIOS-update advisories, AMD chipset pages) as of
/// 2026-08. Not an exhaustive real-world catalog — extend as new SKUs/generations are seeded.
/// </summary>
public static class ChipsetGenerationSupportMatrix
{
    private static readonly IReadOnlyDictionary<(string Chipset, string Generation), bool> RequiresBiosUpdateByPair =
        new Dictionary<(string, string), bool>
        {
            // Intel LGA1700 — 600-series (launched with 12th gen)
            [("Z690", "Intel12Gen")] = false,
            [("Z690", "Intel13Gen")] = true,
            [("Z690", "Intel14Gen")] = true,
            [("B660", "Intel12Gen")] = false,
            [("B660", "Intel13Gen")] = true,
            [("B660", "Intel14Gen")] = true,
            [("H670", "Intel12Gen")] = false,
            [("H670", "Intel13Gen")] = true,
            [("H670", "Intel14Gen")] = true,
            [("H610", "Intel12Gen")] = false,
            [("H610", "Intel13Gen")] = true,
            [("H610", "Intel14Gen")] = true,

            // Intel LGA1700 — 700-series (launched with 13th gen)
            [("Z790", "Intel12Gen")] = false,
            [("Z790", "Intel13Gen")] = false,
            [("Z790", "Intel14Gen")] = true,
            [("B760", "Intel12Gen")] = false,
            [("B760", "Intel13Gen")] = false,
            [("B760", "Intel14Gen")] = true,
            [("H770", "Intel12Gen")] = false,
            [("H770", "Intel13Gen")] = false,
            [("H770", "Intel14Gen")] = true,

            // AMD AM5 — 600-series (launched with Ryzen 7000)
            [("X670E", "Ryzen7000")] = false,
            [("X670E", "Ryzen8000")] = true,
            [("X670E", "Ryzen9000")] = true,
            [("X670", "Ryzen7000")] = false,
            [("X670", "Ryzen8000")] = true,
            [("X670", "Ryzen9000")] = true,
            [("B650E", "Ryzen7000")] = false,
            [("B650E", "Ryzen8000")] = true,
            [("B650E", "Ryzen9000")] = true,
            [("B650", "Ryzen7000")] = false,
            [("B650", "Ryzen8000")] = true,
            [("B650", "Ryzen9000")] = true,
            [("A620", "Ryzen7000")] = false,
            [("A620", "Ryzen8000")] = true,
            [("A620", "Ryzen9000")] = true,

            // AMD AM5 — 800-series (launched with Ryzen 9000, ships pre-supporting the line)
            [("X870E", "Ryzen7000")] = false,
            [("X870E", "Ryzen8000")] = false,
            [("X870E", "Ryzen9000")] = false,
            [("X870", "Ryzen7000")] = false,
            [("X870", "Ryzen8000")] = false,
            [("X870", "Ryzen9000")] = false,
            [("B850", "Ryzen7000")] = false,
            [("B850", "Ryzen8000")] = false,
            [("B850", "Ryzen9000")] = false,
            [("B840", "Ryzen7000")] = false,
            [("B840", "Ryzen8000")] = false,
            [("B840", "Ryzen9000")] = false,

            // AMD AM4
            [("X370", "Ryzen1000")] = false,
            [("X370", "Ryzen2000")] = true,
            [("X370", "Ryzen3000")] = true,
            [("X370", "Ryzen5000")] = true,
            [("B350", "Ryzen1000")] = false,
            [("B350", "Ryzen2000")] = true,
            [("B350", "Ryzen3000")] = true,
            [("B350", "Ryzen5000")] = true,
            [("X470", "Ryzen1000")] = true,
            [("X470", "Ryzen2000")] = false,
            [("X470", "Ryzen3000")] = true,
            [("X470", "Ryzen5000")] = true,
            [("B450", "Ryzen1000")] = true,
            [("B450", "Ryzen2000")] = false,
            [("B450", "Ryzen3000")] = true,
            [("B450", "Ryzen5000")] = true,
            [("X570", "Ryzen3000")] = false,
            [("X570", "Ryzen5000")] = true,
            [("B550", "Ryzen3000")] = false,
            [("B550", "Ryzen5000")] = true,
            [("A320", "Ryzen1000")] = false,
            [("A320", "Ryzen2000")] = true,
            [("A320", "Ryzen3000")] = true,
            [("A320", "Ryzen5000")] = true,
            [("A520", "Ryzen3000")] = false,
            [("A520", "Ryzen5000")] = true,
        };

    /// <returns>
    /// <c>null</c> when the chipset does not support the CPU generation at all (hard block);
    /// otherwise <c>true</c>/<c>false</c> for whether a BIOS update is known to be required.
    /// </returns>
    public static bool? TryGetSupport(string chipset, string cpuGeneration) =>
        RequiresBiosUpdateByPair.TryGetValue((chipset, cpuGeneration), out var requiresBiosUpdate)
            ? requiresBiosUpdate
            : null;
}
