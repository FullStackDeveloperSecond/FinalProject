using DoSelect.Domain.Catalog;

namespace DoSelect.Domain.Builds;

public sealed record CompatibilitySpecification(
    decimal? DecimalValue,
    string? OptionCode,
    IReadOnlySet<string>? OptionCodes)
{
    public static CompatibilitySpecification FromDecimal(decimal value) => new(value, null, null);

    public static CompatibilitySpecification FromOption(string value) =>
        new(null, Normalize(value), null);

    public static CompatibilitySpecification FromOptions(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var normalized = values.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("At least one option is required.", nameof(values));
        }

        return new CompatibilitySpecification(null, null, normalized);
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The option code is required.", nameof(value))
            : value.Trim().ToUpperInvariant();
}

public sealed record CompatibilityComponent
{
    public CompatibilityComponent(
        Guid skuPublicId,
        string categoryCode,
        int quantity,
        IReadOnlyDictionary<string, CompatibilitySpecification> specifications)
    {
        if (skuPublicId == Guid.Empty || string.IsNullOrWhiteSpace(categoryCode) ||
            quantity <= 0 || specifications is null)
        {
            throw new ArgumentException("The compatibility component is invalid.");
        }

        SkuPublicId = skuPublicId;
        CategoryCode = categoryCode.Trim().ToUpperInvariant();
        Quantity = quantity;
        Specifications = specifications;
    }

    public Guid SkuPublicId { get; init; }
    public string CategoryCode { get; init; }
    public int Quantity { get; init; }
    public IReadOnlyDictionary<string, CompatibilitySpecification> Specifications { get; init; }
}

public sealed record CompatibilityWarningSettings
{
    public CompatibilityWarningSettings(
        decimal gpuClearanceWarningMm,
        decimal coolerClearanceWarningMm,
        decimal psuReserveWarningPercent,
        int remainingRamSlotWarningCount,
        int remainingStoragePortWarningCount)
    {
        if (gpuClearanceWarningMm is < 10m or > 50m ||
            coolerClearanceWarningMm is < 5m or > 30m ||
            psuReserveWarningPercent is < 30m or > 50m ||
            remainingRamSlotWarningCount is < 0 or > 2 ||
            remainingStoragePortWarningCount is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(gpuClearanceWarningMm));
        }

        GpuClearanceWarningMm = gpuClearanceWarningMm;
        CoolerClearanceWarningMm = coolerClearanceWarningMm;
        PsuReserveWarningPercent = psuReserveWarningPercent;
        RemainingRamSlotWarningCount = remainingRamSlotWarningCount;
        RemainingStoragePortWarningCount = remainingStoragePortWarningCount;
    }

    public decimal GpuClearanceWarningMm { get; }
    public decimal CoolerClearanceWarningMm { get; }
    public decimal PsuReserveWarningPercent { get; }
    public int RemainingRamSlotWarningCount { get; }
    public int RemainingStoragePortWarningCount { get; }
}

public sealed record CpuChipsetCompatibility(
    string ChipsetCode,
    string CpuGenerationCode,
    bool RequiresBiosUpdate);

public sealed class CompatibilityRuleCatalog
{
    private readonly IReadOnlyDictionary<(string Chipset, string Generation), CpuChipsetCompatibility>
        _cpuChipsetRules;

    public CompatibilityRuleCatalog(IEnumerable<CpuChipsetCompatibility> cpuChipsetRules)
    {
        ArgumentNullException.ThrowIfNull(cpuChipsetRules);
        var normalized = cpuChipsetRules.Select(rule => new CpuChipsetCompatibility(
            Normalize(rule.ChipsetCode),
            Normalize(rule.CpuGenerationCode),
            rule.RequiresBiosUpdate)).ToArray();
        if (normalized.Length == 0 || normalized
            .GroupBy(rule => (rule.ChipsetCode, rule.CpuGenerationCode))
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("CPU/chipset rules must be non-empty and unique.", nameof(cpuChipsetRules));
        }

        _cpuChipsetRules = normalized.ToDictionary(
            rule => (rule.ChipsetCode, rule.CpuGenerationCode));
    }

    public bool HasChipset(string chipset) =>
        _cpuChipsetRules.Keys.Any(key => key.Chipset == Normalize(chipset));

    public bool TryGet(string chipset, string generation, out CpuChipsetCompatibility? rule) =>
        _cpuChipsetRules.TryGetValue((Normalize(chipset), Normalize(generation)), out rule);

