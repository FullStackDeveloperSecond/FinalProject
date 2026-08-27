using DoSelect.Domain.Builds;

namespace DoSelect.Domain.Tests;

public sealed class CompatibilityRuleEngineTests
{
    private static readonly Guid CpuId = Guid.NewGuid();
    private static readonly Guid BoardId = Guid.NewGuid();
    private static readonly Guid MemoryId = Guid.NewGuid();
    private static readonly Guid GpuId = Guid.NewGuid();
    private static readonly Guid StorageId = Guid.NewGuid();
    private static readonly Guid PsuId = Guid.NewGuid();
    private static readonly Guid CaseId = Guid.NewGuid();
    private static readonly Guid CoolerId = Guid.NewGuid();

    /// <summary>A fully compatible, generously-specced build used as the baseline for every
    /// single-field mutation test below.</summary>
    private static BuildComponentSet CompatibleSet() => new(
        Cpu: new CpuFacts(CpuId, "AM5", "Ryzen7000", 105m),
        Motherboard: new MotherboardFacts(
            BoardId, "AM5", "X670E", "DDR5", MemorySlotCount: 4, MaxMemoryCapacityGb: 128m,
            FormFactor: "ATX", SupportedStorageInterfaces: new Dictionary<string, int> { ["M2_NVME"] = 2, ["SATA"] = 4 }),
        Memory:
        [
            new MemoryModuleFacts(MemoryId, "DDR5", CapacityGbPerModule: 16m, Quantity: 2),
        ],
        Gpu: new GraphicsCardFacts(
            GpuId, LengthMm: 300m, RecommendedPsuWatts: 750m, PowerWatts: 300m,
            RequiredConnectors: ["pcie_8pin", "pcie_8pin"]),
        Storage: [new StorageDeviceFacts(StorageId, "M2_NVME", PowerWatts: 8m)],
        Psu: new PowerSupplyFacts(PsuId, WattageW: 850m, AvailableConnectors: ["pcie_8pin", "pcie_8pin", "24pin"]),
        Case: new CaseFacts(CaseId, SupportedFormFactors: ["ATX", "MicroATX"], MaxGpuLengthMm: 360m, MaxCoolerHeightMm: 180m),
        Cooler: new CoolerFacts(CoolerId, HeightMm: 160m, SupportedSockets: ["AM5", "AM4"], PowerWatts: 5m));

    [Fact]
    public void Evaluate_ReturnsCompatible_WhenEveryRulePasses()
    {
        var evaluation = CompatibilityRuleEngine.Evaluate(CompatibleSet());

        Assert.Equal(CompatibilityOverall.Compatible, evaluation.Overall);
        Assert.Empty(evaluation.Findings);
    }

    [Fact]
    public void Evaluate_SkipsRules_WhenBothRelevantComponentsAreAbsent() =>
        Assert.Equal(CompatibilityOverall.Compatible, CompatibilityRuleEngine.Evaluate(BuildComponentSet.Empty).Overall);

    [Fact]
    public void CpuSocket_Blocks_OnMismatch()
    {
        var set = CompatibleSet() with { Motherboard = CompatibleSet().Motherboard! with { Socket = "LGA1700" } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Equal(CompatibilityOverall.Blocked, evaluation.Overall);
        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.CpuSocket);
    }

