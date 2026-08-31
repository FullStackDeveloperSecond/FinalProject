using System.Security.Claims;
using DoSelect.Api.Contracts.Favorites;
using DoSelect.Api.Security;
using DoSelect.Application.Favorites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Favorites;

/// <summary>S-01 會員收藏（02-領域需求/04-客服與售後/評價收藏檢舉與模擬發票規格.md「收藏」）。</summary>
[ApiController]
[Route("api/v1/members/me/favorites")]
[Authorize(Policy = DoSelectPolicies.Member)]
public sealed class FavoritesController(IFavoriteService favoriteService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FavoriteResponse>>> ListMine(
        CancellationToken cancellationToken)
    {
        var favorites = await favoriteService.ListMineAsync(MemberUserId(), cancellationToken);
        return Ok(favorites.Select(FavoriteResponse.From).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<FavoriteResponse>> Add(
        [FromBody] AddFavoriteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await favoriteService.AddAsync(
                MemberUserId(), request.ProductPublicId, cancellationToken);
            return CreatedAtAction(nameof(ListMine), null, FavoriteResponse.From(result));
        }
        catch (FavoriteWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>冪等：收藏不存在（或商品從未存在）都回 204，不視為錯誤。</summary>
    [HttpDelete("{productPublicId:guid}")]
    public async Task<IActionResult> Remove(
        Guid productPublicId,
        CancellationToken cancellationToken)
    {
        await favoriteService.RemoveAsync(MemberUserId(), productPublicId, cancellationToken);
        return NoContent();
    }

    private string MemberUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
