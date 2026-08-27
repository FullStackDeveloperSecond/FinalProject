namespace DoSelect.Domain.Builds;

public sealed record CpuFacts(Guid SkuPublicId, string? Socket, string? Generation, decimal? PowerWatts);

/// <summary>
/// <paramref name="SupportedStorageInterfaces"/> maps each interface code (e.g. "SATA", "NVME") to
/// its actual physical port count on the board — not just which interface kinds it supports.
/// SkuCompatibilityAttribute's (SkuId, AttributeKey, AttributeValue) unique index can't represent
/// "6 SATA ports" as 6 duplicate "SATA" rows, so EfCompatibilityFactsReader parses each attribute
/// value as an "{interface}:{portCount}" pair instead of a bare interface name (組長 PR #34 review
/// — port count, not interface-kind count, is what EvaluateStorageInterface must compare against).
/// </summary>
public sealed record MotherboardFacts(
    Guid SkuPublicId,
    string? Socket,
    string? Chipset,
    string? MemoryGeneration,
    int? MemorySlotCount,
    decimal? MaxMemoryCapacityGb,
    string? FormFactor,
    IReadOnlyDictionary<string, int> SupportedStorageInterfaces);

public sealed record MemoryModuleFacts(
    Guid SkuPublicId,
    string? Generation,
    decimal? CapacityGbPerModule,
    int Quantity);

public sealed record GraphicsCardFacts(
    Guid SkuPublicId,
    decimal? LengthMm,
    decimal? RecommendedPsuWatts,
    decimal? PowerWatts,
    IReadOnlyCollection<string> RequiredConnectors);

public sealed record StorageDeviceFacts(Guid SkuPublicId, string? Interface, decimal? PowerWatts, int Quantity = 1);

public sealed record PowerSupplyFacts(
    Guid SkuPublicId,
    decimal? WattageW,
    IReadOnlyCollection<string> AvailableConnectors);

public sealed record CaseFacts(
    Guid SkuPublicId,
    IReadOnlyCollection<string> SupportedFormFactors,
    decimal? MaxGpuLengthMm,
    decimal? MaxCoolerHeightMm);

public sealed record CoolerFacts(
    Guid SkuPublicId,
    decimal? HeightMm,
    IReadOnlyCollection<string> SupportedSockets,
    decimal? PowerWatts);

public sealed record BuildComponentSet(
    CpuFacts? Cpu,
    MotherboardFacts? Motherboard,
    IReadOnlyList<MemoryModuleFacts> Memory,
    GraphicsCardFacts? Gpu,
    IReadOnlyList<StorageDeviceFacts> Storage,
    PowerSupplyFacts? Psu,
    CaseFacts? Case,
    CoolerFacts? Cooler)
{
    public static readonly BuildComponentSet Empty = new(null, null, [], null, [], null, null, null);
}

public sealed record BuildCompatibilityWarningSettings(
    decimal GpuClearanceWarningMm = 20m,
    decimal CoolerClearanceWarningMm = 10m,
    decimal PsuReserveWarningPercent = 35m,
    decimal RemainingRamSlotWarningCount = 0m,
    decimal RemainingStoragePortWarningCount = 0m)
{
    public static readonly BuildCompatibilityWarningSettings Default = new();
}

/// <summary>
/// <see cref="Severity"/> is a lowerCamel token (<see cref="CompatibilitySeverityTokens"/>), not
/// <see cref="CompatibilityOverall"/> — a finding can be <c>ruleDisabled</c>
/// (相容性規則後台設計.md's "停用規則後結果顯示 RuleDisabled，不得回 Compatible"), a state that
/// legitimately applies to one finding but must never appear as the 4-value top-level
/// <see cref="BuildCompatibilityEvaluation.Overall"/>, so the two use separate types.
/// </summary>
public sealed record CompatibilityFinding(
    string RuleCode,
    string Severity,
    string MessageKey,
    IReadOnlyList<Guid> SubjectSkuPublicIds,
    IReadOnlyDictionary<string, object?> Facts);

public sealed record BuildCompatibilityEvaluation(
    CompatibilityOverall Overall,
    IReadOnlyList<CompatibilityFinding> Findings);

