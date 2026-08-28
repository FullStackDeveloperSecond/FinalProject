using DoSelect.Api.Common;
using DoSelect.Application.Builds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DoSelect.Api.Builds;

/// <summary>UC-BUILD-01 分享: public, unauthenticated read of a shared build list by opaque token.</summary>
[ApiController]
[Route("api/v1/build-shares")]
[EnableRateLimiting(RateLimiterPolicies.PublicBuildsAnonymous)]
public sealed class BuildSharesController : ControllerBase
{
    private readonly IBuildListService _buildListService;

    public BuildSharesController(IBuildListService buildListService)
    {
        _buildListService = buildListService;
    }

    [HttpGet("{token}")]
    public async Task<ActionResult<SharedBuildDto>> Get(string token, CancellationToken cancellationToken)
    {
        try
        {
            var sharedBuild = await _buildListService.GetSharedBuildAsync(token, cancellationToken);
            return Ok(sharedBuild);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}
