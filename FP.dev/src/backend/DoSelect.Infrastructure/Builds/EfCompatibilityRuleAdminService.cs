using System.Data;
using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Builds;
using DoSelect.Application.Common;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DoSelect.Infrastructure.Builds;

/// <summary>相容性規則後台設計.md: admin surface for the 5 adjustable warning thresholds, per-rule activation, and no-write rule testing.</summary>
public sealed class EfCompatibilityRuleAdminService : ICompatibilityRuleAdminService
{
    /// <summary>
    /// DEC-BATCH-026 (DEC-P309): a stable audit reason code, distinct from the free-text
    /// request.Reason an admin types (which cannot satisfy AuditFieldChange.RequireSafeCode's
    /// identifier-only format and belongs on CompatibilityRuleSetting.Reason instead, not on the
    /// central Audit Log's own reason column).
    /// </summary>
    private const string AuditReasonSettingChange = "compatibility_rule_setting_change";
    private const string AuditReasonTest = "compatibility_rule_test";

    private static readonly CompatibilityRuleCatalog RuleCatalog = CompatibilityRuleCatalog.CreateVersion1();

    private readonly DoSelectDbContext _dbContext;
    private readonly EfCompatibilityCheckService _compatibilityCheckService;
    private readonly ICompatibilityCatalogReader _catalogReader;
    private readonly IAuditWriter _auditWriter;

    public EfCompatibilityRuleAdminService(
        DoSelectDbContext dbContext,
        EfCompatibilityCheckService compatibilityCheckService,
        ICompatibilityCatalogReader catalogReader,
        IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _compatibilityCheckService = compatibilityCheckService;
        _catalogReader = catalogReader;
        _auditWriter = auditWriter;
    }

    public async Task<CompatibilityRuleListDto> ListAsync(CancellationToken cancellationToken)
    {
        var (settings, settingsVersion, disabledRuleCodes, rowVersionsByKey) =
            await _compatibilityCheckService.LoadCurrentSettingsAsync(cancellationToken);

        var rules = CompatibilityRuleCodes.All
            .Select(ruleCode => BuildRuleDto(ruleCode, settings, disabledRuleCodes, rowVersionsByKey))
            .ToList();

        return new CompatibilityRuleListDto(rules, settingsVersion);
    }

    public async Task<CompatibilityRuleAdminDto> UpdateWarningSettingAsync(
        string ruleCode,
        string adminUserId,
        UpdateWarningSettingRequest request,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireKnownRuleCode(ruleCode);

        var settingCode = CompatibilityRuleWarningSettingMap.TryGetSettingCode(ruleCode) ??
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"Rule '{ruleCode}' has no adjustable warning threshold.");