/// <summary>
/// Deterministic evaluator for the 12 hard compatibility checks in 商品、組裝與相容性.md
/// "相容性規則". Pure and side-effect free: callers assemble <see cref="BuildComponentSet"/> from
/// Sku specification data, and this class never touches the database. A rule is skipped entirely
/// (no finding) when the relevant component slots are both absent from the set — a required-but-
/// missing component is a build-completeness concern (`build_incomplete`), not a compatibility
/// rule finding. A present component with a missing structured fact yields
/// <see cref="CompatibilityOverall.InsufficientData"/>, never a silent "compatible".
/// </summary>
public static class CompatibilityRuleEngine
{
    /// <summary>
    /// Version of the hardcoded rule set itself (12 rules as of this writing). Bump when a
    /// rule's comparison logic changes, so a stored <c>CompatibilityCheckRun.RuleSetVersion</c>
    /// records which engine revision actually produced a historical result.
    /// </summary>
    public const int RuleSetVersion = 1;

    public static BuildCompatibilityEvaluation Evaluate(
        BuildComponentSet components,
        BuildCompatibilityWarningSettings? settings = null,
        IReadOnlySet<string>? disabledRuleCodes = null,
        IReadOnlySet<string>? onlyRuleCodes = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        settings ??= BuildCompatibilityWarningSettings.Default;
        disabledRuleCodes ??= new HashSet<string>();

        var evaluators = new (string RuleCode, Func<CompatibilityFinding?> Evaluate)[]
        {
            (BuildCompatibilityRuleCodes.CpuSocket, () => EvaluateCpuSocket(components)),
            (BuildCompatibilityRuleCodes.ChipsetCpuGeneration, () => EvaluateChipsetCpuGeneration(components)),
            (BuildCompatibilityRuleCodes.RamGeneration, () => EvaluateRamGeneration(components)),
            (BuildCompatibilityRuleCodes.RamSlotCount, () => EvaluateRamSlotCount(components, settings)),
            (BuildCompatibilityRuleCodes.RamCapacity, () => EvaluateRamCapacity(components)),
            (BuildCompatibilityRuleCodes.CaseFormFactor, () => EvaluateCaseFormFactor(components)),
            (BuildCompatibilityRuleCodes.GpuLength, () => EvaluateGpuLength(components, settings)),
            (BuildCompatibilityRuleCodes.CoolerSocket, () => EvaluateCoolerSocket(components)),
            (BuildCompatibilityRuleCodes.CoolerHeight, () => EvaluateCoolerHeight(components, settings)),
            (BuildCompatibilityRuleCodes.StorageInterface, () => EvaluateStorageInterface(components, settings)),
            (BuildCompatibilityRuleCodes.PsuCapacity, () => EvaluatePsuCapacity(components, settings)),
            (BuildCompatibilityRuleCodes.PsuConnectors, () => EvaluatePsuConnectors(components)),
        };

        var findings = new List<CompatibilityFinding>();
        foreach (var (ruleCode, evaluate) in evaluators)
        {
            if (onlyRuleCodes is not null && !onlyRuleCodes.Contains(ruleCode))
            {
                continue;
            }

            // Always invoked (even when disabled) so a disabled rule that wouldn't apply to this
            // build anyway (e.g. no matching component slots present) still produces no finding
            // — only a rule that *would* have fired gets surfaced as RuleDisabled below.
            var finding = evaluate();
            if (finding is null)
            {
                continue;
            }

            findings.Add(disabledRuleCodes.Contains(ruleCode)
                ? finding with
                {
                    Severity = CompatibilitySeverityTokens.RuleDisabled,
                    MessageKey = "compatibility.rule_disabled",
                    Facts = Facts(),
                }
                : finding);
        }

        var overall = findings.Count == 0
            ? CompatibilityOverall.Compatible
            : findings.Max(finding => SeverityRank(finding.Severity)) switch
            {
                3 => CompatibilityOverall.Blocked,
                2 => CompatibilityOverall.InsufficientData,
                1 => CompatibilityOverall.Warning,
                _ => CompatibilityOverall.Compatible,
            };

        return new BuildCompatibilityEvaluation(overall, findings);
    }

