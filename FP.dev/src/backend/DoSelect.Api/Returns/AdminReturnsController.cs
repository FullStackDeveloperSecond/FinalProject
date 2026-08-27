using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Returns;
using DoSelect.Domain.Returns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Returns;

/// <summary>
/// Back-office return endpoints. Query, process actions (receive/inspect/extend) and review all
/// require the same OrderManager／SuperAdmin role set per API Endpoint目錄.md, so every action
/// here reuses the existing Return.Approve policy rather than registering new ones with an
/// identical role set — see the implementation report.
/// </summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.ReturnApprove)]
[Route("api/v1/admin/returns")]
public sealed class AdminReturnsController : ControllerBase
{
    private readonly IAdminReturnService _service;

    public AdminReturnsController(IAdminReturnService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<AdminReturnSummaryDto>>> List(
        [FromQuery] AdminReturnListRequest request, CancellationToken cancellationToken)
    {
        var query = new AdminReturnQuery(
            request.Statuses, request.ReasonCodes, request.From, request.To, request.Q,
            request.PageNumber ?? 1, request.PageSize ?? 20);
        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminReturnDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetDetailAsync(id, cancellationToken));
        }
        catch (ReturnsWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/actions/review")]
    public async Task<ActionResult<ReturnRequestDto>> Review(
        Guid id, [FromBody] ApproveReturnRequest request, CancellationToken cancellationToken) =>
        await RunAction(() => _service.ReviewAsync(id, AdminUserId(), request, cancellationToken));

    [HttpPost("{id:guid}/actions/receive")]
    public async Task<ActionResult<ReturnRequestDto>> Receive(
        Guid id, [FromBody] ReceiveReturnRequest request, CancellationToken cancellationToken) =>
        await RunAction(() => _service.ReceiveAsync(id, AdminUserId(), request, cancellationToken));

    [HttpPost("{id:guid}/actions/inspect")]
    public async Task<ActionResult<ReturnRequestDto>> Inspect(
        Guid id, [FromBody] InspectReturnRequest request, CancellationToken cancellationToken) =>
        await RunAction(() => _service.InspectAsync(id, AdminUserId(), request, cancellationToken));

    [HttpPost("{id:guid}/actions/extend-shipment-deadline")]
    public async Task<ActionResult<ReturnRequestDto>> ExtendShipmentDeadline(
        Guid id, [FromBody] ExtendShipmentDeadlineRequest request, CancellationToken cancellationToken) =>
        await RunAction(() => _service.ExtendShipmentDeadlineAsync(id, AdminUserId(), request, cancellationToken));

    [HttpGet("{id:guid}/shipment")]
    public async Task<ActionResult<ReturnShipmentDto>> GetShipment(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetShipmentAsync(id, cancellationToken));
        }
        catch (ReturnsWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/shipment")]
    public async Task<ActionResult<ReturnShipmentDto>> CreateShipment(
        Guid id, [FromBody] CreateReturnShipmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.CreateShipmentAsync(id, request, cancellationToken));
        }
        catch (ReturnsWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/shipment/events")]
    public async Task<ActionResult<ReturnShipmentDto>> AppendShipmentEvent(
        Guid id, [FromBody] AppendReturnShipmentEventRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.AppendShipmentEventAsync(id, request, cancellationToken));
        }
        catch (ReturnsWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private async Task<ActionResult<ReturnRequestDto>> RunAction(Func<Task<ReturnRequestDto>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ReturnsWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private string AdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated admin request is missing a NameIdentifier claim.");
}

public sealed record AdminReturnListRequest(
    IReadOnlyList<ReturnRequestStatus>? Statuses,
    IReadOnlyList<string>? ReasonCodes,
    DateTime? From,
    DateTime? To,
    string? Q,
    int? PageNumber,
    int? PageSize);
