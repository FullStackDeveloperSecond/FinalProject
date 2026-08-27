namespace DoSelect.Domain.Catalog;

/// <summary>
/// Stable category and protected specification keys consumed by the fixed compatibility engine.
/// Display labels remain editable, but these codes are immutable data contracts.
/// </summary>
public static class CompatibilityCatalogContract
{
    public static class Categories
    {
        public const string Cpu = "CPU";
        public const string Motherboard = "MOTHERBOARD";
        public const string Memory = "MEMORY";
        public const string Gpu = "GPU";
        public const string Storage = "STORAGE";
        public const string Psu = "PSU";
        public const string Case = "CASE";
        public const string CpuCooler = "CPU_COOLER";
    }

    public static class SemanticKeys
    {
        public const string CpuSocket = "CPU_SOCKET";
        public const string CpuGeneration = "CPU_GENERATION";
        public const string PowerDrawWatts = "POWER_DRAW_WATTS";
        public const string MotherboardChipset = "MOTHERBOARD_CHIPSET";
        public const string MemoryType = "MEMORY_TYPE";
        public const string MemorySlotCount = "MEMORY_SLOT_COUNT";
        public const string MemoryMaxCapacityGb = "MEMORY_MAX_CAPACITY_GB";
        public const string MotherboardFormFactor = "MOTHERBOARD_FORM_FACTOR";
        public const string M2SlotCount = "M2_SLOT_COUNT";
        public const string SataPortCount = "SATA_PORT_COUNT";
        public const string MotherboardCpuEps8PinRequiredCount =
            "MOTHERBOARD_CPU_EPS_8PIN_REQUIRED_COUNT";
        public const string MemoryModuleCount = "MEMORY_MODULE_COUNT";
        public const string MemoryKitCapacityGb = "MEMORY_KIT_CAPACITY_GB";
        public const string GpuLengthMm = "GPU_LENGTH_MM";
        public const string GpuRecommendedPsuWatts = "GPU_RECOMMENDED_PSU_WATTS";
        public const string GpuPcie62PinRequiredCount = "GPU_PCIE_6_2PIN_REQUIRED_COUNT";
        public const string Gpu12VhpwrRequiredCount = "GPU_12VHPWR_REQUIRED_COUNT";
        public const string StorageInterface = "STORAGE_INTERFACE";
        public const string PsuRatedWatts = "PSU_RATED_WATTS";
        public const string PsuPcie62PinCount = "PSU_PCIE_6_2PIN_COUNT";
        public const string Psu12VhpwrCount = "PSU_12VHPWR_COUNT";
        public const string PsuCpuEps8PinCount = "PSU_CPU_EPS_8PIN_COUNT";
        public const string PsuFormFactor = "PSU_FORM_FACTOR";
        public const string CaseSupportedMotherboardFormFactor =
            "CASE_SUPPORTED_MOTHERBOARD_FORM_FACTOR";
        public const string CaseGpuMaxLengthMm = "CASE_GPU_MAX_LENGTH_MM";
        public const string CaseCoolerMaxHeightMm = "CASE_COOLER_MAX_HEIGHT_MM";
        public const string CaseSupportedPsuFormFactor = "CASE_SUPPORTED_PSU_FORM_FACTOR";
        public const string CoolerHeightMm = "COOLER_HEIGHT_MM";
    }

    public static IReadOnlyList<string> MultiValueSemanticKeys { get; } =
    [
        SemanticKeys.CpuSocket,
        SemanticKeys.CaseSupportedMotherboardFormFactor,
        SemanticKeys.CaseSupportedPsuFormFactor,
    ];

    public static IReadOnlySet<string> HardRuleSemanticKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            SemanticKeys.CpuSocket,
            SemanticKeys.CpuGeneration,
            SemanticKeys.PowerDrawWatts,
            SemanticKeys.MotherboardChipset,
            SemanticKeys.MemoryType,
            SemanticKeys.MemorySlotCount,
            SemanticKeys.MemoryMaxCapacityGb,
            SemanticKeys.MotherboardFormFactor,
            SemanticKeys.M2SlotCount,
            SemanticKeys.SataPortCount,
            SemanticKeys.MotherboardCpuEps8PinRequiredCount,
            SemanticKeys.MemoryModuleCount,
            SemanticKeys.MemoryKitCapacityGb,
            SemanticKeys.GpuLengthMm,
            SemanticKeys.GpuRecommendedPsuWatts,
            SemanticKeys.GpuPcie62PinRequiredCount,
            SemanticKeys.Gpu12VhpwrRequiredCount,
            SemanticKeys.StorageInterface,
            SemanticKeys.PsuRatedWatts,
            SemanticKeys.PsuPcie62PinCount,
            SemanticKeys.Psu12VhpwrCount,
            SemanticKeys.PsuCpuEps8PinCount,
            SemanticKeys.PsuFormFactor,
            SemanticKeys.CaseSupportedMotherboardFormFactor,
            SemanticKeys.CaseGpuMaxLengthMm,
            SemanticKeys.CaseCoolerMaxHeightMm,
            SemanticKeys.CaseSupportedPsuFormFactor,
            SemanticKeys.CoolerHeightMm,
        };
}