    [Fact]
    public void CpuSocket_IsInsufficientData_WhenSocketMissing()
    {
        var set = CompatibleSet() with { Cpu = CompatibleSet().Cpu! with { Socket = null } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Equal(CompatibilityOverall.InsufficientData, evaluation.Overall);
    }

    [Fact]
    public void ChipsetCpuGeneration_Blocks_WhenCombinationUnknown()
    {
        var set = CompatibleSet() with { Motherboard = CompatibleSet().Motherboard! with { Chipset = "A620" }, Cpu = CompatibleSet().Cpu! with { Generation = "Ryzen1000" } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.ChipsetCpuGeneration);
        Assert.Equal(CompatibilitySeverityTokens.Blocked, finding.Severity);
    }

    [Fact]
    public void ChipsetCpuGeneration_Warns_WhenBiosUpdateIsKnownToBeRequired()
    {
        var set = CompatibleSet() with { Motherboard = CompatibleSet().Motherboard! with { Chipset = "X670E" }, Cpu = CompatibleSet().Cpu! with { Generation = "Ryzen9000" } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.ChipsetCpuGeneration);
        Assert.Equal(CompatibilitySeverityTokens.Warning, finding.Severity);
        Assert.Equal(CompatibilityOverall.Warning, evaluation.Overall);
    }

    [Fact]
    public void RamGeneration_Blocks_OnDdrMismatch()
    {
        var set = CompatibleSet() with
        {
            Memory = [new MemoryModuleFacts(MemoryId, "DDR4", CapacityGbPerModule: 16m, Quantity: 2)],
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.RamGeneration && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void RamSlotCount_Blocks_WhenModuleCountExceedsSlots()
    {
        var set = CompatibleSet() with
        {
            Memory = [new MemoryModuleFacts(MemoryId, "DDR5", CapacityGbPerModule: 16m, Quantity: 6)],
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.RamSlotCount && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void RamSlotCount_Warns_WhenRemainingSlotsAtOrBelowThreshold()
    {
        var set = CompatibleSet() with
        {
            Motherboard = CompatibleSet().Motherboard! with { MemorySlotCount = 2 },
            Memory = [new MemoryModuleFacts(MemoryId, "DDR5", CapacityGbPerModule: 16m, Quantity: 2)],
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set, new BuildCompatibilityWarningSettings(RemainingRamSlotWarningCount: 0m));

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.RamSlotCount && f.Severity == CompatibilitySeverityTokens.Warning);
    }

    [Fact]
    public void RamCapacity_Blocks_WhenTotalExceedsBoardMaximum()
    {
        var set = CompatibleSet() with
        {
            Motherboard = CompatibleSet().Motherboard! with { MaxMemoryCapacityGb = 32m },
            Memory = [new MemoryModuleFacts(MemoryId, "DDR5", CapacityGbPerModule: 32m, Quantity: 2)],
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.RamCapacity && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void CaseFormFactor_Blocks_WhenCaseDoesNotSupportBoardFormFactor()
    {
        var set = CompatibleSet() with { Motherboard = CompatibleSet().Motherboard! with { FormFactor = "EATX" } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.CaseFormFactor && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void GpuLength_Blocks_WhenLongerThanCaseLimit()
    {
        var set = CompatibleSet() with { Gpu = CompatibleSet().Gpu! with { LengthMm = 400m } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.GpuLength && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void GpuLength_Warns_WhenClearanceAtOrBelowThreshold()
    {
        var set = CompatibleSet() with { Gpu = CompatibleSet().Gpu! with { LengthMm = 345m } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set, new BuildCompatibilityWarningSettings(GpuClearanceWarningMm: 20m));

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.GpuLength && f.Severity == CompatibilitySeverityTokens.Warning);
    }

    [Fact]
    public void CoolerSocket_Blocks_WhenCpuSocketNotSupported()
    {
        var set = CompatibleSet() with { Cooler = CompatibleSet().Cooler! with { SupportedSockets = ["LGA1700"] } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.CoolerSocket && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void CoolerHeight_Blocks_WhenTallerThanCaseLimit()
    {
        var set = CompatibleSet() with { Cooler = CompatibleSet().Cooler! with { HeightMm = 200m } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.CoolerHeight && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void StorageInterface_Blocks_WhenBoardDoesNotSupportInterface()
    {
        var set = CompatibleSet() with { Storage = [new StorageDeviceFacts(StorageId, "PCIE_ADD_IN_CARD", PowerWatts: 8m)] };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.StorageInterface && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void StorageInterface_Blocks_WhenDeviceCountExceedsThatInterfacesActualPortCount()
    {
        // Board supports SATA (4 physical ports per CompatibleSet's baseline) — 8 SATA devices
        // must be blocked by the real port count, not silently allowed because "SATA" is a
        // supported interface *kind* (組長 PR #34 review — the bug this regression test targets).
        var set = CompatibleSet() with { Storage = [new StorageDeviceFacts(StorageId, "SATA", PowerWatts: 8m, Quantity: 8)] };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.StorageInterface && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void StorageInterface_DoesNotFindAnything_WhenDifferentInterfacesEachStayWithinTheirOwnPortCountWithHeadroom()
    {
        // 1 M2_NVME device (of 2 ports) and 3 SATA devices (of 4 ports) — combined device count
        // (4) exceeds the board's *interface-kind* count (2), but each interface's own pool has
        // headroom, so this must produce no finding at all.
        var set = CompatibleSet() with
        {
            Storage =
            [
                new StorageDeviceFacts(StorageId, "M2_NVME", PowerWatts: 8m, Quantity: 1),
                new StorageDeviceFacts(Guid.NewGuid(), "SATA", PowerWatts: 3m, Quantity: 3),
            ],
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.DoesNotContain(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.StorageInterface);
    }

    [Fact]
    public void PsuCapacity_Blocks_WhenBelowRequiredMinimum()
    {
        var set = CompatibleSet() with { Psu = CompatibleSet().Psu! with { WattageW = 450m } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.PsuCapacity);
        Assert.Equal(CompatibilitySeverityTokens.Blocked, finding.Severity);
    }

    [Fact]
    public void PsuCapacity_IsInsufficientData_WhenPsuWattageIsUnknown()
    {
        // Previously fell through to the powerContributors.Count == 0 branch's "return null" (no
        // finding at all — silently read as compatible) whenever the PSU's own rated wattage was
        // missing, regardless of whether every other power fact was known (組長 PR #34 review).
        var set = CompatibleSet() with { Psu = CompatibleSet().Psu! with { WattageW = null } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.PsuCapacity);
        Assert.Equal(CompatibilitySeverityTokens.InsufficientData, finding.Severity);
    }

    [Fact]
    public void PsuCapacity_IsInsufficientData_WhenGpuRecommendedWattageIsUnknown()
    {
        // Previously treated a missing GpuFacts.RecommendedPsuWatts as 0 via `?? 0m`, so an
        // underpowered PSU could pass silently whenever only this one fact was missing (組長 PR #34
        // review). A 450W PSU is enough for the structured estimate here (105+300+8 = 413W * 1.3 ≈
        // 537W → tier 550W... use a PSU that only clears the tier, not a real GPU recommendation).
        var set = CompatibleSet() with
        {
            Gpu = CompatibleSet().Gpu! with { RecommendedPsuWatts = null },
            Psu = CompatibleSet().Psu! with { WattageW = 550m },
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.PsuCapacity);
        Assert.Equal(CompatibilitySeverityTokens.InsufficientData, finding.Severity);
    }

    [Fact]
    public void PsuCapacity_Blocks_WhenEstimateExceedsTheTopTier()
    {
        var set = CompatibleSet() with
        {
            Cpu = CompatibleSet().Cpu! with { PowerWatts = 800m },
            Gpu = CompatibleSet().Gpu! with { PowerWatts = 800m, RecommendedPsuWatts = 1000m },
            Psu = CompatibleSet().Psu! with { WattageW = 1500m },
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.PsuCapacity);
        Assert.Equal(CompatibilitySeverityTokens.Blocked, finding.Severity);
        Assert.Equal("compatibility.psu_no_suitable_tier", finding.MessageKey);
    }

    [Fact]
    public void PsuCapacity_Warns_WhenReservePercentBelowThreshold()
    {
        // Estimate = 105 + 300 + 8 + 5 = 418W; required-with-margin = 543.4 -> tier 550W.
        // GPU-recommended watts (500W) stays below that tier so it doesn't dominate the
        // required minimum. A 560W PSU clears the hard 550W floor but its actual reserve over
        // the raw 418W estimate is only ~34%, just under the default 35% warning threshold.
        var set = CompatibleSet() with
        {
            Gpu = CompatibleSet().Gpu! with { RecommendedPsuWatts = 500m },
            Psu = CompatibleSet().Psu! with { WattageW = 560m },
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.PsuCapacity);
        Assert.Equal(CompatibilitySeverityTokens.Warning, finding.Severity);
    }

    [Fact]
    public void PsuConnectors_Blocks_WhenGpuConnectorsUnavailable()
    {
        var set = CompatibleSet() with { Psu = CompatibleSet().Psu! with { AvailableConnectors = ["24pin"] } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.PsuConnectors && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void PsuConnectors_Blocks_OnCount_WhenPsuHasFewerOfAConnectorThanRequired()
    {
        // GPU needs two 8-pin connectors; PSU only lists one. A naive set-difference would miss
        // this (the value "pcie_8pin" is present), so this guards the multiset comparison.
        var set = CompatibleSet() with { Psu = CompatibleSet().Psu! with { AvailableConnectors = ["pcie_8pin", "24pin"] } };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Contains(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.PsuConnectors && f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void Evaluate_OverallPrefersBlocked_OverInsufficientDataAndWarning()
    {
        // CPU_SOCKET has no socket to compare -> InsufficientData; RAM_GENERATION has a real
        // DDR4/DDR5 mismatch -> Blocked. Overall must roll up to Blocked, not InsufficientData.
        var set = CompatibleSet() with
        {
            Cpu = CompatibleSet().Cpu! with { Socket = null },
            Memory = [new MemoryModuleFacts(MemoryId, "DDR4", CapacityGbPerModule: 16m, Quantity: 2)],
        };

        var evaluation = CompatibilityRuleEngine.Evaluate(set);

        Assert.Equal(CompatibilityOverall.Blocked, evaluation.Overall);
        Assert.Contains(evaluation.Findings, f => f.Severity == CompatibilitySeverityTokens.InsufficientData);
        Assert.Contains(evaluation.Findings, f => f.Severity == CompatibilitySeverityTokens.Blocked);
    }

    [Fact]
    public void Evaluate_DisabledRuleCodes_ReportRuleDisabled_InsteadOfTheRealViolation()
    {
        // 相容性規則後台設計.md 驗收重點: "停用規則後結果顯示 RuleDisabled，不得回 Compatible" — a
        // disabled rule that would have fired must still show up (as RuleDisabled), not vanish.
        var set = CompatibleSet() with { Motherboard = CompatibleSet().Motherboard! with { Socket = "LGA1700" } };

        var evaluation = CompatibilityRuleEngine.Evaluate(
            set,
            disabledRuleCodes: new HashSet<string> { BuildCompatibilityRuleCodes.CpuSocket });

        var finding = Assert.Single(evaluation.Findings, f => f.RuleCode == BuildCompatibilityRuleCodes.CpuSocket);
        Assert.Equal(CompatibilitySeverityTokens.RuleDisabled, finding.Severity);
        // RuleDisabled never wins the overall rollup — Overall only ever takes its 4 real values.
        Assert.Equal(CompatibilityOverall.Compatible, evaluation.Overall);
    }

    [Fact]
    public void Evaluate_DisabledRuleCodes_ProduceNoFinding_WhenTheRuleWouldNotHaveApplied()
    {
        // The disabled rule's own applicability precondition (both Cpu and Motherboard present)
        // isn't met here, so it must stay silent rather than manufacture a RuleDisabled entry
        // for a rule that was never relevant to this build in the first place.
        var evaluation = CompatibilityRuleEngine.Evaluate(
            BuildComponentSet.Empty,
            disabledRuleCodes: new HashSet<string> { BuildCompatibilityRuleCodes.CpuSocket });

        Assert.Empty(evaluation.Findings);
    }

    [Fact]
    public void Evaluate_OnlyRuleCodes_RestrictsWhichRulesRun()
    {
        var set = CompatibleSet() with { Motherboard = CompatibleSet().Motherboard! with { Socket = "LGA1700", FormFactor = "EATX" } };

        var evaluation = CompatibilityRuleEngine.Evaluate(
            set,
            onlyRuleCodes: new HashSet<string> { BuildCompatibilityRuleCodes.CpuSocket });

        var finding = Assert.Single(evaluation.Findings);
        Assert.Equal(BuildCompatibilityRuleCodes.CpuSocket, finding.RuleCode);
    }
}
