using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Builds;

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
        var cpu = await _fixture.SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CpuSocket] = "AM5",
                [CompatibilitySemanticKeys.CpuGeneration] = "Ryzen7000",
            });
        var board = await _fixture.SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.BoardSocket] = "AM5",
                [CompatibilitySemanticKeys.BoardChipset] = "X670E",
            });

        using var response = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
            _fixture.Client,
            "/api/v1/compatibility-checks",
            new
            {
                items = new object[]
                {
                    new { skuPublicId = cpu.PublicId, quantity = 1 },
                    new { skuPublicId = board.PublicId, quantity = 1 },
                },
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
        var cpu = await _fixture.SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.CpuSocket] = "AM5" });
        var board = await _fixture.SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.BoardSocket] = "LGA1700" });

        using var response = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
            _fixture.Client,
            "/api/v1/compatibility-checks",
            new
            {
                items = new object[]
                {
                    new { skuPublicId = cpu.PublicId, quantity = 1 },
                    new { skuPublicId = board.PublicId, quantity = 1 },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("blocked", body.GetProperty("overall").GetString());
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(results, result => result.GetProperty("ruleCode").GetString() == BuildCompatibilityRuleCodes.CpuSocket);
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
