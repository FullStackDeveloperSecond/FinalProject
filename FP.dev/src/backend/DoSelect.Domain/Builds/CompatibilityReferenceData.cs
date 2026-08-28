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

/// <summary>Wire tokens for a per-finding <c>CompatibilityFindingDto.Severity</c> string.</summary>
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
/// Each of the 5 adjustable warning thresholds belongs to exactly one of the canonical
/// <see cref="CompatibilityRuleCodes"/> rules (相容性規則後台設計.md's table); the other rules are
/// pure hard-blocking checks with no admin-adjustable threshold. PR #34 round-7 review (DEC-BATCH-027):
/// remapped from this PR's own now-removed rule codes to the canonical evaluator's.
/// </summary>
public static class CompatibilityRuleWarningSettingMap
{
    public static readonly IReadOnlyDictionary<string, string> RuleCodeToSettingCode =
        new Dictionary<string, string>
        {
            [CompatibilityRuleCodes.GpuLength] = CompatibilityWarningSettingCodes.GpuClearanceWarningMm,
            [CompatibilityRuleCodes.CoolerHeight] = CompatibilityWarningSettingCodes.CoolerClearanceWarningMm,
            [CompatibilityRuleCodes.PsuCapacity] = CompatibilityWarningSettingCodes.PsuReserveWarningPercent,
            [CompatibilityRuleCodes.MemorySlots] = CompatibilityWarningSettingCodes.RemainingRamSlotWarningCount,
            [CompatibilityRuleCodes.StorageInterface] = CompatibilityWarningSettingCodes.RemainingStoragePortWarningCount,
        };

    public static string? TryGetSettingCode(string ruleCode) => RuleCodeToSettingCode.GetValueOrDefault(ruleCode);
}
