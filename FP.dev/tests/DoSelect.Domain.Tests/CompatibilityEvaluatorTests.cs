using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;

namespace DoSelect.Domain.Tests;

public sealed class CompatibilityEvaluatorTests
{
    private static readonly Guid CpuId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid BoardId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void Evaluate_WithCompleteCompatibleBuild_ReturnsCompatible()
    {
        var result = Evaluate(CompatibleComponents());

        Assert.Equal(CompatibilityOverall.Compatible, result.Overall);
        Assert.Empty(result.Results);
        Assert.Equal(750m, result.RequiredPsuWatts);
    }

    [Fact]
    public void EvaluatePartial_WithCompatibleCpuAndMotherboard_DoesNotRequireCompleteBuild()
    {
        var components = CompatibleComponents()
            .Where(component => component.CategoryCode is
                CompatibilityCatalogContract.Categories.Cpu or
                CompatibilityCatalogContract.Categories.Motherboard)
            .ToArray();

        var result = CompatibilityEvaluator.EvaluatePartial(components, Settings(), Catalog());

        Assert.Equal(CompatibilityOverall.Compatible, result.Overall);
        Assert.DoesNotContain(
            result.Results,
            item => item.RuleCode == CompatibilityRuleCodes.RequiredComponent);
    }

    [Fact]
    public void EvaluatePartial_WithMismatchedCpuAndMotherboard_ReturnsBlocked()
    {
        var components = CompatibleComponents()
            .Where(component => component.CategoryCode is
                CompatibilityCatalogContract.Categories.Cpu or
                CompatibilityCatalogContract.Categories.Motherboard)
            .Select(component => component.CategoryCode == CompatibilityCatalogContract.Categories.Cpu
                ? component with
                {
                    Specifications = SetOption(component.Specifications, "CPU_SOCKET", "LGA1700"),
                }
                : component)
            .ToArray();

        var result = CompatibilityEvaluator.EvaluatePartial(components, Settings(), Catalog());

        Assert.Equal(CompatibilityOverall.Blocked, result.Overall);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.CpuSocket);
    }

    [Fact]
    public void Evaluate_SocketAndUnsupportedGeneration_ReturnsBothBlockedRules()
    {
        var components = Replace(
            CompatibleComponents(),
            CompatibilityCatalogContract.Categories.Cpu,
            component => component with
            {
                Specifications = SetOption(component.Specifications, "CPU_SOCKET", "LGA1700"),
            });
        components = Replace(
            components,
            CompatibilityCatalogContract.Categories.Cpu,
            component => component with
            {
                Specifications = SetOption(component.Specifications, "CPU_GENERATION", "RYZEN_9000"),
            });

        var result = Evaluate(components);

        Assert.Equal(CompatibilityOverall.Blocked, result.Overall);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.CpuSocket);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.CpuChipset);
    }

    [Fact]
    public void Evaluate_BiosMappedCombination_ReturnsWarning()
    {
        var result = CompatibilityEvaluator.Evaluate(
            CompatibleComponents(),
            Settings(),
            new CompatibilityRuleCatalog(
            [
                new CpuChipsetCompatibility("B650", "RYZEN_7000", RequiresBiosUpdate: true),
            ]));

        Assert.Equal(CompatibilityOverall.Warning, result.Overall);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.BiosUpdate);
    }

    [Fact]
    public void Evaluate_MemoryViolations_ReturnsTypeSlotAndCapacityBlocks()
    {
        var components = Replace(
            CompatibleComponents(),
            CompatibilityCatalogContract.Categories.Memory,
            component => component with
            {
                Quantity = 5,
                Specifications = SetOption(component.Specifications, "MEMORY_TYPE", "DDR4"),
            });

        var result = Evaluate(components);

        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.MemoryType);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.MemorySlots);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.MemoryCapacity);
    }

    [Fact]
    public void Evaluate_ChassisAndCoolerViolations_ReturnsFourPhysicalBlocks()
    {
        var components = Replace(
            CompatibleComponents(),
            CompatibilityCatalogContract.Categories.Case,
            component => component with
            {
                Specifications = SetOptions(
                    SetOptions(
                        SetDecimal(
                            SetDecimal(component.Specifications, "CASE_GPU_MAX_LENGTH_MM", 250m),
                            "CASE_COOLER_MAX_HEIGHT_MM",
                            140m),
                        "CASE_SUPPORTED_MOTHERBOARD_FORM_FACTOR",
                        ["ITX"]),
                    "CASE_SUPPORTED_PSU_FORM_FACTOR",
                    ["SFX"]),
            });
        components = Replace(
            components,
            CompatibilityCatalogContract.Categories.CpuCooler,
            component => component with
            {
                Specifications = SetOptions(component.Specifications, "CPU_SOCKET", ["AM4"]),
            });

        var result = Evaluate(components);

        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.MotherboardFormFactor);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.GpuLength);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.CoolerSocket);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.CoolerHeight);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.PsuFormFactor);
    }

    [Fact]
    public void Evaluate_StoragePsuAndConnectorViolations_ReturnsBlockedRules()
    {
        var components = Replace(
            CompatibleComponents(),
            CompatibilityCatalogContract.Categories.Storage,
            component => component with { Quantity = 3 });
        components = Replace(
            components,
            CompatibilityCatalogContract.Categories.Psu,
            component => component with
            {
                Specifications = SetDecimal(
                    SetDecimal(component.Specifications, "PSU_RATED_WATTS", 650m),
                    "PSU_12VHPWR_COUNT",
                    0m),
            });

        var result = Evaluate(components);

        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.StorageInterface);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.PsuCapacity);
        Assert.Contains(result.Results, item => item.RuleCode == CompatibilityRuleCodes.PsuConnectors);
    }

    [Fact]
    public void Evaluate_WhenCoreSpecificationIsMissing_ReturnsInsufficientData()
    {
        var components = Replace(
            CompatibleComponents(),
            CompatibilityCatalogContract.Categories.Cpu,
            component => component with
            {
                Specifications = Remove(component.Specifications, "POWER_DRAW_WATTS"),
            });

        var result = Evaluate(components);

        Assert.Equal(CompatibilityOverall.InsufficientData, result.Overall);
        Assert.Contains(result.Results, item => item.Severity == CompatibilityOverall.InsufficientData);
    }

    [Theory]
    [InlineData("B450", "RYZEN_5000", true)]
    [InlineData("B650", "RYZEN_7000", false)]
    [InlineData("B650", "RYZEN_9000", true)]
    [InlineData("Z690", "INTEL_CORE_14", true)]
    [InlineData("Z790", "INTEL_CORE_13", false)]
    [InlineData("B860", "INTEL_CORE_ULTRA_200", false)]
    public void Version1Catalog_ContainsApprovedMappings(
        string chipset,
        string generation,
        bool requiresBiosUpdate)
    {
        var catalog = CompatibilityRuleCatalog.CreateVersion1();

        Assert.True(catalog.TryGet(chipset, generation, out var mapping));
        Assert.NotNull(mapping);
        Assert.Equal(requiresBiosUpdate, mapping.RequiresBiosUpdate);
    }

    [Fact]
    public void Version1Catalog_DistinguishesKnownUnsupportedFromUnknownChipset()
    {
        var catalog = CompatibilityRuleCatalog.CreateVersion1();

        Assert.True(catalog.HasChipset("B650"));
        Assert.False(catalog.TryGet("B650", "RYZEN_5000", out _));
        Assert.False(catalog.HasChipset("UNKNOWN"));
    }

    private static CompatibilityEvaluation Evaluate(
        IReadOnlyList<CompatibilityComponent> components) =>
        CompatibilityEvaluator.Evaluate(
            components,
            Settings(),
            new CompatibilityRuleCatalog(
            [
                new CpuChipsetCompatibility("B650", "RYZEN_7000", RequiresBiosUpdate: false),
            ]));

    private static CompatibilityWarningSettings Settings() => new(20m, 10m, 35m, 0, 0);

    private static CompatibilityRuleCatalog Catalog() => new(
    [
        new CpuChipsetCompatibility("B650", "RYZEN_7000", RequiresBiosUpdate: false),
    ]);

    private static IReadOnlyList<CompatibilityComponent> CompatibleComponents() =>
    [
        Component(CpuId, "CPU", Specs(
            Option("CPU_SOCKET", "AM5"), Option("CPU_GENERATION", "RYZEN_7000"),
            Decimal("POWER_DRAW_WATTS", 120m))),
        Component(BoardId, "MOTHERBOARD", Specs(
            Option("CPU_SOCKET", "AM5"), Option("MOTHERBOARD_CHIPSET", "B650"),
            Option("MEMORY_TYPE", "DDR5"), Decimal("MEMORY_SLOT_COUNT", 4m),
            Decimal("MEMORY_MAX_CAPACITY_GB", 128m), Option("MOTHERBOARD_FORM_FACTOR", "ATX"),
            Decimal("M2_SLOT_COUNT", 2m), Decimal("SATA_PORT_COUNT", 4m),
            Decimal("MOTHERBOARD_CPU_EPS_8PIN_REQUIRED_COUNT", 1m), Decimal("POWER_DRAW_WATTS", 50m))),
        Component(Guid.NewGuid(), "MEMORY", Specs(
            Option("MEMORY_TYPE", "DDR5"), Decimal("MEMORY_MODULE_COUNT", 2m),
            Decimal("MEMORY_KIT_CAPACITY_GB", 32m), Decimal("POWER_DRAW_WATTS", 10m))),
        Component(Guid.NewGuid(), "GPU", Specs(
            Decimal("GPU_LENGTH_MM", 300m), Decimal("GPU_RECOMMENDED_PSU_WATTS", 750m),
            Decimal("GPU_PCIE_6_2PIN_REQUIRED_COUNT", 0m), Decimal("GPU_12VHPWR_REQUIRED_COUNT", 1m),
            Decimal("POWER_DRAW_WATTS", 320m))),
        Component(Guid.NewGuid(), "STORAGE", Specs(
            Option("STORAGE_INTERFACE", "M2_NVME"), Decimal("POWER_DRAW_WATTS", 8m))),
        Component(Guid.NewGuid(), "PSU", Specs(
            Decimal("PSU_RATED_WATTS", 850m), Decimal("PSU_PCIE_6_2PIN_COUNT", 4m),
            Decimal("PSU_12VHPWR_COUNT", 1m), Decimal("PSU_CPU_EPS_8PIN_COUNT", 2m),
            Option("PSU_FORM_FACTOR", "ATX"))),
        Component(Guid.NewGuid(), "CASE", Specs(
            Options("CASE_SUPPORTED_MOTHERBOARD_FORM_FACTOR", "ATX", "MATX", "ITX"),
            Decimal("CASE_GPU_MAX_LENGTH_MM", 350m), Decimal("CASE_COOLER_MAX_HEIGHT_MM", 170m),
            Options("CASE_SUPPORTED_PSU_FORM_FACTOR", "ATX", "SFX"))),
        Component(Guid.NewGuid(), "CPU_COOLER", Specs(
            Options("CPU_SOCKET", "AM4", "AM5"), Decimal("COOLER_HEIGHT_MM", 150m),
            Decimal("POWER_DRAW_WATTS", 5m))),
    ];

    private static CompatibilityComponent Component(
        Guid skuPublicId,
        string category,
        IReadOnlyDictionary<string, CompatibilitySpecification> specifications,
        int quantity = 1) => new(skuPublicId, category, quantity, specifications);

    private static KeyValuePair<string, CompatibilitySpecification> Decimal(string key, decimal value) =>
        new(key, CompatibilitySpecification.FromDecimal(value));

    private static KeyValuePair<string, CompatibilitySpecification> Option(string key, string value) =>
        new(key, CompatibilitySpecification.FromOption(value));

    private static KeyValuePair<string, CompatibilitySpecification> Options(string key, params string[] values) =>
        new(key, CompatibilitySpecification.FromOptions(values));

    private static IReadOnlyDictionary<string, CompatibilitySpecification> Specs(
        params KeyValuePair<string, CompatibilitySpecification>[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyList<CompatibilityComponent> Replace(
        IReadOnlyList<CompatibilityComponent> components,
        string category,
        Func<CompatibilityComponent, CompatibilityComponent> replace) =>
        components.Select(component => component.CategoryCode == category ? replace(component) : component)
            .ToArray();

    private static IReadOnlyDictionary<string, CompatibilitySpecification> SetDecimal(
        IReadOnlyDictionary<string, CompatibilitySpecification> source,
        string key,
        decimal value) => Set(source, key, CompatibilitySpecification.FromDecimal(value));

    private static IReadOnlyDictionary<string, CompatibilitySpecification> SetOption(
        IReadOnlyDictionary<string, CompatibilitySpecification> source,
        string key,
        string value) => Set(source, key, CompatibilitySpecification.FromOption(value));

    private static IReadOnlyDictionary<string, CompatibilitySpecification> SetOptions(
        IReadOnlyDictionary<string, CompatibilitySpecification> source,
        string key,
        IReadOnlyCollection<string> values) => Set(source, key, CompatibilitySpecification.FromOptions(values));

    private static IReadOnlyDictionary<string, CompatibilitySpecification> Set(
        IReadOnlyDictionary<string, CompatibilitySpecification> source,
        string key,
        CompatibilitySpecification value)
    {
        var copy = source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        copy[key] = value;
        return copy;
    }

    private static IReadOnlyDictionary<string, CompatibilitySpecification> Remove(
        IReadOnlyDictionary<string, CompatibilitySpecification> source,
        string key) => source.Where(pair => pair.Key != key)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