    public static CompatibilityRuleCatalog CreateVersion1() => new(
    [
        Rule("X570", "RYZEN_2000"),
        Rule("X570", "RYZEN_3000_G"),
        Rule("X570", "RYZEN_3000"),
        Rule("X570", "RYZEN_4000"),
        Rule("X570", "RYZEN_5000"),
        Rule("B550", "RYZEN_3000"),
        Rule("B550", "RYZEN_4000"),
        Rule("B550", "RYZEN_5000"),
        Rule("A520", "RYZEN_3000"),
        Rule("A520", "RYZEN_4000"),
        Rule("A520", "RYZEN_5000"),
        Rule("B450", "RYZEN_2000"),
        Rule("B450", "RYZEN_3000_G"),
        Rule("B450", "RYZEN_3000"),
        Rule("B450", "RYZEN_4000", requiresBiosUpdate: true),
        Rule("B450", "RYZEN_5000", requiresBiosUpdate: true),

        Rule("A620", "RYZEN_7000"),
        Rule("A620", "RYZEN_8000", requiresBiosUpdate: true),
        Rule("A620", "RYZEN_9000", requiresBiosUpdate: true),
        Rule("B650", "RYZEN_7000"),
        Rule("B650", "RYZEN_8000", requiresBiosUpdate: true),
        Rule("B650", "RYZEN_9000", requiresBiosUpdate: true),
        Rule("B650E", "RYZEN_7000"),
        Rule("B650E", "RYZEN_8000", requiresBiosUpdate: true),
        Rule("B650E", "RYZEN_9000", requiresBiosUpdate: true),
        Rule("X670", "RYZEN_7000"),
        Rule("X670", "RYZEN_8000", requiresBiosUpdate: true),
        Rule("X670", "RYZEN_9000", requiresBiosUpdate: true),
        Rule("X670E", "RYZEN_7000"),
        Rule("X670E", "RYZEN_8000", requiresBiosUpdate: true),
        Rule("X670E", "RYZEN_9000", requiresBiosUpdate: true),
        Rule("B840", "RYZEN_7000"),
        Rule("B840", "RYZEN_8000"),
        Rule("B840", "RYZEN_9000"),
        Rule("B850", "RYZEN_7000"),
        Rule("B850", "RYZEN_8000"),
        Rule("B850", "RYZEN_9000"),
        Rule("X870", "RYZEN_7000"),
        Rule("X870", "RYZEN_8000"),
        Rule("X870", "RYZEN_9000"),
        Rule("X870E", "RYZEN_7000"),
        Rule("X870E", "RYZEN_8000"),
        Rule("X870E", "RYZEN_9000"),

        Rule("H610", "INTEL_CORE_12"),
        Rule("H610", "INTEL_CORE_13", requiresBiosUpdate: true),
        Rule("H610", "INTEL_CORE_14", requiresBiosUpdate: true),
        Rule("B660", "INTEL_CORE_12"),
        Rule("B660", "INTEL_CORE_13", requiresBiosUpdate: true),
        Rule("B660", "INTEL_CORE_14", requiresBiosUpdate: true),
        Rule("H670", "INTEL_CORE_12"),
        Rule("H670", "INTEL_CORE_13", requiresBiosUpdate: true),
        Rule("H670", "INTEL_CORE_14", requiresBiosUpdate: true),
        Rule("Z690", "INTEL_CORE_12"),
        Rule("Z690", "INTEL_CORE_13", requiresBiosUpdate: true),
        Rule("Z690", "INTEL_CORE_14", requiresBiosUpdate: true),
        Rule("B760", "INTEL_CORE_12"),
        Rule("B760", "INTEL_CORE_13"),
        Rule("B760", "INTEL_CORE_14", requiresBiosUpdate: true),
        Rule("H770", "INTEL_CORE_12"),
        Rule("H770", "INTEL_CORE_13"),
        Rule("H770", "INTEL_CORE_14", requiresBiosUpdate: true),
        Rule("Z790", "INTEL_CORE_12"),
        Rule("Z790", "INTEL_CORE_13"),
        Rule("Z790", "INTEL_CORE_14", requiresBiosUpdate: true),

        Rule("H810", "INTEL_CORE_ULTRA_200"),
        Rule("B860", "INTEL_CORE_ULTRA_200"),
        Rule("Z890", "INTEL_CORE_ULTRA_200"),
    ]);

    private static CpuChipsetCompatibility Rule(
        string chipsetCode,
        string cpuGenerationCode,
        bool requiresBiosUpdate = false) =>
        new(chipsetCode, cpuGenerationCode, requiresBiosUpdate);

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The compatibility code is required.", nameof(value))
            : value.Trim().ToUpperInvariant();
}

public static class CompatibilityRuleCodes
{
    public const string RequiredComponent = "BUILD_REQUIRED_COMPONENT";
    public const string CpuSocket = "CPU_SOCKET";
    public const string CpuChipset = "CPU_CHIPSET";
    public const string BiosUpdate = "BIOS_UPDATE";
    public const string MemoryType = "MEMORY_TYPE";
    public const string MemorySlots = "MEMORY_SLOTS";
    public const string MemoryCapacity = "MEMORY_CAPACITY";
    public const string MotherboardFormFactor = "MOTHERBOARD_FORM_FACTOR";
    public const string GpuLength = "GPU_LENGTH";
    public const string CoolerSocket = "COOLER_SOCKET";
    public const string CoolerHeight = "COOLER_HEIGHT";
    public const string StorageInterface = "STORAGE_INTERFACE";
    public const string PsuCapacity = "PSU_CAPACITY";
    public const string PsuConnectors = "PSU_CONNECTORS";
    public const string PsuFormFactor = "PSU_FORM_FACTOR";

