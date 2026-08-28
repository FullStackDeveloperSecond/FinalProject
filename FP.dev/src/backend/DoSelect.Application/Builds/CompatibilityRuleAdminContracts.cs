using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Auditing;

namespace DoSelect.Application.Builds;

/// <summary>
/// <see cref="RowVersion"/> is null when this rule's warning threshold has never been customized
/// (no <c>CompatibilityRuleSetting</c> row exists yet for this rule's tunable SettingCode) — the
/// next <see cref="UpdateWarningSettingRequest"/> for this rule must submit it back exactly
/// (still null for a genuine first write) per API 共通規範's "可編輯資源 Response 回 rowVersion
/// Base64；Update／Command Request 必須原樣帶回", and DEC-P311's API 契約收斂 for this settings API.
/// </summary>
public sealed record CompatibilityRuleWarningSettingDto(
    string SettingCode,
    decimal Value,
    decimal MinValue,
    decimal MaxValue,
    decimal DefaultValue,
    byte[]? RowVersion);

/// <summary>
/// A rule has at most one adjustable warning threshold (相容性規則後台設計.md's 5-row table) — the
/// other 7 hard-blocking rules carry a null <see cref="WarningSetting"/>. <see
/// cref="ActivationRowVersion"/> is null when this rule's activation state has never been changed
/// from its default (Active, no <c>CompatibilityRuleSetting</c> row yet for the IsActive
/// SettingCode) — mirrors <see cref="CompatibilityRuleWarningSettingDto.RowVersion"/>'s semantics
/// for the separate activation write.
/// </summary>
public sealed record CompatibilityRuleAdminDto(
    string RuleCode,
    bool IsActive,
    byte[]? ActivationRowVersion,
    CompatibilityRuleWarningSettingDto? WarningSetting);

/// <summary>
/// DEC-P309/DEC-P311: individual writes (<see cref="UpdateWarningSettingRequest"/>, <see
/// cref="SetRuleActivationRequest"/>) are now gated by each rule's own per-SettingCode RowVersion,
/// not this counter — <see cref="SettingsVersion"/> stays as a whole-ruleset reporting number
/// (still bumped by every successful write, still echoed on <see cref="CompatibilityCheckDto"/>/
/// <see cref="CompatibilityRuleTestResultDto"/> to say which generation of settings a check ran
/// against) but is no longer submitted back as a concurrency token by either write endpoint.
/// </summary>
public sealed record CompatibilityRuleListDto(
    IReadOnlyList<CompatibilityRuleAdminDto> Rules,
    int SettingsVersion);

public sealed record UpdateWarningSettingRequest(
    decimal Value,
    byte[]? RowVersion,
    [Required, StringLength(500, MinimumLength = 1)] string Reason);

/// <summary>
/// 相容性規則後台設計.md requires a double-confirm before a rule's activation flips — no API-level
/// two-phase protocol is specified for it (unlike the test endpoint's fully-worked request/
/// response example), so that confirmation is a frontend UX concern (a confirm dialog) and this
/// request carries only what the confirmed action itself needs. Field order/shape (isActive,
/// reason, rowVersion) matches DEC-P311 exactly.
/// </summary>
public sealed record SetRuleActivationRequest(
    bool IsActive,
    [Required, StringLength(500, MinimumLength = 1)] string Reason,
    byte[]? RowVersion);

public sealed record CompatibilityRuleTestRequest(
    [Required] IReadOnlyList<BuildItemInput> Items,
    IReadOnlyList<string>? RuleCodes,
    bool UseDraftSettings,
    IReadOnlyDictionary<string, decimal>? DraftWarningSettings);

public sealed record CompatibilityRuleTestResultDto(
    string Overall,
    IReadOnlyList<CompatibilityFindingDto> Results,
    int SettingsVersion,
    DateTime EvaluatedAtUtc);

public interface ICompatibilityRuleAdminService
{
    Task<CompatibilityRuleListDto> ListAsync(CancellationToken cancellationToken);

    Task<CompatibilityRuleAdminDto> UpdateWarningSettingAsync(
        string ruleCode,
        string adminUserId,
        UpdateWarningSettingRequest request,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);

    Task<CompatibilityRuleAdminDto> SetActivationAsync(
        string ruleCode,
        string adminUserId,
        SetRuleActivationRequest request,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);

    Task<CompatibilityRuleTestResultDto> TestAsync(
        CompatibilityRuleTestRequest request,
        string adminUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);
}
