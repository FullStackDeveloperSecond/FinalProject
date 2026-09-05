using DoSelect.Application.Catalog;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Ai;

public enum AiProductSearchIntentType
{
    SingleProduct,
    PrebuiltComputer,
    CustomBuild,
}

public sealed record AiProductSearchExistingPart(
    Guid? SkuPublicId,
    string SourceType,
    string? CategoryCode,
    string? DisplayName,
    IReadOnlyList<AiRequiredSpec> Specifications,
    int Quantity,
    bool ConfirmedByUser);

public sealed record AiProductSearchProposedPart(
    string CategoryCode,
    string DisplayName,
    IReadOnlyList<AiRequiredSpec> Specifications,
    int Quantity);

public sealed record AiProductSearchIntent(
    AiProductSearchIntentType Intent,
    IReadOnlyList<string> Purposes,
    AiBudgetRange? Budget,
    string? Keyword,
    string? CategoryCode,
    IReadOnlyList<string> PreferredBrandCodes,
    IReadOnlyList<string> ExcludedBrandCodes,
    IReadOnlyList<AiRequiredSpec> RequiredSpecs,
    IReadOnlyList<string> Preferences,
    IReadOnlyList<AiProductSearchProposedPart> ProposedExistingParts,
    IReadOnlyList<string> Clarifications);

public sealed record AiProductSearchMetadata(
    IReadOnlyList<string> CategoryCodes,
    IReadOnlyList<string> BrandCodes,
    IReadOnlyList<string> SemanticKeys);

public enum AiProductSearchModelStatus
{
    Completed,
    Unavailable,
    InvalidOutput,
}

public sealed record AiProductSearchIntentResult(
    AiProductSearchModelStatus Status,
    AiProductSearchIntent? Intent,
    AiSupportModelUsage? Usage,
    string? ValidationFailureCode = null,
    string? ValidationFailureField = null);

public sealed record AiProductRecommendationReason(
    Guid SkuPublicId,
    string Reason);

public sealed record AiProductSearchExplanationResult(
    AiProductSearchModelStatus Status,
    IReadOnlyList<AiProductRecommendationReason> Reasons,
    AiSupportModelUsage? Usage);

public interface IAiProductSearchModelClient
{
    Task<AiProductSearchIntentResult> ParseIntentAsync(
        string message,
        SupportedLocale locale,
        AiProductSearchMetadata metadata,
        CancellationToken cancellationToken);

    Task<AiProductSearchExplanationResult> ExplainAsync(
        AiProductSearchIntent intent,
        IReadOnlyList<ProductCardDto> approvedCandidates,
        SupportedLocale locale,
        CancellationToken cancellationToken);
}

public enum AiCompatibilityStatus
{
    NotRequired,
    Compatible,
    Warning,
}

public sealed record AiProductSearchCandidate(
    ProductCardDto Product,
    AiCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> CompatibilityMessageKeys);

public sealed record AiCustomBuildComponentCandidate(
    ProductCardDto? Product,
    Guid? SkuPublicId,
    string SourceType,
    string CategoryCode,
    string DisplayName,
    int Quantity,
    bool IsExistingPart);

public sealed record AiCustomBuildCandidate(
    IReadOnlyList<AiCustomBuildComponentCandidate> Components,
    decimal PurchaseSubtotal,
    decimal AssemblyFee,
    decimal PurchaseTotal,
    string Currency,
    AiCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> CompatibilityMessageKeys);

public static class AiCustomBuildPricing
{
    public const decimal AssemblyFee = 300m;
}

public sealed record AiProductSearchCandidateResult(
    bool IsValid,
    AiSafetyReason Reason,
    IReadOnlyList<AiProductSearchCandidate> Candidates,
    IReadOnlyList<string> Clarifications,
    AiCustomBuildCandidate? CustomBuild = null);

public interface IAiProductSearchCatalog
{
    Task<AiProductSearchMetadata> ReadMetadataAsync(CancellationToken cancellationToken);

    Task<AiProductSearchCandidateResult> FindCandidatesAsync(
        AiProductSearchIntent intent,
        IReadOnlyList<AiProductSearchExistingPart> existingParts,
        SupportedLocale locale,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductCardDto>> KeywordFallbackAsync(
        string message,
        SupportedLocale locale,
        CancellationToken cancellationToken);
}

public sealed record AiProductSearchActor(
    string? MemberUserId,
    byte[]? AnonymousSessionKeyHash,
    bool IsDemoAllowlisted)
{
    public bool IsMember => MemberUserId is not null;
}

public sealed record AiProductSearchAccessState(
    int RemainingDailyRequests,
    DateTimeOffset ResetAtUtc,
    bool BudgetProtectionActive,
    bool IsDemoAllowlisted);

public sealed record AiProductSearchReservationResult(
    bool IsReserved,
    AiProductSearchAccessState State);

public interface IAiProductSearchAdmissionGate
{
    Task<AiProductSearchAccessState> ReadAsync(
        AiProductSearchActor actor,
        CancellationToken cancellationToken);

    Task<AiProductSearchReservationResult> TryReserveAsync(
        AiProductSearchActor actor,
        Guid requestPublicId,
        CancellationToken cancellationToken);
}

public sealed record AiProductSearchInteractionWrite(
    Guid SearchPublicId,
    string UserMessage,
    AiProductSearchIntent? Intent,
    string? AssistantContent,
    AiSupportModelUsage? Usage,
    bool IsDegraded,
    string? FallbackReason,
    int LatencyMs);

public interface IAiProductSearchInteractionStore
{
    Task<bool> SaveAsync(
        AiProductSearchInteractionWrite interaction,
        CancellationToken cancellationToken);
}

public enum AiProductSearchExecutionStatus
{
    Recommendations,
    Clarification,
    NoResults,
    Degraded,
    Rejected,
}

public sealed record AiProductSearchRecommendation(
    ProductCardDto Product,
    string Reason,
    AiCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> CompatibilityMessageKeys);

public sealed record AiCustomBuildRecommendationComponent(
    ProductCardDto? Product,
    Guid? SkuPublicId,
    string SourceType,
    string CategoryCode,
    string DisplayName,
    int Quantity,
    bool IsExistingPart,
    string? Reason);

public sealed record AiCustomBuildRecommendation(
    IReadOnlyList<AiCustomBuildRecommendationComponent> Components,
    decimal PurchaseSubtotal,
    decimal AssemblyFee,
    decimal PurchaseTotal,
    string Currency,
    AiCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> CompatibilityMessageKeys);

public sealed record AiProductSearchExecutionRequest(
    Guid SearchPublicId,
    AiProductSearchActor Actor,
    string Message,
    SupportedLocale Locale,
    IReadOnlyList<AiProductSearchExistingPart> ExistingParts);

public sealed record AiProductSearchExecutionResult(
    AiProductSearchExecutionStatus Status,
    AiSafetyReason Reason,
    AiFallback Fallback,
    AiProductSearchIntent? Intent,
    IReadOnlyList<string> Clarifications,
    IReadOnlyList<AiProductSearchRecommendation> Recommendations,
    IReadOnlyList<ProductCardDto> FallbackProducts,
    int RemainingDailyRequests,
    DateTimeOffset ResetAtUtc,
    AiCustomBuildRecommendation? CustomBuild = null);
