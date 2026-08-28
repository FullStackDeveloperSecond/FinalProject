using System.ComponentModel.DataAnnotations;

namespace DoSelect.Api.Ai;

public static class AiSupportResultCodes
{
    public const string Answered = "answered";
    public const string SafeRejection = "safe_rejection";
    public const string Degraded = "degraded";
}

public static class AiDegradationModes
{
    public const string None = "none";
    public const string KeywordSearch = "keywordSearch";
    public const string CreateSupportTicket = "createSupportTicket";
}

public sealed class AiSupportMessageRequest
{
    public Guid? ConversationPublicId { get; init; }

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    [RegularExpression(@"(?s)^(?=.*\S).+$")]
    public string Message { get; init; } = string.Empty;

    [Required]
    [MaxLength(3)]
    public Guid[] ReferencedOrderPublicIds { get; init; } = [];

    [Required]
    [MaxLength(3)]
    public Guid[] ReferencedSupportTicketPublicIds { get; init; } = [];

    [Required]
    [RegularExpression("^(zh-TW|ja-JP|ko-KR)$")]
    public string Locale { get; init; } = string.Empty;
}

public sealed record AiSupportCitationDto(
    string Type,
    string Label,
    Guid? ResourcePublicId,
    string? Url);

public sealed record AiSupportUsageDto(
    int RemainingRequests,
    DateTimeOffset ResetAtUtc);

public sealed record AiSupportAnswerDto(
    Guid ConversationPublicId,
    Guid InteractionPublicId,
    string Answer,
    IReadOnlyList<AiSupportCitationDto> Citations,
    string ResultCode,
    string DegradationMode,
    string DisclaimerKey,
    AiSupportUsageDto Usage);

public sealed class AiConsentRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int PolicyVersion { get; init; }

    [Required]
    [RegularExpression("^(zh-TW|ja-JP|ko-KR)$")]
    public string Locale { get; init; } = string.Empty;

    [Required]
    [Range(typeof(bool), "true", "true")]
    public bool Accepted { get; init; }
}

public sealed record AiConsentStatusDto(
    string State,
    int PolicyVersion,
    string? Locale,
    DateTimeOffset? DecidedAtUtc);

public sealed record AiUsageDto(
    string Feature,
    int UsedRequests,
    int RequestLimit,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset ResetAtUtc,
    bool BudgetWarningActive,
    bool BudgetProtectionActive);

public sealed record AdminAiUsageRowDto(
    string Feature,
    string Model,
    string Status,
    int InteractionCount,
    int InputTokens,
    int OutputTokens,
    decimal? EstimatedCostUsd);

public sealed record AdminAiUsageReportDto(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<AdminAiUsageRowDto> Rows,
    decimal? CumulativeCostUsd,
    bool BudgetWarningActive,
    bool BudgetProtectionActive,
    DateTimeOffset DataAsOfUtc);
