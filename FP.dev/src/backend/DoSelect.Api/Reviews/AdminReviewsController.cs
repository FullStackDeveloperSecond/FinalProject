using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Reviews;

[ApiController]
[Route("api/v1/admin/reviews")]
[Authorize(Policy = DoSelectPolicies.ProductReviewModerate)]
public sealed class AdminReviewsController(IReviewService reviewService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminReviewDto>>> List(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reviewService.ListForModerationAsync(status, cancellationToken));
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminReviewDto>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await reviewService.GetForModerationAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/actions/{moderationAction}")]
    public async Task<ActionResult<AdminReviewDto>> Moderate(
        Guid id,
        string moderationAction,
        [FromBody] ReviewModerationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reviewService.ModerateAsync(
                BuildActor(), id, moderationAction, request, cancellationToken));
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private ReviewAdminActor BuildActor()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var roles = User.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new ReviewAdminActor(
            userId,
            roles,
            new AuditRequestContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                traceId,
                HttpContext.Connection.RemoteIpAddress));
    }
}