        if (!CompatibilityWarningSettingRanges.IsInRange(settingCode, request.Value))
        {
            var range = CompatibilityWarningSettingRanges.ByCode[settingCode];
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.CompatibilityThresholdOutOfRange,
                $"'{settingCode}' must be between {range.Min} and {range.Max}.");
        }

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);
        var now = DateTime.UtcNow;
        await InsertNextVersionRowAsync(
            ruleCode,
            settingCode,
            request.RowVersion,
            nextVersion => new CompatibilityRuleSetting(
                Guid.CreateVersion7(), ruleCode, settingCode, request.Value, null,
                nextVersion, request.Reason, adminUserId, now),
            (row, previousRow, previousGlobalVersion) => AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.CompatibilityRuleWarningSettingUpdate,
                AuditResourceTypes.CompatibilityRuleSetting,
                row.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("ruleCode", null, ruleCode),
                    AuditFieldChange.Code("settingCode", null, settingCode),
                    // 組長 PR #34 round-3 review: a rule's *effective* value before its first-ever
                    // customization is the program default, not "nothing" — recording null there
                    // would misreport what admins actually changed the threshold from.
                    AuditFieldChange.Code(
                        "value",
                        (previousRow?.DecimalValue ?? CompatibilityWarningSettingRanges.ByCode[settingCode].Default)
                            .ToString(CultureInfo.InvariantCulture),
                        request.Value.ToString(CultureInfo.InvariantCulture)),
                    // 組長 PR #34 round-3 review: Before must be the global SettingsVersion as it
                    // stood immediately before *this* write, not this key's own last-seen version
                    // — those diverge whenever some other rule was updated in between.
                    AuditFieldChange.Code(
                        "settingsVersion",
                        previousGlobalVersion.ToString(CultureInfo.InvariantCulture),
                        row.SettingsVersion.ToString(CultureInfo.InvariantCulture)),
                ],
                AuditReasonSettingChange,
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress),
            cancellationToken);

        var (settings, _, disabledRuleCodes, rowVersionsByKey) =
            await _compatibilityCheckService.LoadCurrentSettingsAsync(cancellationToken);
        return BuildRuleDto(ruleCode, settings, disabledRuleCodes, rowVersionsByKey);
    }

    public async Task<CompatibilityRuleAdminDto> SetActivationAsync(
        string ruleCode,
        string adminUserId,
        SetRuleActivationRequest request,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireKnownRuleCode(ruleCode);

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);
        var now = DateTime.UtcNow;
        await InsertNextVersionRowAsync(
            ruleCode,
            CompatibilityRuleActivationSettingCodes.IsActive,
            request.RowVersion,
            nextVersion => new CompatibilityRuleSetting(
                Guid.CreateVersion7(), ruleCode, CompatibilityRuleActivationSettingCodes.IsActive, null,
                request.IsActive, nextVersion, request.Reason, adminUserId, now),
            (row, previousRow, previousGlobalVersion) => AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.CompatibilityRuleActivationUpdate,
                AuditResourceTypes.CompatibilityRuleSetting,
                row.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("ruleCode", null, ruleCode),
                    AuditFieldChange.Code("settingCode", null, CompatibilityRuleActivationSettingCodes.IsActive),
                    // 組長 PR #34 round-3 review: a rule's effective activation state before its
                    // first-ever change is Active (the program default — every rule starts
                    // enabled), not "nothing".
                    AuditFieldChange.Code(
                        "isActive",
                        (previousRow?.BooleanValue ?? true).ToString(),
                        request.IsActive.ToString()),
                    AuditFieldChange.Code(
                        "settingsVersion",
                        previousGlobalVersion.ToString(CultureInfo.InvariantCulture),
                        row.SettingsVersion.ToString(CultureInfo.InvariantCulture)),
                ],
                AuditReasonSettingChange,
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress),
            cancellationToken);

        var (settings, _, disabledRuleCodes, rowVersionsByKey) =
            await _compatibilityCheckService.LoadCurrentSettingsAsync(cancellationToken);
        return BuildRuleDto(ruleCode, settings, disabledRuleCodes, rowVersionsByKey);
    }

    public async Task<CompatibilityRuleTestResultDto> TestAsync(
        CompatibilityRuleTestRequest request,
        string adminUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);
        var mergedItems = EfCompatibilityCheckService.MergeAndValidateItems(request.Items);

        HashSet<string>? onlyRuleCodes = null;
        if (request.RuleCodes is not null)
        {
            if (request.RuleCodes.Count > 20)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed, "At most 20 rule codes are allowed.");
            }

            var unknown = request.RuleCodes.Where(code => !CompatibilityRuleCodes.All.Contains(code)).ToList();
            if (unknown.Count > 0)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"Unknown rule code(s): {string.Join(", ", unknown)}.");
            }

            onlyRuleCodes = request.RuleCodes.ToHashSet();
        }

        var catalogResult = await _catalogReader.ReadAsync(
            mergedItems.Select(item => new CompatibilityItemReference(item.SkuPublicId, item.Quantity)).ToArray(),
            cancellationToken);
        if (catalogResult.MissingSkuPublicIds.Count > 0)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"Unknown SKU(s): {string.Join(", ", catalogResult.MissingSkuPublicIds)}.");
        }

        var (persistedSettings, settingsVersion, disabledRuleCodes, _) =
            await _compatibilityCheckService.LoadCurrentSettingsAsync(cancellationToken);

        var settings = request.UseDraftSettings
            ? ApplyDraftOverrides(persistedSettings, request.DraftWarningSettings)
            : persistedSettings;

        var evaluation = CompatibilityEvaluator.Evaluate(catalogResult.Components, settings, RuleCatalog);
        var (overall, results) = EfCompatibilityCheckService.ApplyDisabledRules(evaluation, disabledRuleCodes);
        if (onlyRuleCodes is not null)
        {
            // The canonical evaluator has no "only run these rules" mode (it always evaluates
            // everything), unlike this PR's own now-removed engine — filtering the *output*
            // findings after the fact is behaviorally equivalent for what this no-write test tool
            // reports, just without skipping the evaluation work for excluded rules.
            results = results.Where(finding => onlyRuleCodes.Contains(finding.RuleCode)).ToList();
        }

        var now = DateTime.UtcNow;

        // DEC-BATCH-026 (DEC-P309): the Run/Result snapshot and its central Audit Log entry share
        // one transaction — a failed audit write (e.g. a rejected safe-code) rolls the Run back
        // too, and a successful test leaves exactly one Audit row. Opening the transaction here
        // (rather than inside RecordRunAsync) lets RecordRunAsync detect the ambient transaction
        // and just participate in it instead of committing on its own.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var run = await _compatibilityCheckService.RecordRunAsync(
            mergedItems, null, settingsVersion, overall, results, now, cancellationToken);

        var overallToken = EfCompatibilityCheckService.OverallToken(overall);
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.CompatibilityRuleTest,
            AuditResourceTypes.CompatibilityCheckRun,
            run.PublicId,
            AuditResult.Success,
            errorCode: null,
            [
                // DEC-P310: the central Audit only ever holds the SHA-256 hash of the input SKU
                // set, the result summary, and the settings version — never the full product
                // content the hash was computed from.
                AuditFieldChange.Code("inputHash", null, Convert.ToHexString(run.InputHash)),
                AuditFieldChange.Code("overall", null, overallToken),
                AuditFieldChange.Code("settingsVersion", null, settingsVersion.ToString(CultureInfo.InvariantCulture)),
            ],
            AuditReasonTest,
            auditContext.CorrelationId,
            auditContext.TraceId,
            jobPublicId: null,
            auditContext.RemoteIpAddress));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CompatibilityRuleTestResultDto(overallToken, results, settingsVersion, now);
    }

    /// <summary>Mirrors InvoiceAllowanceWriter.ResolveActorAsync — the HTTP Policy already gates access, but the Audit actor still needs the admin's PublicId/current roles, and a defensive re-check catches a role revoked between session issuance and this call.</summary>
    private async Task<AuditActor> ResolveActorAsync(string adminUserId, CancellationToken cancellationToken)
    {
        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.CatalogManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw DomainProblemException.Forbidden(
                "The administrator no longer has permission to manage compatibility rules.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    private static void RequireKnownRuleCode(string ruleCode)
    {
        if (!CompatibilityRuleCodes.All.Contains(ruleCode))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ResourceNotFound, $"Rule '{ruleCode}' was not found.");
        }
    }

    /// <summary>
    /// A sys.sp_getapplock exclusive lock on a fixed resource still serializes every write to
    /// this table, so the read-previous-row-check-insert sequence below is atomic across any two
    /// admins racing — this is what keeps the global SettingsVersion counter (a reporting label,
    /// not the concurrency token) monotonic and gap-free even under concurrent writes to
    /// different keys. 組長 PR #34 round-3 review: the lock previously used @LockTimeout = 0
    /// (fail immediately), which meant two admins updating *different* rules at the same moment
    /// still had one spuriously get ConcurrencyConflict from the lock itself, before its own
    /// per-key RowVersion check — which would have passed — ever ran. Mirrors
    /// InvoiceAllowanceReader's own 5-second wait for the identical shape of problem: the second
    /// writer now waits for the first to finish (a few hundred ms at most) instead of being
    /// rejected outright, so two different-key writers can both succeed in the same request wave.
    /// A genuine same-key race still correctly conflicts — the loser's per-key RowVersion check
    /// (immediately below) catches it once it gets the lock.
    /// </summary>
    private const string SettingsLockResource = "compatibility-rule-settings";

    private static bool RowVersionsEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    /// <summary>
    /// DEC-P311: concurrency is now gated by the specific (RuleCode, SettingCode) row's own
    /// RowVersion — null means "no row exists yet for this key", itself a value the caller can be
    /// stale about (someone else's write since they last read null) — rather than the old
    /// cross-rule global SettingsVersion counter (round-2 review: MAX(SettingsVersion) let two
    /// admins updating *different* rules both "win"). <paramref name="createRow"/> receives the
    /// next global SettingsVersion (still bumped every write, now purely a reporting/audit
    /// generation label — see CompatibilityRuleListDto's doc comment) and
    /// <paramref name="createAuditRequest"/> receives the newly-inserted row plus whatever the
    /// previous row for this key was (null on a genuine first write) so the Audit entry can carry
    /// real Before／After values (DEC-P309).
    /// </summary>
    private async Task InsertNextVersionRowAsync(
        string ruleCode,
        string settingCode,
        byte[]? callerRowVersion,
        Func<int, CompatibilityRuleSetting> createRow,
        Func<CompatibilityRuleSetting, CompatibilityRuleSetting?, int, AuditWriteRequest> createAuditRequest,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        if (!await TryAcquireSettingsLockAsync(transaction, cancellationToken))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "The compatibility rule settings are being updated by someone else. Try again shortly.");
        }

        var previousRow = await _dbContext.CompatibilityRuleSettings
            .Where(setting => setting.RuleCode == ruleCode && setting.SettingCode == settingCode)
            .OrderByDescending(setting => setting.SettingsVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (!RowVersionsEqual(callerRowVersion, previousRow?.RowVersion))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "This setting was changed by someone else. Reload and try again.");
        }

        var (_, currentGlobalVersion, _, _) = await _compatibilityCheckService.LoadCurrentSettingsAsync(cancellationToken);
        var nextVersion = currentGlobalVersion + 1;

        // DEC-BATCH-026 (DEC-P309): row.PublicId is caller-assigned (Guid.CreateVersion7()), so
        // it's already known before SaveChanges — the setting row and its Audit Log entry are
        // added to the same DbContext and persisted together in one SaveChangesAsync, giving
        // exactly one Audit row per successful update and rolling both back together on failure.
        var row = createRow(nextVersion);
        _dbContext.CompatibilityRuleSettings.Add(row);
        _auditWriter.Add(createAuditRequest(row, previousRow, currentGlobalVersion));
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "This setting was changed by someone else. Reload and try again.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> TryAcquireSettingsLockAsync(
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        // 5-second wait, not fail-fast — mirrors InvoiceAllowanceReader's own lock. See
        // SettingsLockResource's doc comment for why this must wait rather than reject instantly.
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            SELECT @result;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = SettingsLockResource;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) >= 0;
    }

    private static CompatibilityRuleAdminDto BuildRuleDto(
        string ruleCode,
        CompatibilityWarningSettings settings,
        IReadOnlySet<string> disabledRuleCodes,
        IReadOnlyDictionary<(string RuleCode, string SettingCode), byte[]> rowVersionsByKey)
    {
        var settingCode = CompatibilityRuleWarningSettingMap.TryGetSettingCode(ruleCode);
        CompatibilityRuleWarningSettingDto? warningSetting = null;
        if (settingCode is not null)
        {
            var range = CompatibilityWarningSettingRanges.ByCode[settingCode];
            rowVersionsByKey.TryGetValue((ruleCode, settingCode), out var warningRowVersion);
            warningSetting = new CompatibilityRuleWarningSettingDto(
                settingCode, ValueFor(settingCode, settings), range.Min, range.Max, range.Default, warningRowVersion);
        }

        rowVersionsByKey.TryGetValue((ruleCode, CompatibilityRuleActivationSettingCodes.IsActive), out var activationRowVersion);
        return new CompatibilityRuleAdminDto(
            ruleCode, !disabledRuleCodes.Contains(ruleCode), activationRowVersion, warningSetting);
    }

    private static decimal ValueFor(string settingCode, CompatibilityWarningSettings settings) => settingCode switch
    {
        CompatibilityWarningSettingCodes.GpuClearanceWarningMm => settings.GpuClearanceWarningMm,
        CompatibilityWarningSettingCodes.CoolerClearanceWarningMm => settings.CoolerClearanceWarningMm,
        CompatibilityWarningSettingCodes.PsuReserveWarningPercent => settings.PsuReserveWarningPercent,
        CompatibilityWarningSettingCodes.RemainingRamSlotWarningCount => settings.RemainingRamSlotWarningCount,
        CompatibilityWarningSettingCodes.RemainingStoragePortWarningCount => settings.RemainingStoragePortWarningCount,
        _ => throw new ArgumentOutOfRangeException(nameof(settingCode)),
    };

    private static CompatibilityWarningSettings ApplyDraftOverrides(
        CompatibilityWarningSettings baseline,
        IReadOnlyDictionary<string, decimal>? draftWarningSettings)
    {
        if (draftWarningSettings is null || draftWarningSettings.Count == 0)
        {
            return baseline;
        }

        var gpuClearance = baseline.GpuClearanceWarningMm;
        var coolerClearance = baseline.CoolerClearanceWarningMm;
        var psuReserve = baseline.PsuReserveWarningPercent;
        var remainingRamSlots = baseline.RemainingRamSlotWarningCount;
        var remainingStoragePorts = baseline.RemainingStoragePortWarningCount;

        foreach (var (settingCode, value) in draftWarningSettings)
        {
            if (!CompatibilityWarningSettingRanges.IsInRange(settingCode, value))
            {
                var isKnownCode = CompatibilityWarningSettingRanges.ByCode.ContainsKey(settingCode);
                throw new BuildWriteException(
                    isKnownCode
                        ? BuildWriteException.ErrorCodes.CompatibilityThresholdOutOfRange
                        : BuildWriteException.ErrorCodes.ValidationFailed,
                    isKnownCode
                        ? $"'{settingCode}' is outside its safe range."
                        : $"Unknown draft setting code '{settingCode}'.");
            }

            switch (settingCode)
            {
                case CompatibilityWarningSettingCodes.GpuClearanceWarningMm: gpuClearance = value; break;
                case CompatibilityWarningSettingCodes.CoolerClearanceWarningMm: coolerClearance = value; break;
                case CompatibilityWarningSettingCodes.PsuReserveWarningPercent: psuReserve = value; break;
                case CompatibilityWarningSettingCodes.RemainingRamSlotWarningCount: remainingRamSlots = decimal.ToInt32(value); break;
                case CompatibilityWarningSettingCodes.RemainingStoragePortWarningCount: remainingStoragePorts = decimal.ToInt32(value); break;
            }
        }

        return new CompatibilityWarningSettings(
            gpuClearance, coolerClearance, psuReserve, remainingRamSlots, remainingStoragePorts);
    }
}
