using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Configuration;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.Ai;

[ApiController]
[Route("api/v1/ai/support/messages")]
[Authorize(Policy = DoSelectPolicies.AiSupportMember)]
public sealed class AiSupportController : ControllerBase
{
    private const string DisclaimerKey = "ai.support.answerDisclaimer";
    private readonly AiSupportOrchestrator _orchestrator;
    private readonly FeatureOptions _features;

    public AiSupportController(
        AiSupportOrchestrator orchestrator,
        IOptions<FeatureOptions> features)
    {
        _orchestrator = orchestrator;
        _features = features.Value;
    }

    [HttpPost]
    [ProducesResponseType<AiSupportAnswerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateMessage(
        AiSupportMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (!_features.AiEnabled)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.AiServiceUnavailable);
        }

        if (request.ConversationPublicId == Guid.Empty ||
            request.ReferencedOrderPublicIds.Any(publicId => publicId == Guid.Empty) ||
            request.ReferencedSupportTicketPublicIds.Any(publicId => publicId == Guid.Empty) ||
            request.ReferencedOrderPublicIds.Distinct().Count() != request.ReferencedOrderPublicIds.Length ||
            request.ReferencedSupportTicketPublicIds.Distinct().Count() != request.ReferencedSupportTicketPublicIds.Length)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed);
        }

        var rawMemberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawMemberId, out var memberId))
        {
            return Problem(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthenticationRequired);
        }

        var interactionPublicId = Guid.NewGuid();
        var result = await _orchestrator.ExecuteAsync(
            new AiSupportExecutionRequest(
                memberId,
                interactionPublicId,
                request.ConversationPublicId,
                request.Message,
                ParseLocale(request.Locale),
                request.ReferencedOrderPublicIds,
                request.ReferencedSupportTicketPublicIds),
            cancellationToken);

        if (result.Status == AiSupportExecutionStatus.Rejected)
        {
            return MapRejection(result.Reason);
        }

        if (result.Answer is null ||
            result.Answer.Length > 4000 ||
            result.ConversationPublicId is null ||
            result.InteractionPublicId is null)
        {
            return Problem(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.AiOutputInvalid);
        }

        return Ok(new AiSupportAnswerDto(
            result.ConversationPublicId.Value,
            result.InteractionPublicId.Value,
            result.Answer,
            result.Citations.Select(MapCitation).ToArray(),
            AiSupportResultCodes.Answered,
            AiDegradationModes.None,
            DisclaimerKey,
            new AiSupportUsageDto(
                result.RemainingDailyMessages,
                result.ResetAtUtc)));
    }

    private static AiSupportCitationDto MapCitation(AiSupportCitation citation) =>
        new(
            citation.SourceType,
            citation.Title,
            Guid.TryParse(citation.SourceId, out var publicId) ? publicId : null,
            Url: null);

    private IActionResult MapRejection(AiSafetyReason reason) => reason switch
    {
        AiSafetyReason.ConsentRequired or AiSafetyReason.ConsentDenied =>
            Problem(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.AiConsentRequired),
        AiSafetyReason.DailyQuotaExceeded =>
            Problem(
                StatusCodes.Status429TooManyRequests,
                ApiErrorCodes.AiUsageLimitExceeded),
        AiSafetyReason.BudgetProtectionActive =>
            Problem(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.AiBudgetProtectionActive),
        AiSafetyReason.SecretDetected or AiSafetyReason.PersonalDataDetected =>
            Problem(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                "The message contains sensitive content that cannot be processed by AI."),
        AiSafetyReason.ResourceOwnershipMismatch =>
            Problem(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.AiOrderAccessDenied),
        _ =>
            Problem(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.AiServiceUnavailable),
    };

    private static SupportedLocale ParseLocale(string locale) => locale switch
    {
        "zh-TW" => SupportedLocale.ZhTw,
        "ja-JP" => SupportedLocale.JaJp,
        "ko-KR" => SupportedLocale.KoKr,
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private ObjectResult Problem(int statusCode, string code, string? detail = null)
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            statusCode,
            code,
            detail: detail);
        var result = new ObjectResult(problem)
        {
            StatusCode = statusCode,
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
