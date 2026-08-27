using DoSelect.Api.Common;
using DoSelect.Application.Builds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DoSelect.Api.Builds;

/// <summary>UC-COMPAT-01: public, unauthenticated compatibility evaluation.</summary>
[ApiController]
[Route("api/v1/compatibility-checks")]
[EnableRateLimiting(RateLimiterPolicies.PublicBuildsAnonymous)]
public sealed class CompatibilityChecksController : ControllerBase
{
    private readonly ICompatibilityCheckService _compatibilityCheckService;

    public CompatibilityChecksController(ICompatibilityCheckService compatibilityCheckService)
    {
        _compatibilityCheckService = compatibilityCheckService;
    }

    [HttpPost]
    public async Task<ActionResult<CompatibilityCheckDto>> Check(
        [FromBody] CompatibilityCheckRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _compatibilityCheckService.CheckAsync(request, buildListId: null, cancellationToken);
            return Ok(result);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}
