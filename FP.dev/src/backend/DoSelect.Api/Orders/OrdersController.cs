using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Orders;

/// <summary>
/// Member-owner scoped only — see <see cref="DoSelect.Infrastructure.Orders.EfOrderService"/>'s
/// remarks for why the guest (GuestOrderAccessToken) path isn't wired into this controller yet.
/// </summary>
[ApiController]
[Route("api/v1/orders")]
[Authorize(Policy = DoSelectPolicies.Member)]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet]
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
        var memberUserId = RequireMemberUserId();

        try
        {
            var order = await _orderService.GetOrderAsync(memberUserId, id, cancellationToken);
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
        var memberUserId = RequireMemberUserId();

        try
        {
            var auditContext = new OrderCancellationAuditContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress);
            var order = await _orderService.CancelOrderAsync(
                memberUserId,
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
}
