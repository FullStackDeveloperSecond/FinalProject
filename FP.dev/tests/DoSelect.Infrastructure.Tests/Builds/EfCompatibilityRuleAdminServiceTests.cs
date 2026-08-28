using DoSelect.Application.Auditing;
using DoSelect.Application.Builds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Builds;

[Collection(nameof(CompatibilityRuleAdminServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfCompatibilityRuleAdminServiceTests
{
    private static readonly AuditRequestContext AuditContext = CompatibilityRuleAdminServiceFixture.TestAuditContext;

    private readonly CompatibilityRuleAdminServiceFixture _fixture;

    public EfCompatibilityRuleAdminServiceTests(CompatibilityRuleAdminServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListAsync_ReturnsAllTwelveRules_WithWarningSettingOnlyForTheFiveTunableRules()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Equal(15, result.Rules.Count);
        Assert.Equal(5, result.Rules.Count(rule => rule.WarningSetting is not null));
        Assert.All(result.Rules, rule => Assert.True(rule.IsActive));
    }

    [Fact]
    public async Task UpdateWarningSettingAsync_UpdatesTheValue_AndBumpsSettingsVersion()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = CreateService(context);
        var before = await service.ListAsync(CancellationToken.None);
        var currentVersion = before.SettingsVersion;
        var rowVersion = GetWarningRowVersion(before, CompatibilityRuleCodes.GpuLength);

        var updated = await service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.GpuLength,
            adminUserId,
            new UpdateWarningSettingRequest(30m, rowVersion, "Tighten the warning"),
            AuditContext,
            CancellationToken.None);

        Assert.Equal(30m, updated.WarningSetting!.Value);
        Assert.NotNull(updated.WarningSetting!.RowVersion);
        var after = await service.ListAsync(CancellationToken.None);
        Assert.Equal(currentVersion + 1, after.SettingsVersion);
    }

    [Fact]
    public async Task UpdateWarningSettingAsync_Throws_ThresholdOutOfRange_ForAnInvalidValue()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = CreateService(context);
        var before = await service.ListAsync(CancellationToken.None);
        var rowVersion = GetWarningRowVersion(before, CompatibilityRuleCodes.GpuLength);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.GpuLength,
            adminUserId,
            new UpdateWarningSettingRequest(999m, rowVersion, "Bad value"),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.CompatibilityThresholdOutOfRange, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateWarningSettingAsync_Throws_ValidationFailed_ForARuleWithNoAdjustableThreshold()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = CreateService(context);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.CpuSocket,
            adminUserId,
            new UpdateWarningSettingRequest(1m, null, "No such threshold"),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateWarningSettingAsync_Throws_ConcurrencyConflict_ForAStaleRowVersion()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = CreateService(context);
        var before = await service.ListAsync(CancellationToken.None);
        var staleRowVersion = GetWarningRowVersion(before, CompatibilityRuleCodes.CoolerHeight);

        await service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.CoolerHeight, adminUserId,
            new UpdateWarningSettingRequest(15m, staleRowVersion, "First edit"), AuditContext, CancellationToken.None);

        // Retrying with the same (now stale) RowVersion must conflict — a row now exists for this
        // key where the caller still believes there is none (or an older one).
        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.CoolerHeight, adminUserId,
            new UpdateWarningSettingRequest(20m, staleRowVersion, "Stale retry"), AuditContext, CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    /// <summary>
    /// DEC-P311: concurrency is now gated per (RuleCode, SettingCode) RowVersion — two admins
    /// racing the *same* rule must still see exactly one winner. The sys.sp_getapplock (5s wait,
    /// not fail-fast — 組長 PR #34 round-3 review) still serializes the whole table, so the loser
    /// simply waits its turn; once it gets the lock, its own per-key RowVersion check (now stale)
    /// is what actually rejects it — not the lock contention itself.
    /// </summary>
    [Fact]
    public async Task UpdateWarningSettingAsync_WhenTwoAdminsConcurrentlyUpdateTheSameRule_OnlyOneSucceeds()
    {
        await using var seedContext = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(seedContext, AuditRoleNames.CatalogManager);
        var before = await CreateService(seedContext).ListAsync(CancellationToken.None);
        var startingRowVersion = GetWarningRowVersion(before, CompatibilityRuleCodes.PsuCapacity);
        var startingSettingsVersion = before.SettingsVersion;

        await using var contextA = CompatibilityRuleAdminServiceFixture.CreateContext();
        await using var contextB = CompatibilityRuleAdminServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);

        var results = await Task.WhenAll(
            RunOrCaptureConflictAsync(() => serviceA.UpdateWarningSettingAsync(
                CompatibilityRuleCodes.PsuCapacity, adminUserId,
                new UpdateWarningSettingRequest(35m, startingRowVersion, "Admin A"), AuditContext, CancellationToken.None)),
            RunOrCaptureConflictAsync(() => serviceB.UpdateWarningSettingAsync(
                CompatibilityRuleCodes.PsuCapacity, adminUserId,
                new UpdateWarningSettingRequest(40m, startingRowVersion, "Admin B"), AuditContext, CancellationToken.None)));

        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);

        var finalVersion = (await CreateService(seedContext).ListAsync(CancellationToken.None)).SettingsVersion;
        Assert.Equal(startingSettingsVersion + 1, finalVersion);
    }

    /// <summary>
    /// 組長 PR #34 round-3 review: the previous @LockTimeout = 0 meant two admins updating
    /// *different* rules at the same moment still had one spuriously get ConcurrencyConflict from
    /// the lock itself, never even reaching its own (independent, non-conflicting) per-key
    /// RowVersion check. With a 5-second wait instead of fail-fast, both must now succeed —
    /// serialized in time by the shared lock, but not rejecting each other — and consume two
    /// distinct, consecutive global SettingsVersion numbers.
    /// </summary>
    [Fact]
    public async Task UpdateWarningSettingAsync_WhenTwoAdminsConcurrentlyUpdateDifferentRules_BothSucceed()
    {
        await using var seedContext = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(seedContext, AuditRoleNames.CatalogManager);
        var before = await CreateService(seedContext).ListAsync(CancellationToken.None);
        var gpuRowVersion = GetWarningRowVersion(before, CompatibilityRuleCodes.GpuLength);
        var coolerRowVersion = GetWarningRowVersion(before, CompatibilityRuleCodes.CoolerHeight);
        var startingSettingsVersion = before.SettingsVersion;

        await using var contextA = CompatibilityRuleAdminServiceFixture.CreateContext();
        await using var contextB = CompatibilityRuleAdminServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);

        var results = await Task.WhenAll(
            serviceA.UpdateWarningSettingAsync(
                CompatibilityRuleCodes.GpuLength, adminUserId,
                new UpdateWarningSettingRequest(30m, gpuRowVersion, "Admin A"), AuditContext, CancellationToken.None),
            serviceB.UpdateWarningSettingAsync(
                CompatibilityRuleCodes.CoolerHeight, adminUserId,
                new UpdateWarningSettingRequest(15m, coolerRowVersion, "Admin B"), AuditContext, CancellationToken.None));

        Assert.Equal(30m, results[0].WarningSetting!.Value);
        Assert.Equal(15m, results[1].WarningSetting!.Value);

        var settingsVersions = await seedContext.CompatibilityRuleSettings
            .Where(row => row.SettingsVersion > startingSettingsVersion)
            .Select(row => row.SettingsVersion)
            .ToListAsync();
        // Both writers must have consumed a distinct global version — if they'd raced each other
        // into the same "next" number, the unique index would have made one of them a genuine
        // DbUpdateException instead of the clean success asserted above.
        Assert.Equal(2, settingsVersions.Distinct().Count());
        Assert.Equal([startingSettingsVersion + 1, startingSettingsVersion + 2], settingsVersions.OrderBy(v => v));
    }

    private static async Task<bool> RunOrCaptureConflictAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (BuildWriteException exception) when (exception.ErrorCode == BuildWriteException.ErrorCodes.ConcurrencyConflict)
        {
            return false;
        }
    }

    [Fact]
    public async Task SetActivationAsync_DisablesTheRule_AndSubsequentChecksReportRuleDisabled()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.SuperAdmin);
        // Full 8-category build (CompatibilityEvaluator requires every singleton role present
        // before any pairwise rule runs) with a socket-mismatched Motherboard (would normally
        // Block via CPU_SOCKET); Generation/Chipset are a known-compatible pair so CPU_CHIPSET
        // doesn't also fire and muddy the "disabled rule -> Compatible overall" assertion below.
        var components = await EfBuildListServiceTests.SeedCompleteBuildComponentsAsync(context);
        var mismatchedBoard = await CompatibilityRuleAdminServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Motherboard,
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

        var service = CreateService(context);
        var before = await service.ListAsync(CancellationToken.None);
        var rowVersion = GetActivationRowVersion(before, CompatibilityRuleCodes.CpuSocket);

        var updated = await service.SetActivationAsync(
            CompatibilityRuleCodes.CpuSocket, adminUserId,
            new SetRuleActivationRequest(false, "Demo mode", rowVersion), AuditContext, CancellationToken.None);
        Assert.False(updated.IsActive);
        Assert.NotNull(updated.ActivationRowVersion);

        var checkService = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));
        var items = EfBuildListServiceTests.ToBuildItems(components with { Motherboard = mismatchedBoard });
        var checkResult = await checkService.CheckAsync(
            new CompatibilityCheckRequest(items),
            null,
            CancellationToken.None);

        var finding = Assert.Single(checkResult.Results, f => f.RuleCode == CompatibilityRuleCodes.CpuSocket);
        Assert.Equal("ruleDisabled", finding.Severity);
        Assert.Equal("compatible", checkResult.Overall);
    }

    [Fact]
    public async Task SetActivationAsync_Throws_ConcurrencyConflict_ForAStaleRowVersion()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.SuperAdmin);
        var service = CreateService(context);
        var before = await service.ListAsync(CancellationToken.None);
        var staleRowVersion = GetActivationRowVersion(before, CompatibilityRuleCodes.MemoryCapacity);

        await service.SetActivationAsync(
            CompatibilityRuleCodes.MemoryCapacity, adminUserId,
            new SetRuleActivationRequest(false, "First edit", staleRowVersion), AuditContext, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetActivationAsync(
            CompatibilityRuleCodes.MemoryCapacity, adminUserId,
            new SetRuleActivationRequest(true, "Stale retry", staleRowVersion), AuditContext, CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    /// <summary>DEC-BATCH-026 (DEC-P309): a successful setting/activation write must leave exactly one central Audit Log row referencing the new CompatibilityRuleSetting row, with real Before／After values (not just "changed").</summary>
    [Fact]
    public async Task UpdateWarningSettingAsync_PersistsExactlyOneAuditLogRow_WithRealBeforeAndAfterValues()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = CreateService(context);
        var before = await service.ListAsync(CancellationToken.None);
        var currentVersion = before.SettingsVersion;
        var rowVersion = GetWarningRowVersion(before, CompatibilityRuleCodes.MemorySlots);

        await service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.MemorySlots, adminUserId,
            new UpdateWarningSettingRequest(2m, rowVersion, "Tighten the warning"), AuditContext, CancellationToken.None);

        var settingRow = await context.CompatibilityRuleSettings.SingleAsync(
            row => row.RuleCode == CompatibilityRuleCodes.MemorySlots && row.SettingsVersion == currentVersion + 1);
        // Filters on ResourcePublicId (unique per row, unlike Action, which every test in this
        // shared-collection database's other UpdateWarningSettingAsync calls also produces) so
        // this stays exact regardless of how many other tests already ran in the same database.
        var auditRow = await context.AuditLogs.SingleAsync(
            row => row.Action == AuditActions.CompatibilityRuleWarningSettingUpdate &&
                row.ResourcePublicId == settingRow.PublicId);
        Assert.Equal(AuditResourceTypes.CompatibilityRuleSetting, auditRow.ResourceType);
        Assert.Contains("\"field\":\"value\"", auditRow.ChangedFieldsJson);
        Assert.Contains("\"afterCode\":\"2\"", auditRow.ChangedFieldsJson);
    }

    [Fact]
    public async Task TestAsync_Throws_ValidationFailed_ForAnUnknownRuleCode()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var sku = await CompatibilityRuleAdminServiceFixture.SeedComponentSkuAsync(context, CompatibilityCatalogContract.Categories.Storage);
        var service = CreateService(context);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.TestAsync(
            new CompatibilityRuleTestRequest(
                [new BuildItemInput(sku.PublicId, 1)], ["NOT_A_REAL_RULE"], false, null),
            adminUserId,
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task TestAsync_UsesDraftSettings_WithoutPersistingThem()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var sku = await CompatibilityRuleAdminServiceFixture.SeedComponentSkuAsync(context, CompatibilityCatalogContract.Categories.Storage);
        var service = CreateService(context);
        var before = await service.ListAsync(CancellationToken.None);

        var result = await service.TestAsync(
            new CompatibilityRuleTestRequest(
                [new BuildItemInput(sku.PublicId, 1)],
                null,
                true,
                new Dictionary<string, decimal> { [CompatibilityWarningSettingCodes.GpuClearanceWarningMm] = 45m }),
            adminUserId,
            AuditContext,
            CancellationToken.None);

        // A single Storage SKU can never reach "compatible" under the canonical evaluator (all 6
        // singleton roles + Memory must be present first) — this test's subject is that the draft
        // settings override is applied without persisting, not the compatibility verdict itself.
        Assert.Equal("insufficientData", result.Overall);
        var after = await service.ListAsync(CancellationToken.None);
        Assert.Equal(before.SettingsVersion, after.SettingsVersion);
    }

    [Fact]
    public async Task TestAsync_PersistsAnAuditRun_AndExactlyOneAuditLogRowReferencingIt()
    {
        await using var context = CompatibilityRuleAdminServiceFixture.CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var sku = await CompatibilityRuleAdminServiceFixture.SeedComponentSkuAsync(context, CompatibilityCatalogContract.Categories.Storage);
        var service = CreateService(context);

        // This is a shared-collection database — other tests' own CheckAsync/TestAsync calls
        // leave their own Run rows behind, so a bare SingleAsync() over the whole table would be
        // as fragile as searching by a shared literal value. Diffing against the Ids that already
        // existed before this call isolates the one Run this specific call created.
        var priorRunIds = await context.CompatibilityCheckRuns.Select(run => run.Id).ToListAsync();

        await service.TestAsync(
            new CompatibilityRuleTestRequest([new BuildItemInput(sku.PublicId, 1)], null, false, null),
            adminUserId,
            AuditContext,
            CancellationToken.None);

        var run = await context.CompatibilityCheckRuns.SingleAsync(candidate => !priorRunIds.Contains(candidate.Id));
        var auditRow = await context.AuditLogs.SingleAsync(
            row => row.Action == AuditActions.CompatibilityRuleTest && row.ResourcePublicId == run.PublicId);
        Assert.Equal(AuditResourceTypes.CompatibilityCheckRun, auditRow.ResourceType);
        Assert.Contains(Convert.ToHexString(run.InputHash), auditRow.ChangedFieldsJson);
    }

    private static byte[]? GetWarningRowVersion(CompatibilityRuleListDto list, string ruleCode) =>
        list.Rules.Single(rule => rule.RuleCode == ruleCode).WarningSetting!.RowVersion;

    private static byte[]? GetActivationRowVersion(CompatibilityRuleListDto list, string ruleCode) =>
        list.Rules.Single(rule => rule.RuleCode == ruleCode).ActivationRowVersion;

    private static EfCompatibilityRuleAdminService CreateService(
        DoSelect.Infrastructure.Persistence.DoSelectDbContext context) =>
        new(
            context,
            new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context)),
            new EfCompatibilityCatalogReader(context),
            new EfAuditWriter(context, TimeProvider.System));
}

