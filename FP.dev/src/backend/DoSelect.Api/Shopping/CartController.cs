using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Shopping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shopping;

/// <summary>
/// Every action except <see cref="Merge"/> accepts both anonymous guests (via
/// <see cref="CartIdentityResolver.GuestCartKeyHeaderName"/>) and authenticated members —
/// no [Authorize] attribute can express that mix, so those actions resolve the caller
/// themselves and scope every query/write to that caller's own cart. No class-level
/// [AllowAnonymous]/[Authorize] is used (there is no fallback authorization policy in this
/// app, so an action with neither attribute is anonymous by default) — that leaves room for
/// Merge to require the Member policy on its own.
/// </summary>
[ApiController]
[Route("api/v1/cart")]
public sealed class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet]
    public async Task<ActionResult<CartDto>> GetCart(CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return IdentityRequiredProblem();
        }

        var cart = await _cartService.GetCartAsync(identity, cancellationToken);
        return Ok(cart);
    }

    [HttpPost("items")]
    public async Task<ActionResult<CartDto>> AddItem(
        [FromBody] AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var cart = await _cartService.AddItemAsync(identity, request, cancellationToken);
            return Ok(cart);
        }
        catch (ShoppingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPatch("items/{id:guid}")]
    public async Task<ActionResult<CartDto>> UpdateItemQuantity(
        Guid id,
        [FromBody] UpdateCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var cart = await _cartService.UpdateItemQuantityAsync(identity, id, request, cancellationToken);
            return Ok(cart);
        }
        catch (ShoppingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<ActionResult<CartDto>> RemoveItem(
        Guid id,
        [FromBody] RemoveCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var cart = await _cartService.RemoveItemAsync(identity, id, request.ItemRowVersion, cancellationToken);
            return Ok(cart);
        }
        catch (ShoppingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("actions/revalidate")]
    public async Task<ActionResult<CartValidationDto>> Revalidate(CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return IdentityRequiredProblem();
        }

        var validation = await _cartService.RevalidateAsync(identity, cancellationToken);
        return Ok(validation);
    }

    [HttpPost("actions/merge")]
    [Authorize(Policy = DoSelectPolicies.Member)]
    public async Task<ActionResult<CartMergeResultDto>> Merge(
        [FromBody] CartMergeRequest request,
        CancellationToken cancellationToken)
    {
        var memberUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var result = await _cartService.MergeAsync(memberUserId, request, cancellationToken);
            return Ok(result);
        }
        catch (ShoppingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private ActionResult IdentityRequiredProblem()
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status400BadRequest,
            ShoppingWriteException.ErrorCodes.ValidationFailed,
            detail: $"A member session or the '{CartIdentityResolver.GuestCartKeyHeaderName}' header is required.");
        return BadRequest(problem);
    }
}

/// <summary>
/// Same no-documented-convention situation as Catalog's DeleteSkuRequest — RowVersion
/// travels in the DELETE body for consistency with every other write endpoint here.
/// </summary>
public sealed record RemoveCartItemRequest(byte[] ItemRowVersion);
