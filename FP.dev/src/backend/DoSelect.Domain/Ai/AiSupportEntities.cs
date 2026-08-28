using DoSelect.Domain.Common;
using DoSelect.Domain.Members;

namespace DoSelect.Domain.Ai;

public enum AiConversationPurpose
{
    Support,
}

public enum AiConversationStatus
{
    Active,
    Closed,
}

public enum AiInteractionStatus
{
    Answered,
    Degraded,
}

public sealed class AiConversation : MutablePublicEntity
{
    private AiConversation()
    {
    }

    private AiConversation(
        Guid publicId,
        string memberUserId,
        SupportedLocale locale,
        int consentPolicyVersion,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (consentPolicyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consentPolicyVersion));
        }

        if (!Enum.IsDefined(locale))
        {
            throw new ArgumentOutOfRangeException(nameof(locale));
        }

        MemberUserId = RequireText(memberUserId, nameof(memberUserId));
        Purpose = AiConversationPurpose.Support;
        Locale = locale;
        Status = AiConversationStatus.Active;
        ConsentPolicyVersion = consentPolicyVersion;
        LastActivityAtUtc = createdAtUtc;
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
    }

    public string MemberUserId { get; private set; } = string.Empty;

    public long? SupportTicketId { get; private set; }

    public AiConversationPurpose Purpose { get; private set; }

    public SupportedLocale Locale { get; private set; }

    public AiConversationStatus Status { get; private set; }

    public int ConsentPolicyVersion { get; private set; }

    public DateTime LastActivityAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public static AiConversation StartSupport(
        Guid publicId,
        string memberUserId,
        SupportedLocale locale,
        int consentPolicyVersion,
        DateTime expiresAtUtc,
        DateTime createdAtUtc) =>
        new(
            publicId,
            memberUserId,
            locale,
            consentPolicyVersion,
            expiresAtUtc,
            createdAtUtc);

    public void RecordActivity(DateTime occurredAtUtc, DateTime expiresAtUtc)
    {
        if (Status != AiConversationStatus.Active)
        {
            throw new InvalidOperationException("A closed AI conversation cannot receive activity.");
        }

        LastActivityAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        MarkUpdated(occurredAtUtc);
    }
}

public sealed class AiInteraction : PublicEntity
{
    private AiInteraction()
    {
    }

    private AiInteraction(
        Guid publicId,
        long aiConversationId,
        int sequence,
        string userContentProtected,
        string? assistantContent,
        string model,
        string promptVersion,
        string schemaVersion,
        int inputTokens,
        int outputTokens,
        decimal estimatedCostUsd,
        AiInteractionStatus status,
        string? fallbackReason,
        int latencyMs,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (aiConversationId <= 0 || sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aiConversationId));
        }

        if (inputTokens < 0 || outputTokens < 0 || estimatedCostUsd < 0 || latencyMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        }

        AiConversationId = aiConversationId;
        Sequence = sequence;
        UserContentProtected = RequireBoundedText(userContentProtected, nameof(userContentProtected), 4_000);
        AssistantContent = OptionalBoundedText(assistantContent, nameof(assistantContent), 4_000);
        Model = RequireBoundedText(model, nameof(model), 100);
        PromptVersion = RequireBoundedText(promptVersion, nameof(promptVersion), 64);
        SchemaVersion = RequireBoundedText(schemaVersion, nameof(schemaVersion), 64);
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCostUsd = estimatedCostUsd;
        Status = status;
        FallbackReason = OptionalBoundedText(fallbackReason, nameof(fallbackReason), 64);
        LatencyMs = latencyMs;
    }

    public long? AiConversationId { get; private set; }

    public Guid? SearchPublicId { get; private set; }

    public int Sequence { get; private set; }

    public string UserContentProtected { get; private set; } = string.Empty;

    public string? AssistantContent { get; private set; }

    public string? IntentJson { get; private set; }

    public string Model { get; private set; } = string.Empty;

    public string PromptVersion { get; private set; } = string.Empty;

    public string SchemaVersion { get; private set; } = string.Empty;

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public decimal EstimatedCostUsd { get; private set; }

    public AiInteractionStatus Status { get; private set; }

    public string? FallbackReason { get; private set; }

    public int LatencyMs { get; private set; }

    public static AiInteraction RecordSupport(
        Guid publicId,
        long aiConversationId,
        int sequence,
        string userContentProtected,
        string? assistantContent,
        string model,
        string promptVersion,
        string schemaVersion,
        int inputTokens,
        int outputTokens,
        decimal estimatedCostUsd,
        AiInteractionStatus status,
        string? fallbackReason,
        int latencyMs,
        DateTime createdAtUtc) =>
        new(
            publicId,
            aiConversationId,
            sequence,
            userContentProtected,
            assistantContent,
            model,
            promptVersion,
            schemaVersion,
            inputTokens,
            outputTokens,
            estimatedCostUsd,
            status,
            fallbackReason,
            latencyMs,
            createdAtUtc);

    private static string RequireBoundedText(string value, string parameterName, int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? OptionalBoundedText(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Length <= maximumLength
            ? value.Trim()
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }
}

public sealed class AiCitation : Entity
{
    private AiCitation()
    {
    }

    public AiCitation(
        long aiInteractionId,
        string sourceType,
        Guid? sourcePublicId,
        string? sourceVersion,
        string label,
        int sortOrder,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (aiInteractionId <= 0 || sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aiInteractionId));
        }

        AiInteractionId = aiInteractionId;
        SourceType = RequireBoundedText(sourceType, nameof(sourceType), 32);
        SourcePublicId = sourcePublicId;
        SourceVersion = OptionalBoundedText(sourceVersion, nameof(sourceVersion), 64);
        Label = RequireBoundedText(label, nameof(label), 200);
        SortOrder = sortOrder;
    }

    public long AiInteractionId { get; private set; }

    public string SourceType { get; private set; } = string.Empty;

    public Guid? SourcePublicId { get; private set; }

    public string? SourceVersion { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public string? Url { get; private set; }

    public int SortOrder { get; private set; }

    private static string RequireBoundedText(string value, string parameterName, int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? OptionalBoundedText(string? value, string parameterName, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
}