    /// <summary>
    /// Explicit precedence for rolling per-rule findings into one overall status: a definitive
    /// Blocked always wins over an unresolved InsufficientData, which in turn outranks a mere
    /// Warning. RuleDisabled (and Compatible, which never appears as a finding) rank lowest —
    /// a disabled rule is visible in the results list but never influences the rollup, since
    /// Overall has no 5th value to represent "skipped".
    /// </summary>
    private static int SeverityRank(string severity) => severity switch
    {
        CompatibilitySeverityTokens.Blocked => 3,
        CompatibilitySeverityTokens.InsufficientData => 2,
        CompatibilitySeverityTokens.Warning => 1,
        _ => 0,
    };

    private static CompatibilityFinding? EvaluateCpuSocket(BuildComponentSet c)
    {
        if (c.Cpu is null || c.Motherboard is null)
        {
            return null;
        }

        if (c.Cpu.Socket is null || c.Motherboard.Socket is null)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.CpuSocket,
                "compatibility.cpu_socket_insufficient_data",
                [c.Cpu.SkuPublicId, c.Motherboard.SkuPublicId]);
        }

        return c.Cpu.Socket == c.Motherboard.Socket
            ? null
            : Blocked(
                BuildCompatibilityRuleCodes.CpuSocket,
                "compatibility.cpu_socket_mismatch",
                [c.Cpu.SkuPublicId, c.Motherboard.SkuPublicId],
                Facts(("cpuSocket", c.Cpu.Socket), ("boardSocket", c.Motherboard.Socket)));
    }

    private static CompatibilityFinding? EvaluateChipsetCpuGeneration(BuildComponentSet c)
    {
        if (c.Cpu is null || c.Motherboard is null)
        {
            return null;
        }

        if (c.Cpu.Generation is null || c.Motherboard.Chipset is null)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.ChipsetCpuGeneration,
                "compatibility.chipset_cpu_generation_insufficient_data",
                [c.Cpu.SkuPublicId, c.Motherboard.SkuPublicId]);
        }

        var requiresBiosUpdate = ChipsetGenerationSupportMatrix.TryGetSupport(
            c.Motherboard.Chipset,
            c.Cpu.Generation);

        if (requiresBiosUpdate is null)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.ChipsetCpuGeneration,
                "compatibility.chipset_cpu_generation_unsupported",
                [c.Cpu.SkuPublicId, c.Motherboard.SkuPublicId],
                Facts(("chipset", c.Motherboard.Chipset), ("cpuGeneration", c.Cpu.Generation)));
        }

        return requiresBiosUpdate.Value
            ? Warning(
                BuildCompatibilityRuleCodes.ChipsetCpuGeneration,
                "compatibility.chipset_cpu_generation_requires_bios_update",
                [c.Cpu.SkuPublicId, c.Motherboard.SkuPublicId],
                Facts(("chipset", c.Motherboard.Chipset), ("cpuGeneration", c.Cpu.Generation)))
            : null;
    }

    private static CompatibilityFinding? EvaluateRamGeneration(BuildComponentSet c)
    {
        if (c.Motherboard is null || c.Memory.Count == 0)
        {
            return null;
        }

        if (c.Motherboard.MemoryGeneration is null || c.Memory.Any(module => module.Generation is null))
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.RamGeneration,
                "compatibility.ram_generation_insufficient_data",
                [c.Motherboard.SkuPublicId, .. c.Memory.Select(module => module.SkuPublicId)]);
        }

        var mismatched = c.Memory.Where(module => module.Generation != c.Motherboard.MemoryGeneration).ToList();
        return mismatched.Count == 0
            ? null
            : Blocked(
                BuildCompatibilityRuleCodes.RamGeneration,
                "compatibility.ram_generation_mismatch",
                [c.Motherboard.SkuPublicId, .. mismatched.Select(module => module.SkuPublicId)],
                Facts(
                    ("boardMemoryGeneration", c.Motherboard.MemoryGeneration),
                    ("memoryGenerations", mismatched.Select(module => module.Generation).Distinct())));
    }

    private static CompatibilityFinding? EvaluateRamSlotCount(BuildComponentSet c, BuildCompatibilityWarningSettings settings)
    {
        if (c.Motherboard is null || c.Memory.Count == 0)
        {
            return null;
        }

        if (c.Motherboard.MemorySlotCount is null)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.RamSlotCount,
                "compatibility.ram_slot_count_insufficient_data",
                [c.Motherboard.SkuPublicId]);
        }

        var moduleCount = c.Memory.Sum(module => module.Quantity);
        var slotCount = c.Motherboard.MemorySlotCount.Value;
        if (moduleCount > slotCount)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.RamSlotCount,
                "compatibility.ram_slot_count_exceeded",
                [c.Motherboard.SkuPublicId, .. c.Memory.Select(module => module.SkuPublicId)],
                Facts(("boardMemorySlotCount", slotCount), ("requestedModuleCount", moduleCount)));
        }

        var remainingSlots = slotCount - moduleCount;
        return remainingSlots <= settings.RemainingRamSlotWarningCount
            ? Warning(
                BuildCompatibilityRuleCodes.RamSlotCount,
                "compatibility.ram_slot_count_low_headroom",
                [c.Motherboard.SkuPublicId],
                Facts(("remainingSlots", remainingSlots)))
            : null;
    }

    private static CompatibilityFinding? EvaluateRamCapacity(BuildComponentSet c)
    {
        if (c.Motherboard is null || c.Memory.Count == 0)
        {
            return null;
        }

        if (c.Motherboard.MaxMemoryCapacityGb is null || c.Memory.Any(module => module.CapacityGbPerModule is null))
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.RamCapacity,
                "compatibility.ram_capacity_insufficient_data",
                [c.Motherboard.SkuPublicId, .. c.Memory.Select(module => module.SkuPublicId)]);
        }

        var totalCapacityGb = c.Memory.Sum(module => module.CapacityGbPerModule!.Value * module.Quantity);
        return totalCapacityGb > c.Motherboard.MaxMemoryCapacityGb.Value
            ? Blocked(
                BuildCompatibilityRuleCodes.RamCapacity,
                "compatibility.ram_capacity_exceeded",
                [c.Motherboard.SkuPublicId, .. c.Memory.Select(module => module.SkuPublicId)],
                Facts(
                    ("boardMaxMemoryCapacityGb", c.Motherboard.MaxMemoryCapacityGb.Value),
                    ("requestedCapacityGb", totalCapacityGb)))
            : null;
    }

    private static CompatibilityFinding? EvaluateCaseFormFactor(BuildComponentSet c)
    {
        if (c.Motherboard is null || c.Case is null)
        {
            return null;
        }

        if (c.Motherboard.FormFactor is null || c.Case.SupportedFormFactors.Count == 0)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.CaseFormFactor,
                "compatibility.case_form_factor_insufficient_data",
                [c.Motherboard.SkuPublicId, c.Case.SkuPublicId]);
        }

        return c.Case.SupportedFormFactors.Contains(c.Motherboard.FormFactor)
            ? null
            : Blocked(
                BuildCompatibilityRuleCodes.CaseFormFactor,
                "compatibility.case_form_factor_mismatch",
                [c.Motherboard.SkuPublicId, c.Case.SkuPublicId],
                Facts(
                    ("boardFormFactor", c.Motherboard.FormFactor),
                    ("caseSupportedFormFactors", c.Case.SupportedFormFactors)));
    }

    private static CompatibilityFinding? EvaluateGpuLength(BuildComponentSet c, BuildCompatibilityWarningSettings settings)
    {
        if (c.Gpu is null || c.Case is null)
        {
            return null;
        }

        if (c.Gpu.LengthMm is null || c.Case.MaxGpuLengthMm is null)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.GpuLength,
                "compatibility.gpu_length_insufficient_data",
                [c.Gpu.SkuPublicId, c.Case.SkuPublicId]);
        }

        if (c.Gpu.LengthMm.Value > c.Case.MaxGpuLengthMm.Value)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.GpuLength,
                "compatibility.gpu_length_exceeded",
                [c.Gpu.SkuPublicId, c.Case.SkuPublicId],
                Facts(("gpuLengthMm", c.Gpu.LengthMm.Value), ("caseMaxGpuLengthMm", c.Case.MaxGpuLengthMm.Value)));
        }

        var clearanceMm = c.Case.MaxGpuLengthMm.Value - c.Gpu.LengthMm.Value;
        return clearanceMm <= settings.GpuClearanceWarningMm
            ? Warning(
                BuildCompatibilityRuleCodes.GpuLength,
                "compatibility.gpu_length_low_clearance",
                [c.Gpu.SkuPublicId, c.Case.SkuPublicId],
                Facts(("clearanceMm", clearanceMm)))
            : null;
    }

    private static CompatibilityFinding? EvaluateCoolerSocket(BuildComponentSet c)
    {
        if (c.Cooler is null || c.Cpu is null)
        {
            return null;
        }

        if (c.Cpu.Socket is null || c.Cooler.SupportedSockets.Count == 0)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.CoolerSocket,
                "compatibility.cooler_socket_insufficient_data",
                [c.Cooler.SkuPublicId, c.Cpu.SkuPublicId]);
        }

        return c.Cooler.SupportedSockets.Contains(c.Cpu.Socket)
            ? null
            : Blocked(
                BuildCompatibilityRuleCodes.CoolerSocket,
                "compatibility.cooler_socket_mismatch",
                [c.Cooler.SkuPublicId, c.Cpu.SkuPublicId],
                Facts(("cpuSocket", c.Cpu.Socket), ("coolerSupportedSockets", c.Cooler.SupportedSockets)));
    }

    private static CompatibilityFinding? EvaluateCoolerHeight(BuildComponentSet c, BuildCompatibilityWarningSettings settings)
    {
        if (c.Cooler is null || c.Case is null)
        {
            return null;
        }

        if (c.Cooler.HeightMm is null || c.Case.MaxCoolerHeightMm is null)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.CoolerHeight,
                "compatibility.cooler_height_insufficient_data",
                [c.Cooler.SkuPublicId, c.Case.SkuPublicId]);
        }

        if (c.Cooler.HeightMm.Value > c.Case.MaxCoolerHeightMm.Value)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.CoolerHeight,
                "compatibility.cooler_height_exceeded",
                [c.Cooler.SkuPublicId, c.Case.SkuPublicId],
                Facts(("coolerHeightMm", c.Cooler.HeightMm.Value), ("caseMaxCoolerHeightMm", c.Case.MaxCoolerHeightMm.Value)));
        }

        var clearanceMm = c.Case.MaxCoolerHeightMm.Value - c.Cooler.HeightMm.Value;
        return clearanceMm <= settings.CoolerClearanceWarningMm
            ? Warning(
                BuildCompatibilityRuleCodes.CoolerHeight,
                "compatibility.cooler_height_low_clearance",
                [c.Cooler.SkuPublicId, c.Case.SkuPublicId],
                Facts(("clearanceMm", clearanceMm)))
            : null;
    }

    private static CompatibilityFinding? EvaluateStorageInterface(BuildComponentSet c, BuildCompatibilityWarningSettings settings)
    {
        if (c.Motherboard is null || c.Storage.Count == 0)
        {
            return null;
        }

        if (c.Motherboard.SupportedStorageInterfaces.Count == 0 || c.Storage.Any(device => device.Interface is null))
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.StorageInterface,
                "compatibility.storage_interface_insufficient_data",
                [c.Motherboard.SkuPublicId, .. c.Storage.Select(device => device.SkuPublicId)]);
        }

        var unsupported = c.Storage
            .Where(device => !c.Motherboard.SupportedStorageInterfaces.ContainsKey(device.Interface!))
            .ToList();
        if (unsupported.Count > 0)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.StorageInterface,
                "compatibility.storage_interface_unsupported",
                [c.Motherboard.SkuPublicId, .. unsupported.Select(device => device.SkuPublicId)],
                Facts(
                    ("boardSupportedStorageInterfaces", c.Motherboard.SupportedStorageInterfaces.Keys),
                    ("unsupportedInterfaces", unsupported.Select(device => device.Interface).Distinct())));
        }

        // Grouped by interface — a board's SATA and M.2 port counts are independent pools, so 8
        // SATA devices must be checked against the board's actual SATA port count, not against
        // however many *kinds* of interface it happens to support (組長 PR #34 review).
        var usageByInterface = c.Storage
            .GroupBy(device => device.Interface!)
            .Select(group => new
            {
                Interface = group.Key,
                Used = group.Sum(device => device.Quantity),
                Ports = c.Motherboard.SupportedStorageInterfaces[group.Key],
                Devices = group.Select(device => device.SkuPublicId).ToList(),
            })
            .ToList();

        var overCapacity = usageByInterface.Where(row => row.Used > row.Ports).ToList();
        if (overCapacity.Count > 0)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.StorageInterface,
                "compatibility.storage_interface_port_exceeded",
                [c.Motherboard.SkuPublicId, .. overCapacity.SelectMany(row => row.Devices)],
                Facts(("overCapacityInterfaces", overCapacity.Select(row => new { row.Interface, row.Used, row.Ports }))));
        }

        var worst = usageByInterface.MinBy(row => row.Ports - row.Used)!;
        var remainingPorts = worst.Ports - worst.Used;
        return remainingPorts <= settings.RemainingStoragePortWarningCount
            ? Warning(
                BuildCompatibilityRuleCodes.StorageInterface,
                "compatibility.storage_interface_low_headroom",
                [c.Motherboard.SkuPublicId],
                Facts(("interface", worst.Interface), ("remainingPorts", remainingPorts)))
            : null;
    }

    private static CompatibilityFinding? EvaluatePsuCapacity(BuildComponentSet c, BuildCompatibilityWarningSettings settings)
    {
        if (c.Psu is null)
        {
            return null;
        }

        var powerContributors = new List<(Guid SkuPublicId, decimal? PowerWatts)>();
        if (c.Cpu is not null)
        {
            powerContributors.Add((c.Cpu.SkuPublicId, c.Cpu.PowerWatts));
        }

        if (c.Gpu is not null)
        {
            powerContributors.Add((c.Gpu.SkuPublicId, c.Gpu.PowerWatts));
        }

        powerContributors.AddRange(c.Storage.Select(device => (
            device.SkuPublicId,
            device.PowerWatts.HasValue ? device.PowerWatts.Value * device.Quantity : (decimal?)null)));
        if (c.Cooler is not null)
        {
            powerContributors.Add((c.Cooler.SkuPublicId, c.Cooler.PowerWatts));
        }

        // A present PSU with an unknown rated wattage can never be reliably evaluated — this must
        // never fall through to the powerContributors.Count == 0 skip below, or a PSU with no
        // WattageW fact at all would silently read as "no finding" instead of InsufficientData.
        if (c.Psu.WattageW is null)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.PsuCapacity,
                "compatibility.psu_capacity_insufficient_data",
                [c.Psu.SkuPublicId, .. powerContributors.Select(contributor => contributor.SkuPublicId)]);
        }

        if (powerContributors.Count == 0)
        {
            return null;
        }

        // A present GPU with an unknown vendor-recommended PSU wattage can't be reliably compared
        // against requiredMinimumWatts either — treating it as 0 (組長 PR #34 review) let an
        // underpowered PSU pass silently whenever this one fact was missing, even though every
        // other power fact was known.
        if (powerContributors.Any(contributor => contributor.PowerWatts is null) ||
            (c.Gpu is not null && c.Gpu.RecommendedPsuWatts is null))
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.PsuCapacity,
                "compatibility.psu_capacity_insufficient_data",
                [c.Psu.SkuPublicId, .. powerContributors.Select(contributor => contributor.SkuPublicId)]);
        }

        var estimatedWatts = powerContributors.Sum(contributor => contributor.PowerWatts!.Value);
        var requiredWithMargin = estimatedWatts * 1.3m;
        var tierRequirement = PsuTierLadder.RoundUpToTier(requiredWithMargin);
        if (tierRequirement is null)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.PsuCapacity,
                "compatibility.psu_no_suitable_tier",
                [c.Psu.SkuPublicId],
                Facts(("estimatedWatts", estimatedWatts), ("requiredWithMarginWatts", requiredWithMargin)));
        }

        var gpuRecommendedWatts = c.Gpu?.RecommendedPsuWatts ?? 0m;
        var requiredMinimumWatts = Math.Max(tierRequirement.Value, gpuRecommendedWatts);
        if (c.Psu.WattageW.Value < requiredMinimumWatts)
        {
            return Blocked(
                BuildCompatibilityRuleCodes.PsuCapacity,
                "compatibility.psu_capacity_insufficient",
                [c.Psu.SkuPublicId],
                Facts(
                    ("psuWattageW", c.Psu.WattageW.Value),
                    ("requiredMinimumWatts", requiredMinimumWatts),
                    ("tierRequirementWatts", tierRequirement.Value),
                    ("gpuRecommendedWatts", gpuRecommendedWatts)));
        }

        var reservePercent = (c.Psu.WattageW.Value - estimatedWatts) / estimatedWatts * 100m;
        return reservePercent < settings.PsuReserveWarningPercent
            ? Warning(
                BuildCompatibilityRuleCodes.PsuCapacity,
                "compatibility.psu_reserve_low",
                [c.Psu.SkuPublicId],
                Facts(("reservePercent", reservePercent)))
            : null;
    }

    private static CompatibilityFinding? EvaluatePsuConnectors(BuildComponentSet c)
    {
        if (c.Psu is null || c.Gpu is null || c.Gpu.RequiredConnectors.Count == 0)
        {
            return null;
        }

        if (c.Psu.AvailableConnectors.Count == 0)
        {
            return InsufficientData(
                BuildCompatibilityRuleCodes.PsuConnectors,
                "compatibility.psu_connectors_insufficient_data",
                [c.Psu.SkuPublicId, c.Gpu.SkuPublicId]);
        }

        // Counted (multiset) comparison: a GPU needing two 8-pin connectors is not satisfied by
        // a PSU that only lists one, even though a plain set-difference would consider "8pin"
        // as already present and miss the shortfall.
        var requiredCounts = c.Gpu.RequiredConnectors
            .GroupBy(connector => connector)
            .ToDictionary(group => group.Key, group => group.Count());
        var availableCounts = c.Psu.AvailableConnectors
            .GroupBy(connector => connector)
            .ToDictionary(group => group.Key, group => group.Count());
        var missingConnectors = requiredCounts
            .Where(entry => availableCounts.GetValueOrDefault(entry.Key) < entry.Value)
            .Select(entry => entry.Key)
            .ToList();
        return missingConnectors.Count == 0
            ? null
            : Blocked(
                BuildCompatibilityRuleCodes.PsuConnectors,
                "compatibility.psu_connectors_missing",
                [c.Psu.SkuPublicId, c.Gpu.SkuPublicId],
                Facts(
                    ("psuAvailableConnectors", c.Psu.AvailableConnectors),
                    ("missingConnectors", missingConnectors)));
    }

    private static CompatibilityFinding Blocked(
        string ruleCode,
        string messageKey,
        IReadOnlyList<Guid> subjectSkuPublicIds,
        IReadOnlyDictionary<string, object?> facts) =>
        new(ruleCode, CompatibilitySeverityTokens.Blocked, messageKey, subjectSkuPublicIds, facts);

    private static CompatibilityFinding Warning(
        string ruleCode,
        string messageKey,
        IReadOnlyList<Guid> subjectSkuPublicIds,
        IReadOnlyDictionary<string, object?> facts) =>
        new(ruleCode, CompatibilitySeverityTokens.Warning, messageKey, subjectSkuPublicIds, facts);

    private static CompatibilityFinding InsufficientData(
        string ruleCode,
        string messageKey,
        IReadOnlyList<Guid> subjectSkuPublicIds) =>
        new(ruleCode, CompatibilitySeverityTokens.InsufficientData, messageKey, subjectSkuPublicIds, Facts());

    private static IReadOnlyDictionary<string, object?> Facts(params (string Key, object? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value);
}
