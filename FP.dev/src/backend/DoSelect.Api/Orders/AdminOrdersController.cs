using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Orders;

[ApiController]
[Authorize(Policy = DoSelectPolicies.OrderManage)]
[Route("api/v1/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly IAdminOrderService _adminOrderService;

    public AdminOrdersController(IAdminOrderService adminOrderService)
    {
        _adminOrderService = adminOrderService;
    }

    [HttpGet]
    public async Task<ActionResult<CursorPage<AdminOrderSummaryDto>>> List(
        [FromQuery] AdminOrderListRequest request,
        CancellationToken cancellationToken)
    {
        var query = new AdminOrderQuery(
            request.SummaryStatus,
            request.Badge,
            request.Cursor,
            request.PageSize);

        try
        {
            var result = await _adminOrderService.ListAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (AdminOrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminOrderDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _adminOrderService.GetAsync(id, cancellationToken);
            return Ok(order);
        }
        catch (AdminOrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("{id:guid}/recipient")]
    public async Task<ActionResult<OrderRecipientDto>> GetRecipient(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var recipient = await _adminOrderService.GetRecipientAsync(id, cancellationToken);
            return Ok(recipient);
        }
        catch (AdminOrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    // Route parameter deliberately not named "action" — that literal name collides with
    // ASP.NET Core's implicit action-selection route value even under attribute routing and
    // silently 404s instead of reaching this method (confirmed empirically while writing
    // AdminOrdersApiTests).
    [HttpPost("{id:guid}/actions/{actionName}")]
    public async Task<ActionResult<AdminOrderDto>> ExecuteAction(
        Guid id,
        string actionName,
        [FromBody] AdminOrderActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await _adminOrderService.ExecuteActionAsync(
                id,
                actionName,
                RequireAdminUserId(),
                GetTraceId(),
                request,
                cancellationToken);
            return Ok(order);
        }
        catch (AdminOrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private string RequireAdminUserId()
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new InvalidOperationException("Authenticated admin request is missing its identifier claim.");
        }

        return adminUserId;
    }

    private string GetTraceId() => Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
}

public sealed class AdminOrderListRequest
{
    [MaxLength(4)]
    public IReadOnlyList<string>? SummaryStatus { get; init; }

    [MaxLength(3)]
    public IReadOnlyList<string>? Badge { get; init; }

    public string? Cursor { get; init; }

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
