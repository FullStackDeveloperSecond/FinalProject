using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using DoSelect.Domain.Ai;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Ai;

[ApiController]
[Route("api/v1/ai/consents")]
[Authorize(Policy = DoSelectPolicies.AiSupportMember)]
public sealed class AiConsentsController(IAiConsentManager consentManager) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<AiConsentStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        if (!TryGetMemberId(out var memberId))
        {
            return UnauthorizedProblem();
        }

        var snapshot = await consentManager.ReadCurrentAsync(memberId, cancellationToken);
        return snapshot.State == AiConsentState.Unavailable
            ? ServiceUnavailableProblem()
            : Ok(Map(snapshot));
    }

    [HttpPost]
    [ProducesResponseType<AiConsentStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Grant(
        AiConsentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetMemberId(out var memberId))
        {
            return UnauthorizedProblem();
        }

        if (request.PolicyVersion != AiConsentPolicy.CurrentVersion || !request.Accepted)
        {
            return ValidationProblemResult();
        }

        var snapshot = await consentManager.GrantAsync(
            memberId,
            request.PolicyVersion,
            ParseLocale(request.Locale),
            cancellationToken);
        return snapshot.State == AiConsentState.Unavailable
            ? ServiceUnavailableProblem()
            : Ok(Map(snapshot));
    }

    [HttpDelete("current")]
    [ProducesResponseType<AiConsentStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(CancellationToken cancellationToken)
    {
        if (!TryGetMemberId(out var memberId))
        {
            return UnauthorizedProblem();
        }

        var snapshot = await consentManager.WithdrawAsync(memberId, cancellationToken);
        return snapshot.State == AiConsentState.Unavailable
            ? ServiceUnavailableProblem()
            : Ok(Map(snapshot));
    }

    private bool TryGetMemberId(out Guid memberId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out memberId);

    private static AiConsentStatusDto Map(AiConsentSnapshot snapshot) =>
        new(
            snapshot.State.ToString().ToLowerInvariant(),
            snapshot.PolicyVersion,
            snapshot.Locale is null ? null : ToLocale(snapshot.Locale.Value),
            snapshot.DecidedAtUtc);

    private IActionResult UnauthorizedProblem() => Problem(
        StatusCodes.Status401Unauthorized,
        ApiErrorCodes.AuthenticationRequired);

    private IActionResult ValidationProblemResult() => Problem(
        StatusCodes.Status400BadRequest,
        ApiErrorCodes.ValidationFailed);

    private IActionResult ServiceUnavailableProblem() => Problem(
        StatusCodes.Status503ServiceUnavailable,
        ApiErrorCodes.AiServiceUnavailable);

    private ObjectResult Problem(int statusCode, string code)
    {
        var problem = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code);
        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    internal static SupportedLocale ParseLocale(string locale) => locale switch
    {
        "zh-TW" => SupportedLocale.ZhTw,
        "ja-JP" => SupportedLocale.JaJp,
        "ko-KR" => SupportedLocale.KoKr,
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    internal static string ToLocale(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "zh-TW",
        SupportedLocale.JaJp => "ja-JP",
        SupportedLocale.KoKr => "ko-KR",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };
}

[ApiController]
[Route("api/v1/ai/usage")]
[Authorize(Policy = DoSelectPolicies.AiSupportMember)]
public sealed class AiUsageController(IAiMemberUsageReader usageReader) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<AiUsageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var memberId))
        {
            return ProblemResult(StatusCodes.Status401Unauthorized, ApiErrorCodes.AuthenticationRequired);
        }

        var usage = await usageReader.ReadSupportUsageAsync(memberId, cancellationToken);
        if (usage is null)
        {
            return ProblemResult(StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.AiServiceUnavailable);
        }

        return Ok(new AiUsageDto(
            "support",
            usage.UsedRequests,
            usage.RequestLimit,
            usage.WindowStartUtc,
            usage.ResetAtUtc));
    }

    private ObjectResult ProblemResult(int statusCode, string code)
    {
        var problem = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code);
        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}

[ApiController]
[Route("api/v1/admin/ai/usage")]
[Authorize(Policy = DoSelectPolicies.AiUsageView)]
public sealed class AdminAiUsageController(
    IAiAdminUsageReader usageReader,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AdminAiUsageReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var to = (toUtc ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var from = (fromUtc ?? to.AddDays(-30)).ToUniversalTime();
        if (from >= to || to - from > TimeSpan.FromDays(90))
        {
            return ProblemResult(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationFailed);
        }

        var usage = await usageReader.ReadAsync(from, to, cancellationToken);
        if (usage is null)
        {
            return ProblemResult(StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.AiServiceUnavailable);
        }

        var mayViewCost = User.IsInRole(DoSelectRoles.FinanceManager) ||
            User.IsInRole(DoSelectRoles.SuperAdmin);
        return Ok(new AdminAiUsageReportDto(
            usage.FromUtc,
            usage.ToUtc,
            usage.Rows.Select(row => new AdminAiUsageRowDto(
                row.Feature,
                row.Model,
                row.Status,
                row.InteractionCount,
                row.InputTokens,
                row.OutputTokens,
                mayViewCost ? row.EstimatedCostUsd : null)).ToArray(),
            mayViewCost ? usage.CumulativeCostUsd : null,
            usage.BudgetWarningActive,
            usage.BudgetProtectionActive,
            usage.DataAsOfUtc));
    }

    private ObjectResult ProblemResult(int statusCode, string code)
    {
        var problem = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code);
        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
