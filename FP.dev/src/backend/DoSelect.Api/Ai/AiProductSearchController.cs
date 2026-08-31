using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DoSelect.Api.Common;
using DoSelect.Api.Configuration;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.Ai;

[ApiController]
[Route("api/v1/ai/product-search/recommendations")]
public sealed class AiProductSearchController(
    AiProductSearchOrchestrator orchestrator,
    IAiProductSearchCatalog catalog,
    IOptions<FeatureOptions> features,
    IOptions<OpenAiResponsesOptions> openAiOptions,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string BrowserCookieName = ".DoSelect.AiBrowser";
    private const string DisclaimerKey = "ai.productSearch.recommendationDisclaimer";

    [HttpPost]
    [ProducesResponseType<AiProductSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Search(
        AiProductSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExistingParts.Any(part =>
                part.SkuPublicId == Guid.Empty ||
                part.Specifications.Any(spec => string.IsNullOrWhiteSpace(spec.SemanticKey))))
        {
            return Problem(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationFailed);
        }

        if (!features.Value.AiEnabled)
        {
            var fallback = await catalog.KeywordFallbackAsync(
                request.Message,
                ParseLocale(request.Locale),
                cancellationToken);
            return Ok(CreateDegradedResult(
                Guid.NewGuid(),
                fallback,
                remainingRequests: 0,
                resetAtUtc: DateTimeOffset.UtcNow.AddDays(1)));
        }

        var actor = await ResolveActorAsync(cancellationToken);
        var searchPublicId = Guid.NewGuid();
        var result = await orchestrator.ExecuteAsync(
            new AiProductSearchExecutionRequest(
                searchPublicId,
                actor,
                request.Message.Trim(),
                ParseLocale(request.Locale),
                request.ExistingParts.Select(MapExistingPart).ToArray()),
            cancellationToken);
        if (result.Status == AiProductSearchExecutionStatus.Rejected)
        {
            return MapRejection(result.Reason);
        }

        return Ok(new AiProductSearchResultDto(
            searchPublicId,
            ToResultCode(result.Status),
            result.Fallback == AiFallback.KeywordSearch
                ? AiDegradationModes.KeywordSearch
                : AiDegradationModes.None,
            result.Intent is null ? null : MapIntent(result.Intent),
            result.Clarifications,
            result.Recommendations.Select(MapRecommendation).ToArray(),
            result.FallbackProducts,
            DisclaimerKey,
            new AiProductSearchUsageDto(
                result.RemainingDailyRequests,
                result.ResetAtUtc),
            result.CustomBuild is null ? null : MapCustomBuild(result.CustomBuild)));
    }

    private async Task<AiProductSearchActor> ResolveActorAsync(CancellationToken cancellationToken)
    {
        var member = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (member.Succeeded && member.Principal is not null)
        {
            var memberUserId = member.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(memberUserId))
            {
                return new AiProductSearchActor(memberUserId, null, IsDemoAllowlisted: false);
            }
        }

        var browserId = ReadOrIssueBrowserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = Encoding.UTF8.GetBytes(openAiOptions.Value.AnonymousIdentityPepper);
        var material = Encoding.UTF8.GetBytes($"ai-product-search:{ipAddress}:{browserId:D}");
        var hash = HMACSHA256.HashData(key, material);
        return new AiProductSearchActor(
            MemberUserId: null,
            hash,
            openAiOptions.Value.DemoBrowserIds.Contains(browserId));
    }

    private Guid ReadOrIssueBrowserId()
    {
        if (Request.Cookies.TryGetValue(BrowserCookieName, out var raw) &&
            Guid.TryParse(raw, out var existing) && existing != Guid.Empty)
        {
            return existing;
        }

        var browserId = Guid.NewGuid();
        Response.Cookies.Append(
            BrowserCookieName,
            browserId.ToString("D"),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = !environment.IsDevelopment(),
                MaxAge = TimeSpan.FromDays(30),
                Path = "/",
            });
        return browserId;
    }

    private IActionResult MapRejection(AiSafetyReason reason) => reason switch
    {
        AiSafetyReason.DailyQuotaExceeded =>
            Problem(StatusCodes.Status429TooManyRequests, ApiErrorCodes.AiUsageLimitExceeded),
        AiSafetyReason.SecretDetected or AiSafetyReason.PersonalDataDetected =>
            Problem(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                "The search request contains sensitive content that cannot be sent to AI."),
        AiSafetyReason.BudgetProtectionActive =>
            Problem(StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.AiBudgetProtectionActive),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.AiServiceUnavailable),
    };

    private ObjectResult Problem(int statusCode, string code, string? detail = null)
    {
        var problem = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code, detail: detail);
        var response = new ObjectResult(problem) { StatusCode = statusCode };
        response.ContentTypes.Add("application/problem+json");
        return response;
    }

    private static AiProductSearchExistingPart MapExistingPart(AiExistingPartRequest part) =>
        new(
            part.SkuPublicId,
            part.SourceType,
            part.CategoryCode,
            part.DisplayName,
            part.Specifications.Select(spec => new AiRequiredSpec(
                spec.SemanticKey,
                spec.Operator,
                spec.Value,
                spec.Unit)).ToArray(),
            part.Quantity,
            part.ConfirmedByUser);

    private static AiProductSearchIntentDto MapIntent(AiProductSearchIntent intent) =>
        new(
            intent.Intent.ToString(),
            intent.Purposes,
            intent.Budget?.Minimum,
            intent.Budget?.Maximum,
            intent.Keyword,
            intent.CategoryCode,
            intent.PreferredBrandCodes,
            intent.ExcludedBrandCodes,
            intent.RequiredSpecs.Select(spec => new AiSearchSpecDto(
                spec.SemanticKey,
                spec.Operator,
                spec.Value,
                spec.Unit)).ToArray(),
            intent.Preferences,
            intent.ProposedExistingParts.Select(part => new AiProposedExistingPartDto(
                part.CategoryCode,
                part.DisplayName,
                part.Quantity,
                part.Specifications.Select(spec => new AiSearchSpecDto(
                    spec.SemanticKey,
                    spec.Operator,
                    spec.Value,
                    spec.Unit)).ToArray())).ToArray());

    private static AiProductRecommendationDto MapRecommendation(AiProductSearchRecommendation item) =>
        new(
            item.Product,
            item.Reason,
            item.CompatibilityStatus.ToString(),
            item.CompatibilityMessageKeys);

    private static AiCustomBuildRecommendationDto MapCustomBuild(AiCustomBuildRecommendation build) =>
        new(
            build.Components.Select(component => new AiCustomBuildComponentDto(
                component.Product,
                component.SkuPublicId,
                component.SourceType,
                component.CategoryCode,
                component.DisplayName,
                component.Quantity,
                component.IsExistingPart,
                component.Reason)).ToArray(),
            build.PurchaseSubtotal,
            build.AssemblyFee,
            build.PurchaseTotal,
            build.Currency,
            build.CompatibilityStatus.ToString(),
            build.CompatibilityMessageKeys);

    private static string ToResultCode(AiProductSearchExecutionStatus status) => status switch
    {
        AiProductSearchExecutionStatus.Recommendations => "recommendations",
        AiProductSearchExecutionStatus.Clarification => "clarification",
        AiProductSearchExecutionStatus.NoResults => "noResults",
        AiProductSearchExecutionStatus.Degraded => "degraded",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static SupportedLocale ParseLocale(string locale) => locale switch
    {
        "zh-TW" => SupportedLocale.ZhTw,
        "ja-JP" => SupportedLocale.JaJp,
        "ko-KR" => SupportedLocale.KoKr,
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static AiProductSearchResultDto CreateDegradedResult(
        Guid searchPublicId,
        IReadOnlyList<ProductCardDto> products,
        int remainingRequests,
        DateTimeOffset resetAtUtc) =>
        new(
            searchPublicId,
            "degraded",
            AiDegradationModes.KeywordSearch,
            Intent: null,
            Clarifications: [],
            Recommendations: [],
            products,
            DisclaimerKey,
            new AiProductSearchUsageDto(remainingRequests, resetAtUtc),
            CustomBuild: null);
}

public sealed class AiProductSearchRequest
{
    [Required]
    [StringLength(2_000, MinimumLength = 1)]
    [RegularExpression(@"(?s)^(?=.*\S).+$")]
    public string Message { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(zh-TW|ja-JP|ko-KR)$")]
    public string Locale { get; init; } = string.Empty;

    [Required]
    [MaxLength(12)]
    public AiExistingPartRequest[] ExistingParts { get; init; } = [];
}

public sealed class AiExistingPartRequest : IValidatableObject
{
    [Required]
    [RegularExpression("^(catalogSku|structuredManual)$")]
    public string SourceType { get; init; } = string.Empty;

    public Guid? SkuPublicId { get; init; }

    [StringLength(64)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")]
    public string? CategoryCode { get; init; }

    [StringLength(160)]
    public string? DisplayName { get; init; }

    [Required]
    [MaxLength(12)]
    public AiSearchSpecRequest[] Specifications { get; init; } = [];

    [Range(1, 8)]
    public int Quantity { get; init; } = 1;

    [Range(typeof(bool), "true", "true")]
    public bool ConfirmedByUser { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SourceType == "catalogSku")
        {
            if (SkuPublicId is null || SkuPublicId == Guid.Empty)
            {
                yield return new ValidationResult(
                    "A catalog SKU public ID is required.",
                    [nameof(SkuPublicId)]);
            }

            if (CategoryCode is not null || DisplayName is not null || Specifications.Length > 0)
            {
                yield return new ValidationResult(
                    "Catalog SKU input cannot override category, display name, or specifications.",
                    [nameof(CategoryCode), nameof(DisplayName), nameof(Specifications)]);
            }
        }
        else if (SourceType == "structuredManual")
        {
            if (SkuPublicId is not null)
            {
                yield return new ValidationResult(
                    "Structured manual input cannot reference a catalog SKU.",
                    [nameof(SkuPublicId)]);
            }

            if (string.IsNullOrWhiteSpace(CategoryCode) || string.IsNullOrWhiteSpace(DisplayName) ||
                Specifications.Length == 0)
            {
                yield return new ValidationResult(
                    "Structured manual input requires category, display name, and specifications.",
                    [nameof(CategoryCode), nameof(DisplayName), nameof(Specifications)]);
            }
        }
    }
}

public sealed class AiSearchSpecRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression("^[a-z0-9][a-z0-9._-]{0,63}$")]
    public string SemanticKey { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(eq|gte|lte|in)$")]
    public string Operator { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Value { get; init; } = string.Empty;

    [StringLength(16)]
    public string? Unit { get; init; }
}

public sealed record AiSearchSpecDto(string SemanticKey, string Operator, string Value, string? Unit);
public sealed record AiProposedExistingPartDto(
    string CategoryCode,
    string DisplayName,
    int Quantity,
    IReadOnlyList<AiSearchSpecDto> Specifications);
public sealed record AiProductSearchIntentDto(
    string Intent,
    IReadOnlyList<string> Purposes,
    decimal? MinimumBudget,
    decimal? MaximumBudget,
    string? Keyword,
    string? CategoryCode,
    IReadOnlyList<string> PreferredBrandCodes,
    IReadOnlyList<string> ExcludedBrandCodes,
    IReadOnlyList<AiSearchSpecDto> RequiredSpecs,
    IReadOnlyList<string> Preferences,
    IReadOnlyList<AiProposedExistingPartDto> ProposedExistingParts);
public sealed record AiProductRecommendationDto(
    ProductCardDto Product,
    string Reason,
    string CompatibilityStatus,
    IReadOnlyList<string> CompatibilityMessageKeys);
public sealed record AiCustomBuildComponentDto(
    ProductCardDto? Product,
    Guid? SkuPublicId,
    string SourceType,
    string CategoryCode,
    string DisplayName,
    int Quantity,
    bool IsExistingPart,
    string? Reason);
public sealed record AiCustomBuildRecommendationDto(
    IReadOnlyList<AiCustomBuildComponentDto> Components,
    decimal PurchaseSubtotal,
    decimal AssemblyFee,
    decimal PurchaseTotal,
    string Currency,
    string CompatibilityStatus,
    IReadOnlyList<string> CompatibilityMessageKeys);
public sealed record AiProductSearchUsageDto(int RemainingRequests, DateTimeOffset ResetAtUtc);
public sealed record AiProductSearchResultDto(
    Guid SearchPublicId,
    string ResultType,
    string DegradationMode,
    AiProductSearchIntentDto? Intent,
    IReadOnlyList<string> Clarifications,
    IReadOnlyList<AiProductRecommendationDto> Recommendations,
    IReadOnlyList<ProductCardDto> FallbackProducts,
    string DisclaimerKey,
    AiProductSearchUsageDto Usage,
    AiCustomBuildRecommendationDto? CustomBuild = null);
