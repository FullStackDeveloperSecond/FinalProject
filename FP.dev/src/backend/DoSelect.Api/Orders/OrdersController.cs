using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Orders;

/// <summary>
/// Order list remains member-only. Detail and cancellation accept either an authenticated member
/// or a GuestOrderAccess Cookie that has been validated against the exact target order.
/// </summary>
[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly GuestOrderAccessScopeAuthorizer _guestAuthorizer;

    public OrdersController(
        IOrderService orderService,
        GuestOrderAccessScopeAuthorizer guestAuthorizer)
    {
        _orderService = orderService;
        _guestAuthorizer = guestAuthorizer;
    }

    [HttpGet]
    [Authorize(Policy = DoSelectPolicies.Member)]
    public async Task<ActionResult<PageResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] int pageNumber,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var memberUserId = RequireMemberUserId();
        var query = new OrderQuery(
            pageNumber == 0 ? 1 : pageNumber,
            pageSize == 0 ? 20 : pageSize);
        var result = await _orderService.GetOrdersAsync(memberUserId, query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var resolution = await ResolveActorAsync(id, cancellationToken);
        if (resolution.Actor is null)
        {
            return resolution.HadAuthenticatedCookie ? OrderNotFound() : Unauthorized();
        }

        try
        {
            var order = await _orderService.GetOrderAsync(resolution.Actor, id, cancellationToken);
            return Ok(order);
        }
        catch (OrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/actions/cancel")]
    public async Task<ActionResult<OrderDto>> CancelOrder(
        Guid id,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveActorAsync(id, cancellationToken);
        if (resolution.Actor is null)
        {
            return resolution.HadAuthenticatedCookie ? OrderNotFound() : Unauthorized();
        }

        try
        {
            var auditContext = new OrderCancellationAuditContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress);
            var order = await _orderService.CancelOrderAsync(
                resolution.Actor,
                id,
                request,
                auditContext,
                cancellationToken);
            return Ok(order);
        }
        catch (OrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>
    /// [Authorize(Policy = Member)] on the class already guarantees an authenticated member
    /// principal with this claim, so this never actually falls through to
    /// <see cref="UnauthorizedResult"/> in production — it exists only so a missing claim fails
    /// loudly instead of NullReferenceException-ing deeper in the service.
    /// </summary>
    private string RequireMemberUserId()
    {
        var memberUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            throw new InvalidOperationException("Authenticated member request is missing its identifier claim.");
        }

        return memberUserId;
    }

    private async Task<OrderActorResolution> ResolveActorAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        var memberAuthentication = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.Member);
        if (memberAuthentication.Succeeded &&
            memberAuthentication.Principal?.HasClaim(
                DoSelectClaimTypes.AccountType,
                DoSelectClaimValues.Member) == true)
        {
            var memberUserId = memberAuthentication.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                throw new InvalidOperationException(
                    "Authenticated member request is missing its identifier claim.");
            }

            return new OrderActorResolution(new OrderActor.Member(memberUserId), true);
        }

        var guestAuthentication = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.GuestOrderAccess);
        if (!guestAuthentication.Succeeded || guestAuthentication.Principal is null)
        {
            return new OrderActorResolution(Actor: null, HadAuthenticatedCookie: false);
        }

        var authorization = await _guestAuthorizer.AuthorizeAsync(
            guestAuthentication.Principal,
            orderPublicId,
            new GuestOrderAccessAuthorizationAuditContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);
        return authorization is GuestOrderAccessAuthorizationResult.Success success
            ? new OrderActorResolution(new OrderActor.Guest(success.TokenPublicId), true)
            : new OrderActorResolution(Actor: null, HadAuthenticatedCookie: true);
    }

    private ActionResult OrderNotFound()
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status404NotFound,
            OrderWriteException.ErrorCodes.ResourceNotFound,
            detail: "The referenced order was not found.");
        return NotFound(problem);
    }

    private sealed record OrderActorResolution(OrderActor? Actor, bool HadAuthenticatedCookie);
}
