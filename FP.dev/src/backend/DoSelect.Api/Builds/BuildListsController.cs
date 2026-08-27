using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Builds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Builds;

/// <summary>UC-BUILD-01: a member's own saved PC build lists (create/list/read/update/delete).</summary>
[ApiController]
[Route("api/v1/build-lists")]
[Authorize(Policy = DoSelectPolicies.Member)]
public sealed class BuildListsController : ControllerBase
{
    private readonly IBuildListService _buildListService;

    public BuildListsController(IBuildListService buildListService)
    {
        _buildListService = buildListService;
    }

    [HttpGet]
    public async Task<ActionResult> List([FromQuery] BuildListListQuery query, CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        var result = await _buildListService.ListAsync(memberUserId, query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildListDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var buildList = await _buildListService.GetAsync(memberUserId, id, cancellationToken);
            return Ok(buildList);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost]
    public async Task<ActionResult<BuildListDto>> Create(
        [FromBody] CreateBuildListRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var buildList = await _buildListService.CreateAsync(memberUserId, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = buildList.PublicId }, buildList);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BuildListDto>> Update(
        Guid id,
        [FromBody] UpdateBuildListRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var buildList = await _buildListService.UpdateAsync(memberUserId, id, request, cancellationToken);
            return Ok(buildList);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(
        Guid id,
        [FromBody] DeleteBuildListRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            await _buildListService.DeleteAsync(memberUserId, id, request.RowVersion, cancellationToken);
            return NoContent();
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/share")]
    public async Task<ActionResult<BuildShareDto>> CreateShare(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var share = await _buildListService.CreateShareAsync(memberUserId, id, cancellationToken);
            return Ok(share);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpDelete("{id:guid}/share")]
    public async Task<ActionResult> RevokeShare(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            await _buildListService.RevokeShareAsync(memberUserId, id, cancellationToken);
            return NoContent();
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/actions/add-to-cart")]
    public async Task<ActionResult> AddToCart(
        Guid id,
        [FromBody] AddBuildToCartRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetMemberUserId(out var memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var cart = await _buildListService.AddToCartAsync(
                memberUserId, id, request, idempotencyKey ?? string.Empty, cancellationToken);
            return Ok(cart);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private bool TryGetMemberUserId(out string memberUserId)
    {
        memberUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(memberUserId);
    }

    private ActionResult IdentityRequiredProblem()
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status400BadRequest,
            BuildWriteException.ErrorCodes.ValidationFailed,
            detail: "A member session is required.");
        return BadRequest(problem);
    }
}

/// <summary>Same no-documented-convention situation as Cart's RemoveCartItemRequest — RowVersion travels in the DELETE body for consistency with every other write endpoint here.</summary>
public sealed record DeleteBuildListRequest(byte[] RowVersion);
