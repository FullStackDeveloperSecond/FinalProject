using DoSelect.Api.Common;
using DoSelect.Api.Shopping;
using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoSelect.Api.Orders;

/// <summary>
/// UC-CHECKOUT-01, sharing the <c>api/v1/orders</c> route prefix with OrdersController the same
/// way ShippingOptionsController shares <c>api/v1/cart</c> with CartController — deliberately a
/// separate controller, not an action added to OrdersController. OrdersController's GET/cancel
/// routes must keep working in any environment that never provisions Checkout's own config (the
/// idempotency actor-scope pepper, Checkout policy versions); folding CreateOrder into that
/// controller would make CheckoutService — and everything it needs — a hard constructor
/// dependency of every OrdersController route, not just this one.
/// </summary>
[ApiController]
[Route("api/v1/orders")]
public sealed class CheckoutController : ControllerBase
{
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private readonly IOrderService _orderService;
    private readonly CheckoutService _checkoutService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CheckoutController(
        IOrderService orderService,
        CheckoutService checkoutService,
        UserManager<ApplicationUser> userManager)
    {
        _orderService = orderService;
        _checkoutService = checkoutService;
        _userManager = userManager;
    }

    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = IdempotencyKeyHeaderName), BindRequired] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveCheckoutActorAsync();
        if (actor is null)
        {
            throw DomainProblemException.Validation(
                $"A member session or the '{CartIdentityResolver.GuestCartKeyHeaderName}' header is required.");
        }

        var execution = await _checkoutService.CreateOrderAsync(
            actor, request, idempotencyKey, cancellationToken);

        try
        {
            var order = await _orderService.GetOrderForCheckoutConfirmationAsync(
                execution.Body.PublicId, cancellationToken);
            return StatusCode(execution.StatusCode, order);
        }
        catch (OrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private async Task<CheckoutActor?> ResolveCheckoutActorAsync()
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return null;
        }

        if (identity.MemberUserId is not { } memberUserId)
        {
            return CheckoutActor.ForGuest(identity.GuestCartKey!);
        }

        var applicationUser = await _userManager.FindByIdAsync(memberUserId);
        if (applicationUser is null)
        {
            return null;
        }

        return CheckoutActor.ForMember(memberUserId, applicationUser.PublicId);
    }
}
