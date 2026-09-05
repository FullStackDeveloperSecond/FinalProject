using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Favorites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Favorites;

/// <summary>
/// 會員收藏 (S-01). Every action is scoped to the caller's own favorites via
/// <see cref="GetMemberUserId"/> — a member can never read or change another member's list
/// (Actor Scope; 評價收藏檢舉與模擬發票規格.md).
/// </summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.Member)]
[Route("api/v1/members/me/favorites")]
public sealed class FavoritesController : ControllerBase
{
    private readonly IFavoriteGateway _favoriteGateway;

    public FavoritesController(IFavoriteGateway favoriteGateway)
    {
        _favoriteGateway = favoriteGateway;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<FavoriteItemDto>>> List(
        [FromQuery] FavoritesListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _favoriteGateway.ListAsync(
            GetMemberUserId(),
            query.PageNumber,
            query.PageSize,
            cancellationToken);
        return Ok(result);
    }

    // PUT, not POST: adding an already-favorited product is success, not a second resource
    // (評價收藏檢舉與模擬發票規格.md — MemberId+ProductId 唯一，重複加入視為成功且不建立第二筆), which is
    // PUT's idempotent-create semantics rather than POST's.
    [HttpPut("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(Guid productId, CancellationToken cancellationToken)
    {
        var result = await _favoriteGateway.AddAsync(GetMemberUserId(), productId, cancellationToken);

        if (result == AddFavoriteResult.ProductNotFound)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound);
            return NotFound(problem);
        }

        return NoContent();
    }

    // Idempotent by design: removing a favorite that is already gone (or was never there) is
    // still 204, not 404 — see EfFavoriteGateway.RemoveAsync.
    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid productId, CancellationToken cancellationToken)
    {
        await _favoriteGateway.RemoveAsync(GetMemberUserId(), productId, cancellationToken);
        return NoContent();
    }

    private string GetMemberUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated request is missing a NameIdentifier claim.");
}