    /// <summary>PR #34 round-7 review (DEC-BATCH-027): every rule code this evaluator can ever report, for the admin rule-management surface's "known rule code" whitelist and its no-write test tool's per-rule filter.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        RequiredComponent, CpuSocket, CpuChipset, BiosUpdate, MemoryType, MemorySlots,
        MemoryCapacity, MotherboardFormFactor, GpuLength, CoolerSocket, CoolerHeight,
        StorageInterface, PsuCapacity, PsuConnectors, PsuFormFactor,
    ];
}

public sealed record CompatibilityRuleEvaluation(
    string RuleCode,
    CompatibilityOverall Severity,
    string MessageKey,
    IReadOnlyList<Guid> SubjectSkuPublicIds,
    IReadOnlyDictionary<string, string> Facts);

public sealed record CompatibilityEvaluation(
    CompatibilityOverall Overall,
    IReadOnlyList<CompatibilityRuleEvaluation> Results,
    decimal? RequiredPsuWatts);

public static class CompatibilityEvaluator
{
    private static readonly decimal[] PsuTiers = [450m, 550m, 650m, 750m, 850m, 1_000m, 1_200m, 1_500m];
    private static readonly string[] SingletonCategories =
    [
        CompatibilityCatalogContract.Categories.Cpu,
        CompatibilityCatalogContract.Categories.Motherboard,
        CompatibilityCatalogContract.Categories.Gpu,
        CompatibilityCatalogContract.Categories.Psu,
        CompatibilityCatalogContract.Categories.Case,
        CompatibilityCatalogContract.Categories.CpuCooler,
    ];

