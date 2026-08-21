using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Ai;

[ApiController]
[Route("api/v1/ai/support/messages")]
[Authorize(Policy = DoSelectPolicies.Member)]
public sealed class AiSupportController : ControllerBase
{
    private const string DisclaimerKey = "ai.support.answerDisclaimer";
    private readonly AiSupportOrchestrator _orchestrator;

    public AiSupportController(AiSupportOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
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
        var rawMemberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawMemberId, out var memberId))
        {
            return Problem(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthenticationRequired);
        }

        var result = await _orchestrator.ExecuteAsync(
            new AiSupportExecutionRequest(memberId, request.Message, DataItems: []),
            cancellationToken);

        if (result.Status == AiSupportExecutionStatus.Rejected)
        {
            return MapRejection(result.Reason);
        }

        if (result.Answer is null || result.Answer.Length > 4000)
        {
            return Problem(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.AiOutputInvalid);
        }

        return Ok(new AiSupportAnswerDto(
            request.ConversationPublicId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            result.Answer,
            Citations: [],
            AiSupportResultCodes.Answered,
            AiDegradationModes.None,
            DisclaimerKey,
            new AiSupportUsageDto(
                result.RemainingDailyMessages,
                result.ResetAtUtc)));
    }

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
        AiSafetyReason.SecretDetected or AiSafetyReason.PersonalDataDetected =>
            Problem(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                "The message contains sensitive content that cannot be processed by AI."),
        _ =>
            Problem(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.AiServiceUnavailable),
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
