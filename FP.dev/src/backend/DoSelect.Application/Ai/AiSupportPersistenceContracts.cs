using DoSelect.Domain.Members;

namespace DoSelect.Application.Ai;

public static class AiBudgetAlertNotificationContract
{
    public const string TemplateKey = "ai.budget.warning";
    public const string RecipientPurpose = "ai.budget.owner";
    public const string ResourceType = "AdminUser";
    public const string Locale = "zh-TW";
    public const int ParameterSetVersion = 1;
}

public sealed record AiConsentSnapshot(
    AiConsentState State,
    int PolicyVersion,
    SupportedLocale? Locale,
    DateTimeOffset? DecidedAtUtc);

public interface IAiConsentManager
{
    Task<AiConsentSnapshot> ReadCurrentAsync(Guid memberId, CancellationToken cancellationToken);

    Task<AiConsentSnapshot> GrantAsync(
        Guid memberId,
        int policyVersion,
        SupportedLocale locale,
        CancellationToken cancellationToken);

    Task<AiConsentSnapshot> WithdrawAsync(Guid memberId, CancellationToken cancellationToken);
}

public sealed record AiMemberUsageSnapshot(
    int UsedRequests,
    int RequestLimit,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset ResetAtUtc,
    bool BudgetWarningActive,
    bool BudgetProtectionActive);

public interface IAiMemberUsageReader
{
    Task<AiMemberUsageSnapshot?> ReadSupportUsageAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}

public sealed record AiAdminUsageRow(
    string Feature,
    string Model,
    string Status,
    int InteractionCount,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd);

public sealed record AiAdminUsageSnapshot(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<AiAdminUsageRow> Rows,
    decimal CumulativeCostUsd,
    bool BudgetWarningActive,
    bool BudgetProtectionActive,
    DateTimeOffset DataAsOfUtc);

public interface IAiAdminUsageReader
{
    Task<AiAdminUsageSnapshot?> ReadAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

public sealed record AiSupportInteractionWrite(
    Guid MemberId,
    Guid? ConversationPublicId,
    Guid InteractionPublicId,
    string UserMessage,
    SupportedLocale Locale,
    string? Answer,
    IReadOnlyList<AiSupportCitation> Citations,
    AiSupportModelUsage? ModelUsage,
    bool IsDegraded,
    string? FallbackReason,
    int LatencyMs);

public sealed record AiSupportInteractionWriteResult(
    bool Succeeded,
    Guid ConversationPublicId);

public interface IAiSupportInteractionStore
{
    Task<AiSupportInteractionWriteResult> SaveAsync(
        AiSupportInteractionWrite interaction,
        CancellationToken cancellationToken);
}
