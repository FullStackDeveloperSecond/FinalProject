using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;

namespace DoSelect.Api.IntegrationTests.Builds;

[Collection(nameof(CompatibilityChecksApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CompatibilityChecksApiTests
{
    private readonly CompatibilityChecksApiFixture _fixture;

    public CompatibilityChecksApiTests(CompatibilityChecksApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Check_ReturnsOkWithCompatibleOverall_ForAMatchingSocket()
    {
        var components = await _fixture.SeedCompleteBuildComponentsAsync();

        using var response = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
            _fixture.Client,
            "/api/v1/compatibility-checks",
            new
            {
                items = CompatibilityChecksApiFixture.ToBuildItems(components)
                    .Select(item => new { skuPublicId = item.SkuPublicId, quantity = item.Quantity })
                    .ToArray(),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("compatible", body.GetProperty("overall").GetString());
        Assert.Equal(0, body.GetProperty("results").GetArrayLength());
        Assert.True(body.GetProperty("ruleSetVersion").GetInt32() > 0);
    }

    [Fact]
    public async Task Check_ReturnsOkWithBlockedOverallAndFindings_ForAMismatchedSocket()
    {
        var components = await _fixture.SeedCompleteBuildComponentsAsync();
        var mismatchedBoard = await _fixture.SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "LGA1700",
                [CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 128m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var items = CompatibilityChecksApiFixture.ToBuildItems(components with { Motherboard = mismatchedBoard });

        using var response = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
            _fixture.Client,
            "/api/v1/compatibility-checks",
            new { items = items.Select(item => new { skuPublicId = item.SkuPublicId, quantity = item.Quantity }).ToArray() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("blocked", body.GetProperty("overall").GetString());
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(results, result => result.GetProperty("ruleCode").GetString() == CompatibilityRuleCodes.CpuSocket);
    }

    [Fact]
    public async Task Check_ReturnsValidationProblem_ForAnUnknownSku()
    {
        using var response = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
            _fixture.Client,
            "/api/v1/compatibility-checks",
            new { items = new object[] { new { skuPublicId = Guid.NewGuid(), quantity = 1 } } });

        var (status, code, _) = await CompatibilityChecksApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task Check_ReturnsValidationProblem_ForTooManyItems()
    {
        var items = Enumerable.Range(0, 21).Select(_ => new { skuPublicId = Guid.NewGuid(), quantity = 1 }).ToArray();

        using var response = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
            _fixture.Client, "/api/v1/compatibility-checks", new { items });

        var (status, code, _) = await CompatibilityChecksApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", code);
    }
}
