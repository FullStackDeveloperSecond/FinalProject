using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Controllers;

/// <summary>
/// Admin-facing unified case workbench (UC-WORKBENCH-01). Only the Support case-type scope is
/// confirmed for this slice — Report/Return scope mappings are not yet decided, so every caller
/// who satisfies SupportTicket.Handle is authorized for Support-scoped rows only.
/// </summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
[Route("api/v1/admin/case-workbench")]
public sealed class CaseWorkbenchController : ControllerBase
{
    private static readonly IReadOnlyCollection<CaseWorkbenchCaseType> AuthorizedCaseTypes =
        [CaseWorkbenchCaseType.Support];

    private readonly ICaseWorkbenchService _service;

    public CaseWorkbenchController(ICaseWorkbenchService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<CursorPage<CaseWorkbenchItemDto>>> Get(
        [FromQuery] CaseWorkbenchQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetPageAsync(query, AuthorizedCaseTypes, cancellationToken);
        return Ok(result);
    }
}
