using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Builds;
using DoSelect.Domain.Builds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Builds;

/// <summary>
/// PR #34 round-7 review (DEC-BATCH-027／DEC-P313~P315): reuses the canonical
/// <see cref="ICompatibilityCatalogReader"/>／<see cref="CompatibilityEvaluator"/> Checkout already
/// depends on, instead of this PR's own now-removed parallel facts reader/rule engine — a SKU
/// entered through the real Catalog admin flow now gets the exact same compatibility verdict here
/// as it would at Checkout, rather than two independently-maintained models silently disagreeing.
/// </summary>
public sealed class EfCompatibilityCheckService : ICompatibilityCheckService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly ICompatibilityCatalogReader _catalogReader;
    private static readonly CompatibilityRuleCatalog RuleCatalog = CompatibilityRuleCatalog.CreateVersion1();

    public EfCompatibilityCheckService(DoSelectDbContext dbContext, ICompatibilityCatalogReader catalogReader)
    {
        _dbContext = dbContext;
        _catalogReader = catalogReader;
    }

    public async Task<CompatibilityCheckDto> CheckAsync(
        CompatibilityCheckRequest request,
        long? buildListId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mergedItems = MergeAndValidateItems(request.Items);

        var catalogResult = await _catalogReader.ReadAsync(
            mergedItems.Select(item => new CompatibilityItemReference(item.SkuPublicId, item.Quantity)).ToArray(),
            cancellationToken);
        if (catalogResult.MissingSkuPublicIds.Count > 0)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"Unknown SKU(s): {string.Join(", ", catalogResult.MissingSkuPublicIds)}.");
        }

        var (settings, settingsVersion, disabledRuleCodes, _) = await LoadCurrentSettingsAsync(cancellationToken);
        var evaluation = CompatibilityEvaluator.Evaluate(catalogResult.Components, settings, RuleCatalog);
        var (overall, results) = ApplyDisabledRules(evaluation, disabledRuleCodes);

        var now = DateTime.UtcNow;

        // 組長 PR #34 round-3 review: every check must leave an immutable
        // CompatibilityCheckRun/Result snapshot (資料契約), not just the admin rule-test tool's own
        // path. Every CheckAsync caller funnels through here, so recording it once at this level
        // covers the public/general check endpoint, BuildList create/update, share re-validate,
        // and the admin test tool uniformly.
        await RecordRunAsync(mergedItems, buildListId, settingsVersion, overall, results, now, cancellationToken);

        return new CompatibilityCheckDto(
            OverallToken(overall),
            RuleSetVersion,
            settingsVersion,
            results,
            now);
    }

    internal async Task<CompatibilityCheckDto> CheckPartialAsync(
        IReadOnlyCollection<CompatibilityComponent> components,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(components);
        var (settings, settingsVersion, disabledRuleCodes, _) =
            await LoadCurrentSettingsAsync(cancellationToken);
        var evaluation = CompatibilityEvaluator.EvaluatePartial(components, settings, RuleCatalog);
        var (overall, results) = ApplyDisabledRules(evaluation, disabledRuleCodes);
        return new CompatibilityCheckDto(
            OverallToken(overall),
            RuleSetVersion,
            settingsVersion,
            results,
            DateTime.UtcNow);
    }

    internal async Task<CompatibilityCheckDto> CheckCompleteTransientAsync(
        IReadOnlyCollection<CompatibilityComponent> components,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(components);
        var (settings, settingsVersion, disabledRuleCodes, _) =
            await LoadCurrentSettingsAsync(cancellationToken);
        var evaluation = CompatibilityEvaluator.Evaluate(components, settings, RuleCatalog);
        var (overall, results) = ApplyDisabledRules(evaluation, disabledRuleCodes);
        return new CompatibilityCheckDto(
            OverallToken(overall),
            RuleSetVersion,
            settingsVersion,
            results,
            DateTime.UtcNow);
    }

    /// <summary>
    /// The canonical <see cref="CompatibilityEvaluator"/> has no notion of an admin disabling one
    /// rule (相容性規則後台設計.md's own feature, not part of DEC-BATCH-027's checkout-facing
    /// contract). Wraps its raw output instead of forking the evaluator: a disabled rule's finding
    /// is relabeled <see cref="CompatibilitySeverityTokens.RuleDisabled"/> (never silently vanishes,
    /// per the design doc) and excluded from the top-level Overall rollup, which is recomputed from
    /// only the still-active findings rather than trusted from <see cref="CompatibilityEvaluation.Overall"/>.
    /// </summary>
    internal static (CompatibilityOverall Overall, List<CompatibilityFindingDto> Results) ApplyDisabledRules(
        CompatibilityEvaluation evaluation, IReadOnlySet<string> disabledRuleCodes)
    {
        var results = new List<CompatibilityFindingDto>(evaluation.Results.Count);
        var activeSeverities = new List<CompatibilityOverall>();
        foreach (var finding in evaluation.Results)
        {
            if (disabledRuleCodes.Contains(finding.RuleCode))
            {
                results.Add(new CompatibilityFindingDto(
                    finding.RuleCode,
                    CompatibilitySeverityTokens.RuleDisabled,
                    "compatibility.rule_disabled",
                    finding.SubjectSkuPublicIds,
                    new Dictionary<string, object?>()));
                continue;
            }

            activeSeverities.Add(finding.Severity);
            results.Add(new CompatibilityFindingDto(
                finding.RuleCode,
                OverallToken(finding.Severity),
                finding.MessageKey,
                finding.SubjectSkuPublicIds,
                finding.Facts.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal)));
        }

        var overall = activeSeverities.Count == 0
            ? CompatibilityOverall.Compatible
            : activeSeverities.Contains(CompatibilityOverall.Blocked)
                ? CompatibilityOverall.Blocked
                : activeSeverities.Contains(CompatibilityOverall.InsufficientData)
                    ? CompatibilityOverall.InsufficientData
                    : activeSeverities.Contains(CompatibilityOverall.Warning)
                        ? CompatibilityOverall.Warning
                        : CompatibilityOverall.Compatible;

        return (overall, results);
    }

    /// <summary>
    /// 組長 PR #35 round-2 review, P2-6: the admin test tool's "limit to these rule codes" filter
    /// used to only trim <c>results</c> after Overall had already been computed from *every*
    /// rule's findings — a rule the admin left unselected could still fail and drive Overall to
    /// e.g. Blocked, with no finding in the (filtered) response to explain why. Recomputes Overall
    /// from only the findings actually returned, reusing <see cref="ApplyDisabledRules"/>'s exact
    /// Blocked > InsufficientData > Warning > Compatible priority — just starting from the DTOs'
    /// own Severity tokens (already computed) instead of re-deriving from the raw evaluator output.
    /// A RuleDisabled finding still doesn't participate, same as it's excluded from
    /// <c>activeSeverities</c> above.
    /// </summary>
    internal static CompatibilityOverall RecomputeOverallFromFindings(IReadOnlyList<CompatibilityFindingDto> findings)
    {
        var activeSeverities = findings
            .Select(finding => finding.Severity)
            .Where(severity => severity != CompatibilitySeverityTokens.RuleDisabled)
            .ToHashSet(StringComparer.Ordinal);

        if (activeSeverities.Contains(CompatibilitySeverityTokens.Blocked))
        {
            return CompatibilityOverall.Blocked;
        }
        if (activeSeverities.Contains(CompatibilitySeverityTokens.InsufficientData))
        {
            return CompatibilityOverall.InsufficientData;
        }
        if (activeSeverities.Contains(CompatibilitySeverityTokens.Warning))
        {
            return CompatibilityOverall.Warning;
        }
        return CompatibilityOverall.Compatible;
    }

    /// <summary>Version of the canonical rule set as consumed here — bumped only if this PR's own wrapping (disabled-rule handling) changes, not by CompatibilityEvaluator's own internal rule logic.</summary>
    public const int RuleSetVersion = 1;

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
        CompatibilityOverall overall,
        IReadOnlyList<CompatibilityFindingDto> results,
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
                Guid.CreateVersion7(), buildListId, RuleSetVersion, settingsVersion,
                overall, inputHash, now);
            _dbContext.CompatibilityCheckRuns.Add(run);
            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var finding in results)
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
    /// is the same SettingCode reused across all rules and a SettingCode-only grouping would
    /// collapse them into one. <see cref="CompatibilityWarningSettingCodes"/> each belong to
    /// exactly one rule already, so this grouping doesn't change their behavior.
    /// </summary>
    internal async Task<(
        CompatibilityWarningSettings Settings,
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

        var defaultSettings = new CompatibilityWarningSettings(20m, 10m, 35m, 0, 0);
        if (currentRows.Count == 0)
        {
            return (defaultSettings, 1, new HashSet<string>(), rowVersionsByKey);
        }

        decimal ValueFor(string settingCode, decimal fallback)
        {
            var row = currentRows.FirstOrDefault(setting => setting.SettingCode == settingCode);
            return row?.DecimalValue ?? fallback;
        }

        var settings = new CompatibilityWarningSettings(
            ValueFor(CompatibilityWarningSettingCodes.GpuClearanceWarningMm, defaultSettings.GpuClearanceWarningMm),
            ValueFor(CompatibilityWarningSettingCodes.CoolerClearanceWarningMm, defaultSettings.CoolerClearanceWarningMm),
            ValueFor(CompatibilityWarningSettingCodes.PsuReserveWarningPercent, defaultSettings.PsuReserveWarningPercent),
            decimal.ToInt32(ValueFor(CompatibilityWarningSettingCodes.RemainingRamSlotWarningCount, defaultSettings.RemainingRamSlotWarningCount)),
            decimal.ToInt32(ValueFor(CompatibilityWarningSettingCodes.RemainingStoragePortWarningCount, defaultSettings.RemainingStoragePortWarningCount)));

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
