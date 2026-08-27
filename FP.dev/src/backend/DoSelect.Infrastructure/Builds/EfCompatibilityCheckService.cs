using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Builds;
using DoSelect.Domain.Builds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Builds;

public sealed class EfCompatibilityCheckService : ICompatibilityCheckService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly EfCompatibilityFactsReader _factsReader;

    public EfCompatibilityCheckService(DoSelectDbContext dbContext, EfCompatibilityFactsReader factsReader)
    {
        _dbContext = dbContext;
        _factsReader = factsReader;
    }

    public async Task<CompatibilityCheckDto> CheckAsync(
        CompatibilityCheckRequest request,
        long? buildListId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mergedItems = MergeAndValidateItems(request.Items);

        var resolution = await _factsReader.ResolveAsync(mergedItems, cancellationToken);
        if (resolution.UnresolvedSkuPublicIds.Count > 0)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"Unknown SKU(s): {string.Join(", ", resolution.UnresolvedSkuPublicIds)}.");
        }

        var (settings, settingsVersion, disabledRuleCodes, _) = await LoadCurrentSettingsAsync(cancellationToken);
        var evaluation = CompatibilityRuleEngine.Evaluate(resolution.Components, settings, disabledRuleCodes);

        var now = DateTime.UtcNow;
        var results = evaluation.Findings
            .Select(finding => new CompatibilityFindingDto(
                finding.RuleCode,
                finding.Severity,
                finding.MessageKey,
                finding.SubjectSkuPublicIds,
                finding.Facts))
            .ToList();

        // 組長 PR #34 round-3 review: every check must leave an immutable
        // CompatibilityCheckRun/Result snapshot (資料契約), not just the admin rule-test tool's own
        // path. Every CheckAsync caller funnels through here, so recording it once at this level
        // covers the public/general check endpoint, BuildList create/update, share re-validate,
        // and the admin test tool uniformly.
        await RecordRunAsync(mergedItems, buildListId, settingsVersion, evaluation, now, cancellationToken);

        return new CompatibilityCheckDto(
            OverallToken(evaluation.Overall),
            CompatibilityRuleEngine.RuleSetVersion,
            settingsVersion,
            results,
            now);
    }

    /// <summary>Schema Version 1 envelope for CompatibilityCheckResult.FactsJson (DEC-P310) — a format-version wrapper around the rule engine's own Facts payload, distinct from BuildCanonicalInputText's own unrelated "v1|" input-hash prefix.</summary>
    private const int FactsSchemaVersion = 1;

    /// <summary>
    /// Participates in the caller's already-open transaction if one exists (e.g.
    /// EfBuildListService.CreateAsync's whole-method transaction, or
    /// EfCompatibilityRuleAdminService.TestAsync's own transaction wrapping this call plus its
    /// audit write), so a later failure in that caller rolls this back too. Otherwise opens its
    /// own transaction just around these two saves, so a Run row can never persist without its
    /// Results (or vice versa) even when called standalone (the public check endpoint) — 組長's
    /// round-3 review found the two were previously independent SaveChangesAsync calls with no
    /// shared transaction at all. Returns the persisted Run so a caller that needs its PublicId
    /// (DEC-P309: as the Audit ResourcePublicId) doesn't have to re-query for it.
    /// </summary>
    internal async Task<CompatibilityCheckRun> RecordRunAsync(
        IReadOnlyList<BuildItemInput> mergedItems,
        long? buildListId,
        int settingsVersion,
        BuildCompatibilityEvaluation evaluation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var inputHash = SHA256.HashData(Encoding.UTF8.GetBytes(BuildCanonicalInputText(mergedItems)));

        var ownTransaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var run = new CompatibilityCheckRun(
                Guid.CreateVersion7(), buildListId, CompatibilityRuleEngine.RuleSetVersion, settingsVersion,
                evaluation.Overall, inputHash, now);
            _dbContext.CompatibilityCheckRuns.Add(run);
            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var finding in evaluation.Findings)
            {
                _dbContext.CompatibilityCheckResults.Add(new CompatibilityCheckResult(
                    run.Id, finding.RuleCode, finding.Severity, finding.MessageKey,
                    JsonSerializer.Serialize(new FactsEnvelope(FactsSchemaVersion, finding.Facts))));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken);
            }

            return run;
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }
        }
    }

    private sealed record FactsEnvelope(int SchemaVersion, IReadOnlyDictionary<string, object?> Facts);

    /// <summary>
    /// Same-SKU entries are merged by summing quantity (per 相容性規則後台設計.md's test-endpoint
    /// convention: "同一 SKU 先合併數量"), applied here too so both entry points share one rule.
    /// </summary>
    internal static IReadOnlyList<BuildItemInput> MergeAndValidateItems(IReadOnlyList<BuildItemInput> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > 20)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                "Between 1 and 20 items are required.");
        }

        var merged = items
            .GroupBy(item => item.SkuPublicId)
            .Select(group => new BuildItemInput(group.Key, group.Sum(item => item.Quantity)))
            .ToList();

        foreach (var item in merged)
        {
            if (item.Quantity is < 1 or > 8)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"SKU '{item.SkuPublicId}' quantity must be between 1 and 8.");
            }
        }

        return merged;
    }

    /// <summary>
    /// Reads the current effective value of every (RuleCode, SettingCode) pair — grouped by the
    /// pair, not just SettingCode, since <see cref="CompatibilityRuleActivationSettingCodes.IsActive"/>
    /// is the same SettingCode reused across all 12 rules and a SettingCode-only grouping would
    /// collapse them into one. <see cref="CompatibilityWarningSettingCodes"/> each belong to
    /// exactly one rule already, so this grouping doesn't change their behavior.
    /// </summary>
    internal async Task<(
        BuildCompatibilityWarningSettings Settings,
        int SettingsVersion,
        IReadOnlySet<string> DisabledRuleCodes,
        IReadOnlyDictionary<(string RuleCode, string SettingCode), byte[]> RowVersionsByKey)>
        LoadCurrentSettingsAsync(CancellationToken cancellationToken)
    {
        var currentRows = await _dbContext.CompatibilityRuleSettings
            .GroupBy(setting => new { setting.RuleCode, setting.SettingCode })
            .Select(group => group.OrderByDescending(setting => setting.SettingsVersion).First())
            .ToListAsync(cancellationToken);

        // DEC-P311: each rule's warning-setting/activation write is now gated by this specific
        // (RuleCode, SettingCode) pair's latest row RowVersion, not the global SettingsVersion
        // counter below (which remains a whole-ruleset reporting number only).
        var rowVersionsByKey = currentRows.ToDictionary(
            row => (row.RuleCode, row.SettingCode),
            row => row.RowVersion);

        if (currentRows.Count == 0)
        {
            return (BuildCompatibilityWarningSettings.Default, 1, new HashSet<string>(), rowVersionsByKey);
        }

        decimal ValueFor(string settingCode, decimal fallback)
        {
            var row = currentRows.FirstOrDefault(setting => setting.SettingCode == settingCode);
            return row?.DecimalValue ?? fallback;
        }

        var defaults = BuildCompatibilityWarningSettings.Default;
        var settings = new BuildCompatibilityWarningSettings(
            ValueFor(CompatibilityWarningSettingCodes.GpuClearanceWarningMm, defaults.GpuClearanceWarningMm),
            ValueFor(CompatibilityWarningSettingCodes.CoolerClearanceWarningMm, defaults.CoolerClearanceWarningMm),
            ValueFor(CompatibilityWarningSettingCodes.PsuReserveWarningPercent, defaults.PsuReserveWarningPercent),
            ValueFor(CompatibilityWarningSettingCodes.RemainingRamSlotWarningCount, defaults.RemainingRamSlotWarningCount),
            ValueFor(CompatibilityWarningSettingCodes.RemainingStoragePortWarningCount, defaults.RemainingStoragePortWarningCount));

        var disabledRuleCodes = currentRows
            .Where(row => row.SettingCode == CompatibilityRuleActivationSettingCodes.IsActive && row.BooleanValue == false)
            .Select(row => row.RuleCode)
            .ToHashSet();

        var settingsVersion = currentRows.Max(setting => setting.SettingsVersion);
        return (settings, settingsVersion, disabledRuleCodes, rowVersionsByKey);
    }

    public async Task<int> PurgeExpiredRunsAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        // Filters/orders on EvaluatedAtUtc (the entity's own business timestamp) rather than the
        // inherited PublicEntity.CreatedAtUtc column, which the two constructor params always set
        // equal. 組長 PR #34 round-5 review, item 3: seeks
        // IX_CompatibilityCheckRuns_EvaluatedAtUtc_Id (added this round — the older
        // BuildListId-leading index couldn't be seeked by a BuildListId-less date filter).
        // ThenBy(Id) matches that index's trailing column, giving a stable, non-reshuffling batch
        // order across repeated calls even when many rows share the same millisecond timestamp.
        var expiredRunIds = await _dbContext.CompatibilityCheckRuns
            .Where(run => run.EvaluatedAtUtc < olderThanUtc)
            .OrderBy(run => run.EvaluatedAtUtc)
            .ThenBy(run => run.Id)
            .Select(run => run.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (expiredRunIds.Count == 0)
        {
            return 0;
        }

        // 組長 PR #34 review: the Results-then-Runs delete used to be two independent
        // ExecuteDeleteAsync commands with no shared transaction — a failure between them (e.g.
        // the Runs delete failing after Results already committed) permanently corrupted the
        // "immutable technical snapshot" invariant by leaving a Run with no Results. Both deletes
        // now share one SQL Server transaction; a retry after a rollback re-queries the same or
        // next batch fresh, same as before, but a partial batch can no longer persist.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Results have a Restrict FK to their Run — delete children before parents.
        await _dbContext.CompatibilityCheckResults
            .Where(result => expiredRunIds.Contains(result.CompatibilityCheckRunId))
            .ExecuteDeleteAsync(cancellationToken);

        var deletedCount = await _dbContext.CompatibilityCheckRuns
            .Where(run => expiredRunIds.Contains(run.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return deletedCount;
    }

    /// <summary>
    /// Canonical, order-independent text for InputHash: sorted by SkuPublicId so the same
    /// content always hashes the same regardless of request order, and including Quantity — a
    /// hash of SkuPublicIds alone couldn't distinguish quantity 1 from quantity 8 of the same
    /// SKU, even though slot counts, capacity, storage ports and power draw all depend on it
    /// (組長 PR #34 round-4 review, item 4). "v1|" is a format-version prefix so a future change
    /// to this canonical shape is guaranteed to produce different hashes from today's, rather
    /// than silently colliding with them.
    /// </summary>
    internal static string BuildCanonicalInputText(IReadOnlyList<BuildItemInput> mergedItems) =>
        "v1|" + string.Join(';', mergedItems
            .OrderBy(item => item.SkuPublicId)
            .Select(item => $"{item.SkuPublicId:D}:{item.Quantity}"));

    internal static string OverallToken(CompatibilityOverall overall) => overall switch
    {
        CompatibilityOverall.Compatible => CompatibilitySeverityTokens.Compatible,
        CompatibilityOverall.Warning => CompatibilitySeverityTokens.Warning,
        CompatibilityOverall.Blocked => CompatibilitySeverityTokens.Blocked,
        CompatibilityOverall.InsufficientData => CompatibilitySeverityTokens.InsufficientData,
        _ => throw new ArgumentOutOfRangeException(nameof(overall)),
    };
}
