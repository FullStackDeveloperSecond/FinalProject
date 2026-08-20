using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Controllers;

/// <summary>
/// Admin-facing support ticket endpoints. Unlike SupportTicketsController (Actor Scope: a
/// member's own tickets), every action here operates across all tickets and requires the
/// SupportTicket.Handle policy (CustomerService or CustomerServiceSupervisor).
/// </summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
[Route("api/v1/admin/support-tickets")]
public sealed class AdminSupportTicketsController : ControllerBase
{
    private readonly IAdminSupportTicketService _service;
    private readonly ISupportSlaQueueService _slaQueueService;

    public AdminSupportTicketsController(
        IAdminSupportTicketService service,
        ISupportSlaQueueService slaQueueService)
    {
        _service = service;
        _slaQueueService = slaQueueService;
    }

    [HttpPost("{id:guid}/actions/claim")]
    public async Task<ActionResult<AdminSupportTicketDto>> Claim(
        Guid id,
        [FromBody] ClaimSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ClaimAsync(GetAdminUserId(), id, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("sla")]
    public async Task<ActionResult<CursorPage<SupportSlaItemDto>>> GetSlaQueue(
        [FromQuery] SupportSlaQueueQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _slaQueueService.GetPageAsync(query, cancellationToken);
        return Ok(result);
    }

    private string GetAdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated request is missing a NameIdentifier claim.");
}