    public static CompatibilityEvaluation Evaluate(
        IReadOnlyCollection<CompatibilityComponent> components,
        CompatibilityWarningSettings settings,
        CompatibilityRuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);
        var findings = new List<CompatibilityRuleEvaluation>();
        var groups = components.GroupBy(component => component.CategoryCode)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var category in SingletonCategories)
        {
            if (!groups.TryGetValue(category, out var matches) || matches.Length != 1 ||
                matches[0].Quantity != 1)
            {
                Add(findings, CompatibilityRuleCodes.RequiredComponent,
                    CompatibilityOverall.InsufficientData,
                    "compatibility.required_component_invalid", [],
                    ("categoryCode", category));
            }
        }

        foreach (var category in new[]
                 {
                     CompatibilityCatalogContract.Categories.Memory,
                     CompatibilityCatalogContract.Categories.Storage,
                 })
        {
            if (!groups.TryGetValue(category, out var matches) || matches.Length == 0)
            {
                Add(findings, CompatibilityRuleCodes.RequiredComponent,
                    CompatibilityOverall.InsufficientData,
                    "compatibility.required_component_missing", [],
                    ("categoryCode", category));
            }
        }

        if (findings.Any(finding => finding.RuleCode == CompatibilityRuleCodes.RequiredComponent))
        {
            return Complete(findings, null);
        }

        var cpu = groups[CompatibilityCatalogContract.Categories.Cpu][0];
        var board = groups[CompatibilityCatalogContract.Categories.Motherboard][0];
        var gpu = groups[CompatibilityCatalogContract.Categories.Gpu][0];
        var psu = groups[CompatibilityCatalogContract.Categories.Psu][0];
        var chassis = groups[CompatibilityCatalogContract.Categories.Case][0];
        var cooler = groups[CompatibilityCatalogContract.Categories.CpuCooler][0];
        var memories = groups[CompatibilityCatalogContract.Categories.Memory];
        var storages = groups[CompatibilityCatalogContract.Categories.Storage];

        EvaluateCpuSocket(cpu, board, findings);
        EvaluateChipset(cpu, board, catalog, findings);
        EvaluateMemory(board, memories, settings, findings);
        EvaluatePhysical(cpu, board, gpu, psu, chassis, cooler, settings, findings);
        EvaluateStorage(board, storages, settings, findings);
        var requiredPsuWatts = EvaluatePsu(components, board, gpu, psu, settings, findings);

        return Complete(findings, requiredPsuWatts);
    }

    /// <summary>
    /// Evaluates only relationships that can be proven from the supplied components. This is used
    /// by recommendation flows where a customer may own one or more parts but is not submitting a
    /// complete build. It shares the same rule methods and settings as the complete-build path and
    /// never treats a missing, unrelated component as evidence of incompatibility.
    /// </summary>
    public static CompatibilityEvaluation EvaluatePartial(
        IReadOnlyCollection<CompatibilityComponent> components,
        CompatibilityWarningSettings settings,
        CompatibilityRuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);
        if (components.Count is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(components));
        }

        var findings = new List<CompatibilityRuleEvaluation>();
        var groups = components.GroupBy(component => component.CategoryCode)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var category in SingletonCategories)
        {
            if (groups.TryGetValue(category, out var matches) &&
                (matches.Length != 1 || matches[0].Quantity != 1))
            {
                Add(findings, CompatibilityRuleCodes.RequiredComponent,
                    CompatibilityOverall.InsufficientData,
                    "compatibility.required_component_invalid", [],
                    ("categoryCode", category));
            }
        }

        if (findings.Count > 0)
        {
            return Complete(findings, null);
        }

        CompatibilityComponent? One(string category) =>
            groups.TryGetValue(category, out var matches) ? matches[0] : null;

        var cpu = One(CompatibilityCatalogContract.Categories.Cpu);
        var board = One(CompatibilityCatalogContract.Categories.Motherboard);
        var gpu = One(CompatibilityCatalogContract.Categories.Gpu);
        var psu = One(CompatibilityCatalogContract.Categories.Psu);
        var chassis = One(CompatibilityCatalogContract.Categories.Case);
        var cooler = One(CompatibilityCatalogContract.Categories.CpuCooler);

        if (cpu is not null && board is not null)
        {
            EvaluateCpuSocket(cpu, board, findings);
            EvaluateChipset(cpu, board, catalog, findings);
        }

        if (board is not null && groups.TryGetValue(CompatibilityCatalogContract.Categories.Memory, out var memories))
        {
            EvaluateMemory(board, memories, settings, findings);
        }

        if (board is not null && chassis is not null)
        {
            CompareSupportedOption(board, CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor,
                chassis, CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor,
                CompatibilityRuleCodes.MotherboardFormFactor,
                "compatibility.motherboard_form_factor_unsupported", findings);
        }

        if (gpu is not null && chassis is not null)
        {
            CompareMaximum(gpu, CompatibilityCatalogContract.SemanticKeys.GpuLengthMm,
                chassis, CompatibilityCatalogContract.SemanticKeys.CaseGpuMaxLengthMm,
                settings.GpuClearanceWarningMm, CompatibilityRuleCodes.GpuLength,
                "compatibility.gpu_too_long", "compatibility.gpu_clearance_low", findings);
        }

        if (cpu is not null && cooler is not null)
        {
            CompareSupportedOption(cpu, CompatibilityCatalogContract.SemanticKeys.CpuSocket,
                cooler, CompatibilityCatalogContract.SemanticKeys.CpuSocket,
                CompatibilityRuleCodes.CoolerSocket,
                "compatibility.cooler_socket_unsupported", findings);
        }

        if (cooler is not null && chassis is not null)
        {
            CompareMaximum(cooler, CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm,
                chassis, CompatibilityCatalogContract.SemanticKeys.CaseCoolerMaxHeightMm,
                settings.CoolerClearanceWarningMm, CompatibilityRuleCodes.CoolerHeight,
                "compatibility.cooler_too_tall", "compatibility.cooler_clearance_low", findings);
        }

        if (psu is not null && chassis is not null)
        {
            CompareSupportedOption(psu, CompatibilityCatalogContract.SemanticKeys.PsuFormFactor,
                chassis, CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor,
                CompatibilityRuleCodes.PsuFormFactor,
                "compatibility.psu_form_factor_unsupported", findings);
        }

        if (board is not null && groups.TryGetValue(CompatibilityCatalogContract.Categories.Storage, out var storages))
        {
            EvaluateStorage(board, storages, settings, findings);
        }

        decimal? requiredPsuWatts = null;
        if (gpu is not null && psu is not null)
        {
            requiredPsuWatts = EvaluatePsuCapacity(components, gpu, psu, settings, findings);
        }

        if (board is not null && gpu is not null && psu is not null)
        {
            EvaluateConnectors(board, gpu, psu, findings);
        }

        return Complete(findings, requiredPsuWatts);
    }

    private static void EvaluateCpuSocket(
        CompatibilityComponent cpu,
        CompatibilityComponent board,
        List<CompatibilityRuleEvaluation> findings)
    {
        if (!TryOption(cpu, CompatibilityCatalogContract.SemanticKeys.CpuSocket, out var cpuSocket) ||
            !TryOption(board, CompatibilityCatalogContract.SemanticKeys.CpuSocket, out var boardSocket))
        {
            Missing(findings, CompatibilityRuleCodes.CpuSocket, cpu, board);
        }
        else if (cpuSocket != boardSocket)
        {
            Add(findings, CompatibilityRuleCodes.CpuSocket, CompatibilityOverall.Blocked,
                "compatibility.cpu_socket_mismatch", [cpu.SkuPublicId, board.SkuPublicId],
                ("cpuSocket", cpuSocket), ("boardSocket", boardSocket));
        }
    }

    private static void EvaluateChipset(
        CompatibilityComponent cpu,
        CompatibilityComponent board,
        CompatibilityRuleCatalog catalog,
        List<CompatibilityRuleEvaluation> findings)
    {
        if (!TryOption(cpu, CompatibilityCatalogContract.SemanticKeys.CpuGeneration, out var generation) ||
            !TryOption(board, CompatibilityCatalogContract.SemanticKeys.MotherboardChipset, out var chipset))
        {
            Missing(findings, CompatibilityRuleCodes.CpuChipset, cpu, board);
            return;
        }

        if (!catalog.TryGet(chipset, generation, out var mapping))
        {
            Add(findings, CompatibilityRuleCodes.CpuChipset,
                catalog.HasChipset(chipset)
                    ? CompatibilityOverall.Blocked
                    : CompatibilityOverall.InsufficientData,
                catalog.HasChipset(chipset)
                    ? "compatibility.cpu_generation_not_supported"
                    : "compatibility.chipset_mapping_missing",
                [cpu.SkuPublicId, board.SkuPublicId],
                ("chipset", chipset), ("cpuGeneration", generation));
            return;
        }

        if (mapping!.RequiresBiosUpdate)
        {
            Add(findings, CompatibilityRuleCodes.BiosUpdate, CompatibilityOverall.Warning,
                "compatibility.bios_update_may_be_required", [cpu.SkuPublicId, board.SkuPublicId],
                ("chipset", chipset), ("cpuGeneration", generation));
        }
    }

    private static void EvaluateMemory(
        CompatibilityComponent board,
        IReadOnlyCollection<CompatibilityComponent> memories,
        CompatibilityWarningSettings settings,
        List<CompatibilityRuleEvaluation> findings)
    {
        if (!TryOption(board, CompatibilityCatalogContract.SemanticKeys.MemoryType, out var boardType) ||
            memories.Any(memory => !TryOption(
                memory,
                CompatibilityCatalogContract.SemanticKeys.MemoryType,
                out _)))
        {
            Missing(findings, CompatibilityRuleCodes.MemoryType, [board, .. memories]);
        }
        else
        {
            foreach (var memory in memories.Where(memory =>
                         TryOption(memory, CompatibilityCatalogContract.SemanticKeys.MemoryType, out var type) &&
                         type != boardType))
            {
                Add(findings, CompatibilityRuleCodes.MemoryType, CompatibilityOverall.Blocked,
                    "compatibility.memory_type_mismatch", [board.SkuPublicId, memory.SkuPublicId],
                    ("boardMemoryType", boardType));
            }
        }

        if (!TryPositiveWhole(board, CompatibilityCatalogContract.SemanticKeys.MemorySlotCount, out var slots) ||
            memories.Any(memory => !TryPositiveWhole(
                memory,
                CompatibilityCatalogContract.SemanticKeys.MemoryModuleCount,
                out _)))
        {
            Missing(findings, CompatibilityRuleCodes.MemorySlots, [board, .. memories]);
        }
        else
        {
            var used = memories.Sum(memory =>
            {
                TryPositiveWhole(memory, CompatibilityCatalogContract.SemanticKeys.MemoryModuleCount, out var count);
                return count * memory.Quantity;
            });
            if (used > slots)
            {
                Add(findings, CompatibilityRuleCodes.MemorySlots, CompatibilityOverall.Blocked,
                    "compatibility.memory_slots_exceeded", SubjectIds(board, memories),
                    ("availableSlots", slots), ("usedSlots", used));
            }
            else if (slots - used <= settings.RemainingRamSlotWarningCount)
            {
                Add(findings, CompatibilityRuleCodes.MemorySlots, CompatibilityOverall.Warning,
                    "compatibility.memory_slots_low", SubjectIds(board, memories),
                    ("remainingSlots", slots - used));
            }
        }

        if (!TryPositiveDecimal(board, CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb, out var maximum) ||
            memories.Any(memory => !TryPositiveDecimal(
                memory,
                CompatibilityCatalogContract.SemanticKeys.MemoryKitCapacityGb,
                out _)))
        {
            Missing(findings, CompatibilityRuleCodes.MemoryCapacity, [board, .. memories]);
        }
        else
        {
            var total = memories.Sum(memory =>
            {
                TryPositiveDecimal(memory, CompatibilityCatalogContract.SemanticKeys.MemoryKitCapacityGb, out var capacity);
                return capacity * memory.Quantity;
            });
            if (total > maximum)
            {
                Add(findings, CompatibilityRuleCodes.MemoryCapacity, CompatibilityOverall.Blocked,
                    "compatibility.memory_capacity_exceeded", SubjectIds(board, memories),
                    ("maximumGb", maximum), ("selectedGb", total));
            }
        }
    }

    private static void EvaluatePhysical(
        CompatibilityComponent cpu,
        CompatibilityComponent board,
        CompatibilityComponent gpu,
        CompatibilityComponent psu,
        CompatibilityComponent chassis,
        CompatibilityComponent cooler,
        CompatibilityWarningSettings settings,
        List<CompatibilityRuleEvaluation> findings)
    {
        CompareSupportedOption(board, CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor,
            chassis, CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor,
            CompatibilityRuleCodes.MotherboardFormFactor, "compatibility.motherboard_form_factor_unsupported", findings);
        CompareMaximum(gpu, CompatibilityCatalogContract.SemanticKeys.GpuLengthMm,
            chassis, CompatibilityCatalogContract.SemanticKeys.CaseGpuMaxLengthMm,
            settings.GpuClearanceWarningMm, CompatibilityRuleCodes.GpuLength,
            "compatibility.gpu_too_long", "compatibility.gpu_clearance_low", findings);
        CompareSupportedOption(cpu, CompatibilityCatalogContract.SemanticKeys.CpuSocket,
            cooler, CompatibilityCatalogContract.SemanticKeys.CpuSocket,
            CompatibilityRuleCodes.CoolerSocket, "compatibility.cooler_socket_unsupported", findings);
        CompareMaximum(cooler, CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm,
            chassis, CompatibilityCatalogContract.SemanticKeys.CaseCoolerMaxHeightMm,
            settings.CoolerClearanceWarningMm, CompatibilityRuleCodes.CoolerHeight,
            "compatibility.cooler_too_tall", "compatibility.cooler_clearance_low", findings);
        CompareSupportedOption(psu, CompatibilityCatalogContract.SemanticKeys.PsuFormFactor,
            chassis, CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor,
            CompatibilityRuleCodes.PsuFormFactor, "compatibility.psu_form_factor_unsupported", findings);
    }

    private static void EvaluateStorage(
        CompatibilityComponent board,
        IReadOnlyCollection<CompatibilityComponent> storages,
        CompatibilityWarningSettings settings,
        List<CompatibilityRuleEvaluation> findings)
    {
        if (!TryNonNegativeWhole(board, CompatibilityCatalogContract.SemanticKeys.M2SlotCount, out var m2Slots) ||
            !TryNonNegativeWhole(board, CompatibilityCatalogContract.SemanticKeys.SataPortCount, out var sataPorts) ||
            storages.Any(storage => !TryOption(
                storage,
                CompatibilityCatalogContract.SemanticKeys.StorageInterface,
                out _)))
        {
            Missing(findings, CompatibilityRuleCodes.StorageInterface, [board, .. storages]);
            return;
        }

        var usedM2 = storages.Where(IsM2).Sum(storage => storage.Quantity);
        var usedSata = storages.Where(IsSata).Sum(storage => storage.Quantity);
        var unknown = storages.Where(storage => !IsM2(storage) && !IsSata(storage)).ToArray();
        if (unknown.Length > 0 || usedM2 > m2Slots || usedSata > sataPorts)
        {
            Add(findings, CompatibilityRuleCodes.StorageInterface, CompatibilityOverall.Blocked,
                "compatibility.storage_ports_exceeded", SubjectIds(board, storages),
                ("m2Slots", m2Slots), ("m2Used", usedM2),
                ("sataPorts", sataPorts), ("sataUsed", usedSata));
        }
        else if (m2Slots - usedM2 <= settings.RemainingStoragePortWarningCount ||
                 sataPorts - usedSata <= settings.RemainingStoragePortWarningCount)
        {
            Add(findings, CompatibilityRuleCodes.StorageInterface, CompatibilityOverall.Warning,
                "compatibility.storage_ports_low", SubjectIds(board, storages),
                ("remainingM2", m2Slots - usedM2), ("remainingSata", sataPorts - usedSata));
        }

        bool IsM2(CompatibilityComponent storage) =>
            TryOption(storage, CompatibilityCatalogContract.SemanticKeys.StorageInterface, out var value) &&
            value == "M2_NVME";
        bool IsSata(CompatibilityComponent storage) =>
            TryOption(storage, CompatibilityCatalogContract.SemanticKeys.StorageInterface, out var value) &&
            value == "SATA";
    }

    private static decimal? EvaluatePsu(
        IReadOnlyCollection<CompatibilityComponent> components,
        CompatibilityComponent board,
        CompatibilityComponent gpu,
        CompatibilityComponent psu,
        CompatibilityWarningSettings settings,
        List<CompatibilityRuleEvaluation> findings)
    {
        var required = EvaluatePsuCapacity(components, gpu, psu, settings, findings);
        EvaluateConnectors(board, gpu, psu, findings);
        return required;
    }

    private static decimal? EvaluatePsuCapacity(
        IReadOnlyCollection<CompatibilityComponent> components,
        CompatibilityComponent gpu,
        CompatibilityComponent psu,
        CompatibilityWarningSettings settings,
        List<CompatibilityRuleEvaluation> findings)
    {
        var poweredComponents = components.Where(component => component.CategoryCode is not
            CompatibilityCatalogContract.Categories.Psu and not
            CompatibilityCatalogContract.Categories.Case).ToArray();
        if (poweredComponents.Any(component => !TryNonNegativeDecimal(
                component,
                CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts,
                out _)) ||
            !TryPositiveDecimal(psu, CompatibilityCatalogContract.SemanticKeys.PsuRatedWatts, out var rated) ||
            !TryPositiveDecimal(gpu, CompatibilityCatalogContract.SemanticKeys.GpuRecommendedPsuWatts, out var recommended))
        {
            Missing(findings, CompatibilityRuleCodes.PsuCapacity, [psu, gpu, .. poweredComponents]);
            return null;
        }

        var draw = poweredComponents.Sum(component =>
        {
            TryNonNegativeDecimal(component, CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts, out var watts);
            return watts * component.Quantity;
        });
        var target = decimal.Ceiling(draw * 1.30m);
        var tier = PsuTiers.FirstOrDefault(candidate => candidate >= target);
        var required = Math.Max(tier, recommended);
        if (tier == 0m || required > PsuTiers[^1] || rated < required)
        {
            Add(findings, CompatibilityRuleCodes.PsuCapacity, CompatibilityOverall.Blocked,
                "compatibility.psu_capacity_insufficient", SubjectIds(psu, poweredComponents),
                ("estimatedDrawWatts", draw), ("requiredWatts", required), ("ratedWatts", rated));
        }
        else if (draw > 0m && (rated / draw - 1m) * 100m <= settings.PsuReserveWarningPercent)
        {
            Add(findings, CompatibilityRuleCodes.PsuCapacity, CompatibilityOverall.Warning,
                "compatibility.psu_reserve_low", SubjectIds(psu, poweredComponents),
                ("estimatedDrawWatts", draw), ("ratedWatts", rated));
        }
        return required;
    }

    private static void EvaluateConnectors(
        CompatibilityComponent board,
        CompatibilityComponent gpu,
        CompatibilityComponent psu,
        List<CompatibilityRuleEvaluation> findings)
    {
        var keys = new[]
        {
            (Source: board, Required: CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount,
                Available: CompatibilityCatalogContract.SemanticKeys.PsuCpuEps8PinCount),
            (Source: gpu, Required: CompatibilityCatalogContract.SemanticKeys.GpuPcie62PinRequiredCount,
                Available: CompatibilityCatalogContract.SemanticKeys.PsuPcie62PinCount),
            (Source: gpu, Required: CompatibilityCatalogContract.SemanticKeys.Gpu12VhpwrRequiredCount,
                Available: CompatibilityCatalogContract.SemanticKeys.Psu12VhpwrCount),
        };
        if (keys.Any(pair =>
                !TryNonNegativeWhole(pair.Source, pair.Required, out _) ||
                !TryNonNegativeWhole(psu, pair.Available, out _)))
        {
            Missing(findings, CompatibilityRuleCodes.PsuConnectors, board, gpu, psu);
            return;
        }

        var insufficient = keys.Where(pair =>
        {
            TryNonNegativeWhole(pair.Source, pair.Required, out var required);
            TryNonNegativeWhole(psu, pair.Available, out var available);
            return available < required * pair.Source.Quantity;
        }).ToArray();
        if (insufficient.Length > 0)
        {
            Add(findings, CompatibilityRuleCodes.PsuConnectors, CompatibilityOverall.Blocked,
                "compatibility.psu_connectors_insufficient", [board.SkuPublicId, gpu.SkuPublicId, psu.SkuPublicId]);
        }
    }

    private static void CompareSupportedOption(
        CompatibilityComponent selected,
        string selectedKey,
        CompatibilityComponent supportedBy,
        string supportedKey,
        string ruleCode,
        string blockedMessage,
        List<CompatibilityRuleEvaluation> findings)
    {
        if (!TryOption(selected, selectedKey, out var value) ||
            !TryOptions(supportedBy, supportedKey, out var supported))
        {
            Missing(findings, ruleCode, selected, supportedBy);
        }
        else if (!supported.Contains(value))
        {
            Add(findings, ruleCode, CompatibilityOverall.Blocked, blockedMessage,
                [selected.SkuPublicId, supportedBy.SkuPublicId], ("selected", value));
        }
    }

    private static void CompareMaximum(
        CompatibilityComponent selected,
        string selectedKey,
        CompatibilityComponent maximumBy,
        string maximumKey,
        decimal warningClearance,
        string ruleCode,
        string blockedMessage,
        string warningMessage,
        List<CompatibilityRuleEvaluation> findings)
    {
        if (!TryPositiveDecimal(selected, selectedKey, out var value) ||
            !TryPositiveDecimal(maximumBy, maximumKey, out var maximum))
        {
            Missing(findings, ruleCode, selected, maximumBy);
        }
        else if (value > maximum)
        {
            Add(findings, ruleCode, CompatibilityOverall.Blocked, blockedMessage,
                [selected.SkuPublicId, maximumBy.SkuPublicId],
                ("selectedMm", value), ("maximumMm", maximum));
        }
        else if (maximum - value <= warningClearance)
        {
            Add(findings, ruleCode, CompatibilityOverall.Warning, warningMessage,
                [selected.SkuPublicId, maximumBy.SkuPublicId],
                ("clearanceMm", maximum - value));
        }
    }

    private static bool TryOption(CompatibilityComponent component, string key, out string value)
    {
        if (component.Specifications.TryGetValue(key, out var specification) &&
            !string.IsNullOrWhiteSpace(specification.OptionCode))
        {
            value = specification.OptionCode;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryOptions(
        CompatibilityComponent component,
        string key,
        out IReadOnlySet<string> values)
    {
        if (component.Specifications.TryGetValue(key, out var specification) &&
            specification.OptionCodes is { Count: > 0 })
        {
            values = specification.OptionCodes;
            return true;
        }

        values = new HashSet<string>();
        return false;
    }

    private static bool TryPositiveDecimal(
        CompatibilityComponent component,
        string key,
        out decimal value) => TryDecimal(component, key, candidate => candidate > 0m, out value);

    private static bool TryNonNegativeDecimal(
        CompatibilityComponent component,
        string key,
        out decimal value) => TryDecimal(component, key, candidate => candidate >= 0m, out value);

    private static bool TryPositiveWhole(
        CompatibilityComponent component,
        string key,
        out int value) => TryWhole(component, key, candidate => candidate > 0m, out value);

    private static bool TryNonNegativeWhole(
        CompatibilityComponent component,
        string key,
        out int value) => TryWhole(component, key, candidate => candidate >= 0m, out value);

    private static bool TryDecimal(
        CompatibilityComponent component,
        string key,
        Func<decimal, bool> predicate,
        out decimal value)
    {
        if (component.Specifications.TryGetValue(key, out var specification) &&
            specification.DecimalValue is { } candidate && predicate(candidate))
        {
            value = candidate;
            return true;
        }

        value = 0m;
        return false;
    }

    private static bool TryWhole(
        CompatibilityComponent component,
        string key,
        Func<decimal, bool> predicate,
        out int value)
    {
        if (TryDecimal(component, key, predicate, out var candidate) &&
            candidate == decimal.Truncate(candidate) && candidate <= int.MaxValue)
        {
            value = decimal.ToInt32(candidate);
            return true;
        }

        value = 0;
        return false;
    }

    private static void Missing(
        List<CompatibilityRuleEvaluation> findings,
        string ruleCode,
        params CompatibilityComponent[] components) =>
        Missing(findings, ruleCode, (IEnumerable<CompatibilityComponent>)components);

    private static void Missing(
        List<CompatibilityRuleEvaluation> findings,
        string ruleCode,
        IEnumerable<CompatibilityComponent> components) =>
        Add(findings, ruleCode, CompatibilityOverall.InsufficientData,
            "compatibility.required_data_missing",
            components.Select(component => component.SkuPublicId).Distinct().ToArray());

    private static IReadOnlyList<Guid> SubjectIds(
        CompatibilityComponent component,
        IEnumerable<CompatibilityComponent> others) =>
        new[] { component.SkuPublicId }.Concat(others.Select(other => other.SkuPublicId))
            .Distinct()
            .ToArray();

    private static void Add(
        List<CompatibilityRuleEvaluation> findings,
        string ruleCode,
        CompatibilityOverall severity,
        string messageKey,
        IReadOnlyList<Guid> subjects,
        params (string Key, object Value)[] facts) =>
        findings.Add(new CompatibilityRuleEvaluation(
            ruleCode,
            severity,
            messageKey,
            subjects,
            facts.ToDictionary(
                pair => pair.Key,
                pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal)));

    private static CompatibilityEvaluation Complete(
        IReadOnlyList<CompatibilityRuleEvaluation> findings,
        decimal? requiredPsuWatts)
    {
        var overall = findings.Any(finding => finding.Severity == CompatibilityOverall.Blocked)
            ? CompatibilityOverall.Blocked
            : findings.Any(finding => finding.Severity == CompatibilityOverall.InsufficientData)
                ? CompatibilityOverall.InsufficientData
                : findings.Any(finding => finding.Severity == CompatibilityOverall.Warning)
                    ? CompatibilityOverall.Warning
                    : CompatibilityOverall.Compatible;
        return new CompatibilityEvaluation(overall, findings, requiredPsuWatts);
    }
}