/// <summary>
/// 組長 PR #34 round-3 review: the shared-collection fixture can't prove a *genuine* first write's
/// Before value (some other test may have already touched the same RuleCode/SettingCode), and its
/// existing audit test only checked afterCode. Own throwaway database (same pattern as
/// EfBuildListServiceRuleDisabledTests) so these two assertions are provably exact.
/// </summary>
[Trait("Category", "RequiresSqlServer")]
public sealed class EfCompatibilityRuleAdminServiceAuditBeforeValueTests : IAsyncLifetime
{
    private readonly string _connectionString =
        SqlServerTestConnection.Build($"DoSelectCompatAuditBeforeValueTests_{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await CompatibilityCheckServiceFixture.SeedCategoriesAndSpecTemplatesAsync(context);
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    private DoSelect.Infrastructure.Persistence.DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelect.Infrastructure.Persistence.DoSelectDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new DoSelect.Infrastructure.Persistence.DoSelectDbContext(options);
    }

    [Fact]
    public async Task UpdateWarningSettingAsync_OnAGenuineFirstWrite_AuditBeforeIsTheProgramDefault_NotNull()
    {
        await using var context = CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfCompatibilityRuleAdminService(
            context, new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context)),
            new EfCompatibilityCatalogReader(context), new EfAuditWriter(context, TimeProvider.System));

        // A fresh database has no CompatibilityRuleSettings rows at all yet — this really is the
        // first write for GPU_LENGTH's warning threshold.
        await service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.GpuLength, adminUserId,
            new UpdateWarningSettingRequest(30m, null, "First-ever edit"),
            CompatibilityRuleAdminServiceFixture.TestAuditContext, CancellationToken.None);

        var auditRow = await context.AuditLogs.SingleAsync(
            row => row.Action == AuditActions.CompatibilityRuleWarningSettingUpdate);
        var defaultValue = CompatibilityWarningSettingRanges.ByCode[CompatibilityWarningSettingCodes.GpuClearanceWarningMm].Default;
        Assert.Contains($"\"beforeCode\":\"{defaultValue}\"", auditRow.ChangedFieldsJson);
        Assert.Contains("\"afterCode\":\"30\"", auditRow.ChangedFieldsJson);
    }

    [Fact]
    public async Task SetActivationAsync_OnAGenuineFirstWrite_AuditBeforeIsTrue_NotNull()
    {
        await using var context = CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.SuperAdmin);
        var service = new EfCompatibilityRuleAdminService(
            context, new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context)),
            new EfCompatibilityCatalogReader(context), new EfAuditWriter(context, TimeProvider.System));

        await service.SetActivationAsync(
            CompatibilityRuleCodes.CpuSocket, adminUserId,
            new SetRuleActivationRequest(false, "First-ever disable", null),
            CompatibilityRuleAdminServiceFixture.TestAuditContext, CancellationToken.None);

        var auditRow = await context.AuditLogs.SingleAsync(
            row => row.Action == AuditActions.CompatibilityRuleActivationUpdate);
        Assert.Contains("\"beforeCode\":\"True\"", auditRow.ChangedFieldsJson);
        Assert.Contains("\"afterCode\":\"False\"", auditRow.ChangedFieldsJson);
    }

    /// <summary>
    /// 組長 PR #34 round-3 review: after updating a DIFFERENT rule first, the global SettingsVersion
    /// has already moved — the Before recorded on a subsequent write to a fresh rule must be that
    /// current global version, not "no previous row for this key" (null) and not some other key's
    /// stale version number.
    /// </summary>
    [Fact]
    public async Task UpdateWarningSettingAsync_AfterAnotherRuleWasUpdatedFirst_AuditSettingsVersionBeforeIsTheCurrentGlobalVersion()
    {
        await using var context = CreateContext();
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfCompatibilityRuleAdminService(
            context, new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context)),
            new EfCompatibilityCatalogReader(context), new EfAuditWriter(context, TimeProvider.System));

        var startingVersion = (await service.ListAsync(CancellationToken.None)).SettingsVersion;
        await service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.CoolerHeight, adminUserId,
            new UpdateWarningSettingRequest(15m, null, "Bumps the global version first"),
            CompatibilityRuleAdminServiceFixture.TestAuditContext, CancellationToken.None);

        // GPU_LENGTH's own key has never been written, but the global version has already moved
        // to startingVersion + 1 because of the CoolerHeight write above.
        await service.UpdateWarningSettingAsync(
            CompatibilityRuleCodes.GpuLength, adminUserId,
            new UpdateWarningSettingRequest(30m, null, "Second rule, first key write"),
            CompatibilityRuleAdminServiceFixture.TestAuditContext, CancellationToken.None);

        var auditRow = await context.AuditLogs.SingleAsync(
            row => row.Action == AuditActions.CompatibilityRuleWarningSettingUpdate &&
                row.ChangedFieldsJson.Contains("\"afterCode\":\"30\""));
        Assert.Contains($"\"beforeCode\":\"{startingVersion + 1}\"", auditRow.ChangedFieldsJson);
        Assert.Contains($"\"afterCode\":\"{startingVersion + 2}\"", auditRow.ChangedFieldsJson);
    }
}
