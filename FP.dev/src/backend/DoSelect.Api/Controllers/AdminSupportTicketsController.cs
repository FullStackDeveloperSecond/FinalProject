using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Controllers;

/// <summary>
/// Admin-facing support ticket endpoints. DES-23 replaced the single class-level
/// SupportTicket.Handle [Authorize] with an explicit per-action policy: Handle's role list
/// (CustomerService, CustomerServiceSupervisor) does not include SuperAdmin, while Supervise's
/// (CustomerServiceSupervisor, SuperAdmin) does — a class-level Handle attribute would silently
/// AND-compose with a Supervise-only action's own [Authorize] and reject a bare SuperAdmin who
/// should be admitted. change-priority is the one action both a Handle-only caller (general
/// adjustment) and a Supervise-only bare SuperAdmin (override) must reach through the same route;
/// since ASP.NET Core composes multiple [Authorize] attributes with AND (not OR) and no third
/// policy may be invented for this, its entry gate is the bare Admin policy plus an imperative
/// role check replicating "Handle OR Supervise" — not a new named policy.
/// </summary>
[ApiController]
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

    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> GetDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetDetailAsync(GetAdminUserId(), CanSupervise(), id, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    [HttpPost("{id:guid}/actions/claim")]
    public async Task<ActionResult<AdminSupportTicketDto>> Claim(
        Guid id,
        [FromBody] ClaimSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ClaimAsync(GetAdminUserId(), id, request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketSupervise)]
    [HttpPost("{id:guid}/actions/assign")]
    public async Task<ActionResult<AdminSupportTicketDto>> Assign(
        Guid id,
        [FromBody] AssignSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.AssignAsync(BuildContext(), id, request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketSupervise)]
    [HttpPost("{id:guid}/actions/transfer")]
    public async Task<ActionResult<AdminSupportTicketDto>> Transfer(
        Guid id,
        [FromBody] TransferSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.TransferAsync(BuildContext(), id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The single change-priority Action serving both a general adjustment (SupportTicket.Handle)
    /// and a supervisor override (SupportTicket.Supervise) — see the class summary for why the
    /// entry gate is the bare Admin policy plus this imperative "Handle OR Supervise" check
    /// rather than a declarative [Authorize(Policy = ...)] on a single named policy.
    /// </summary>
    [Authorize(Policy = DoSelectPolicies.Admin)]
    [HttpPost("{id:guid}/actions/change-priority")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> ChangePriority(
        Guid id,
        [FromBody] ChangeSupportTicketPriorityRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanHandle() && !CanSupervise())
        {
            return Forbid();
        }

        var result = await _service.ChangePriorityAsync(BuildContext(), id, request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    [HttpPost("{id:guid}/actions/change-status")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeSupportTicketStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ChangeStatusAsync(BuildContext(), id, request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    [HttpPost("{id:guid}/actions/cancel")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> Cancel(
        Guid id,
        [FromBody] CancelSupportTicketByAdminRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(BuildContext(), id, request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    [HttpPost("{id:guid}/actions/reopen")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> Reopen(
        Guid id,
        [FromBody] ReopenSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ReopenAsync(BuildContext(), id, request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    [HttpPost("{id:guid}/internal-notes")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> AddInternalNote(
        Guid id,
        [FromBody] CreateInternalNoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.AddInternalNoteAsync(BuildContext(), id, request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    [HttpGet("sla")]
    public async Task<ActionResult<CursorPage<SupportSlaItemDto>>> GetSlaQueue(
        [FromQuery] SupportSlaQueueQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _slaQueueService.GetPageAsync(
            query,
            GetAdminUserId(),
            CanSupervise(),
            cancellationToken);
        return Ok(result);
    }

    private string GetAdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated request is missing a NameIdentifier claim.");

    private bool CanSupervise() =>
        User.IsInRole(DoSelectRoles.CustomerServiceSupervisor) || User.IsInRole(DoSelectRoles.SuperAdmin);

    private bool CanHandle() =>
        User.IsInRole(DoSelectRoles.CustomerService) || User.IsInRole(DoSelectRoles.CustomerServiceSupervisor);

    private SupportTicketActionContext BuildContext()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new SupportTicketActionContext(
            GetAdminUserId(),
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray(),
            CanSupervise(),
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
    }
}
