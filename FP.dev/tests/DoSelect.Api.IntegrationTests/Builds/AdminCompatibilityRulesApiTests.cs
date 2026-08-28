using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Builds;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Builds;

/// <summary>
/// The compatibility-rule settings table is global (shared across every test in this
/// collection-shared database, per 相容性規則後台設計.md's append-only model) — every test here
/// re-reads the current RowVersion/value from GET immediately before acting on it rather than
/// assuming a fixed starting state, since xUnit doesn't guarantee execution order within the
/// collection. DEC-P311: write requests submit a per-rule RowVersion (Base64, null when the rule
/// has never been customized), not the old global SettingsVersion counter.
/// </summary>
[Collection(nameof(AdminCompatibilityRulesApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AdminCompatibilityRulesApiTests
{
    private readonly AdminCompatibilityRulesApiFixture _fixture;

    public AdminCompatibilityRulesApiTests(AdminCompatibilityRulesApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_ReturnsAllFifteenRules_WithNoWarningSettingForAPureHardRule()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);

        using var response = await client.GetAsync("/api/v1/admin/compatibility-rules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rules = body.GetProperty("rules").EnumerateArray().ToList();
        Assert.Equal(15, rules.Count);
        Assert.True(body.GetProperty("settingsVersion").GetInt32() >= 1);

        var gpuLength = rules.Single(r => r.GetProperty("ruleCode").GetString() == "GPU_LENGTH");
        var warningSetting = gpuLength.GetProperty("warningSetting");
        Assert.Equal("GpuClearanceWarningMm", warningSetting.GetProperty("settingCode").GetString());
        Assert.Equal(10m, warningSetting.GetProperty("minValue").GetDecimal());
        Assert.Equal(50m, warningSetting.GetProperty("maxValue").GetDecimal());

        // CPU_SOCKET is a pure hard-blocking rule with no adjustable warning threshold at all.
        var cpuSocket = rules.Single(r => r.GetProperty("ruleCode").GetString() == "CPU_SOCKET");
        Assert.Equal(JsonValueKind.Null, cpuSocket.GetProperty("warningSetting").ValueKind);
    }

    [Fact]
    public async Task List_ReturnsUnauthorized_WithoutAnAdminSession()
    {
        using var response = await _fixture.CreateClient().GetAsync("/api/v1/admin/compatibility-rules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateWarningSetting_UpdatesTheValue_ForAValidRequest()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var settingsVersion = await GetCurrentSettingsVersionAsync(client);
        var rowVersion = await GetWarningRowVersionAsync(client, "GPU_LENGTH");

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, "/api/v1/admin/compatibility-rules/GPU_LENGTH/warning-settings")
        {
            Content = JsonContent.Create(new { value = 30m, rowVersion, reason = "Tighten clearance warning" }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30m, body.GetProperty("warningSetting").GetProperty("value").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("warningSetting").GetProperty("rowVersion").ValueKind);

        var newVersion = await GetCurrentSettingsVersionAsync(client);
        Assert.Equal(settingsVersion + 1, newVersion);
    }

    [Fact]
    public async Task UpdateWarningSetting_ReturnsThresholdOutOfRange_ForAnInvalidValue()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var rowVersion = await GetWarningRowVersionAsync(client, "GPU_LENGTH");

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, "/api/v1/admin/compatibility-rules/GPU_LENGTH/warning-settings")
        {
            Content = JsonContent.Create(new { value = 999m, rowVersion, reason = "Out of range" }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);

        var (status, code, _) = await AdminCompatibilityRulesApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("compatibility_threshold_out_of_range", code);
    }

    [Fact]
    public async Task UpdateWarningSetting_ReturnsConcurrencyConflict_ForAStaleRowVersion()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var staleRowVersion = await GetWarningRowVersionAsync(client, "COOLER_HEIGHT");

        using (var first = new HttpRequestMessage(
            HttpMethod.Patch, "/api/v1/admin/compatibility-rules/COOLER_HEIGHT/warning-settings")
        {
            Content = JsonContent.Create(new { value = 15m, rowVersion = staleRowVersion, reason = "First edit" }),
        })
        {
            using var firstResponse = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        using var second = new HttpRequestMessage(
            HttpMethod.Patch, "/api/v1/admin/compatibility-rules/COOLER_HEIGHT/warning-settings")
        {
            Content = JsonContent.Create(new { value = 20m, rowVersion = staleRowVersion, reason = "Stale retry" }),
        };
        using var secondResponse = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, second);

        var (status, code, _) = await AdminCompatibilityRulesApiFixture.ReadProblemAsync(secondResponse);
        Assert.Equal((int)HttpStatusCode.Conflict, status);
        Assert.Equal("concurrency_conflict", code);
    }

    [Fact]
    public async Task SetActivation_RequiresSuperAdmin_ForbiddenForCatalogManager()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var rowVersion = await GetActivationRowVersionAsync(client, "MEMORY_TYPE");

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, "/api/v1/admin/compatibility-rules/MEMORY_TYPE/activation")
        {
            Content = JsonContent.Create(new { isActive = false, reason = "Demo mode", rowVersion }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetActivation_DisablesTheRule_ForSuperAdmin_AndListReflectsIt()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.SuperAdmin);
        var rowVersion = await GetActivationRowVersionAsync(client, "PSU_CONNECTORS");

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, "/api/v1/admin/compatibility-rules/PSU_CONNECTORS/activation")
        {
            Content = JsonContent.Create(new { isActive = false, reason = "Demo mode", rowVersion }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("activationRowVersion").ValueKind);

        using var listResponse = await client.GetAsync("/api/v1/admin/compatibility-rules");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var psuConnectors = listBody.GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("ruleCode").GetString() == "PSU_CONNECTORS");
        Assert.False(psuConnectors.GetProperty("isActive").GetBoolean());
    }

    /// <summary>DEC-BATCH-026 (DEC-P309): a successful activation write over real HTTP must also land a central Audit Log row for the new setting row, with real Before／After values — no query endpoint exists yet, so this reads rows back directly via the fixture's DbContext.</summary>
    [Fact]
    public async Task SetActivation_PersistsACentralAuditLogRow_WithRealBeforeAndAfterValues()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.SuperAdmin);
        var settingsVersion = await GetCurrentSettingsVersionAsync(client);
        var rowVersion = await GetActivationRowVersionAsync(client, "CPU_CHIPSET");

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, "/api/v1/admin/compatibility-rules/CPU_CHIPSET/activation")
        {
            Content = JsonContent.Create(new { isActive = false, reason = "Audit check", rowVersion }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = _fixture.CreateScopedContext();
        var settingRow = await context.CompatibilityRuleSettings.SingleAsync(
            row => row.RuleCode == "CPU_CHIPSET" && row.SettingsVersion == settingsVersion + 1);
        var auditRow = await context.AuditLogs.SingleAsync(
            row => row.Action == "compatibility_rule.activation.update" &&
                row.ResourcePublicId == settingRow.PublicId);
        Assert.Equal("CompatibilityRuleSetting", auditRow.ResourceType);
        Assert.Contains("\"field\":\"isActive\"", auditRow.ChangedFieldsJson);
        Assert.Contains("\"afterCode\":\"False\"", auditRow.ChangedFieldsJson);
    }

    // 組長 PR #34 round-7 review (DEC-BATCH-027): the SKU attributes GET/PUT endpoints (and the
    // three tests that exercised them: SetSkuAttributes_PersistsStoragePorts_..., SetSkuAttributes_
    // ReturnsConcurrencyConflict_..., SetSkuAttributes_ReturnsValidationProblem_...) were removed —
    // hard compatibility facts now live in the canonical Catalog spec-value model, written via the
    // ordinary product/SKU admin endpoints, not a Builds-specific one.

    [Fact]
    public async Task Test_ReturnsResults_ForAKnownBuild()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var first = await _fixture.SeedSkuAsync();
        var second = await _fixture.SeedSkuAsync();
        var settingsVersion = await GetCurrentSettingsVersionAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/compatibility-rules/test")
        {
            Content = JsonContent.Create(new
            {
                items = new object[]
                {
                    new { skuPublicId = first.PublicId, quantity = 1 },
                    new { skuPublicId = second.PublicId, quantity = 1 },
                },
                useDraftSettings = false,
            }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Two bare Storage SKUs can never reach "compatible" under the canonical evaluator (every
        // singleton role plus Memory must be present first) — this test's subject is that the
        // endpoint runs and returns a result for a known SKU pair, not the compatibility verdict.
        Assert.Equal("insufficientData", body.GetProperty("overall").GetString());
        Assert.Equal(settingsVersion, body.GetProperty("settingsVersion").GetInt32());
    }

    [Fact]
    public async Task Test_ReturnsValidationProblem_ForAnUnknownRuleCode()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var sku = await _fixture.SeedSkuAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/compatibility-rules/test")
        {
            Content = JsonContent.Create(new
            {
                items = new object[] { new { skuPublicId = sku.PublicId, quantity = 1 } },
                ruleCodes = new[] { "NOT_A_REAL_RULE" },
                useDraftSettings = false,
            }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);

        var (status, code, _) = await AdminCompatibilityRulesApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task Test_UsesDraftSettings_WithoutPersistingThem()
    {
        var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        var sku = await _fixture.SeedSkuAsync();
        var settingsVersionBefore = await GetCurrentSettingsVersionAsync(client);
        var currentGpuClearanceValue = await GetGpuClearanceValueAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/compatibility-rules/test")
        {
            Content = JsonContent.Create(new
            {
                items = new object[] { new { skuPublicId = sku.PublicId, quantity = 1 } },
                useDraftSettings = true,
                draftWarningSettings = new Dictionary<string, decimal> { ["GpuClearanceWarningMm"] = 45m },
            }),
        };
        using var response = await AdminCompatibilityRulesApiFixture.SendWithAntiforgeryAsync(client, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settingsVersionAfter = await GetCurrentSettingsVersionAsync(client);
        var gpuClearanceValueAfter = await GetGpuClearanceValueAsync(client);
        Assert.Equal(settingsVersionBefore, settingsVersionAfter);
        Assert.Equal(currentGpuClearanceValue, gpuClearanceValueAfter);
    }

    private static async Task<int> GetCurrentSettingsVersionAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/admin/compatibility-rules");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("settingsVersion").GetInt32();
    }

    private static async Task<string?> GetWarningRowVersionAsync(HttpClient client, string ruleCode)
    {
        using var response = await client.GetAsync("/api/v1/admin/compatibility-rules");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rule = body.GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("ruleCode").GetString() == ruleCode);
        var rowVersion = rule.GetProperty("warningSetting").GetProperty("rowVersion");
        return rowVersion.ValueKind == JsonValueKind.Null ? null : rowVersion.GetString();
    }

    private static async Task<string?> GetActivationRowVersionAsync(HttpClient client, string ruleCode)
    {
        using var response = await client.GetAsync("/api/v1/admin/compatibility-rules");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rule = body.GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("ruleCode").GetString() == ruleCode);
        var rowVersion = rule.GetProperty("activationRowVersion");
        return rowVersion.ValueKind == JsonValueKind.Null ? null : rowVersion.GetString();
    }

    private static async Task<decimal> GetGpuClearanceValueAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/admin/compatibility-rules");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var gpuLength = body.GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("ruleCode").GetString() == "GPU_LENGTH");
        return gpuLength.GetProperty("warningSetting").GetProperty("value").GetDecimal();
    }
}
