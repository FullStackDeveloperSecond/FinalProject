using DoSelect.Application.Ai;
using DoSelect.Infrastructure.Ai;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class AiProductSearchManualPartTests
{
    [Fact]
    public void TryCreateManualComponent_CompleteConfirmedCpu_UsesCanonicalCompatibilityShape()
    {
        var part = CpuPart(
        [
            new AiRequiredSpec("cpu_socket", "eq", "AM5", null),
            new AiRequiredSpec("cpu_generation", "eq", "RYZEN_7000", null),
            new AiRequiredSpec("power_draw_watts", "eq", "120", "W"),
        ]);

        var valid = EfAiProductSearchCatalog.TryCreateManualComponent(part, out var component);

        Assert.True(valid);
        Assert.NotNull(component);
        Assert.Equal("CPU", component.CategoryCode);
        Assert.Equal("AM5", component.Specifications["CPU_SOCKET"].OptionCode);
        Assert.Equal(120m, component.Specifications["POWER_DRAW_WATTS"].DecimalValue);
    }

    [Fact]
    public void TryCreateManualComponent_MissingCategoryHardRuleField_FailsClosed()
    {
        var part = CpuPart(
        [
            new AiRequiredSpec("cpu_socket", "eq", "AM5", null),
            new AiRequiredSpec("power_draw_watts", "eq", "120", "W"),
        ]);

        var valid = EfAiProductSearchCatalog.TryCreateManualComponent(part, out var component);

        Assert.False(valid);
        Assert.Null(component);
    }

    private static AiProductSearchExistingPart CpuPart(IReadOnlyList<AiRequiredSpec> specs) =>
        new(
            SkuPublicId: null,
            SourceType: "structuredManual",
            CategoryCode: "CPU",
            DisplayName: "既有處理器",
            Specifications: specs,
            Quantity: 1,
            ConfirmedByUser: true);
}
