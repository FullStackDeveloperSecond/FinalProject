using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Invoicing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoSelect.Api.Invoicing;

[ApiController]
[Authorize(Policy = DoSelectPolicies.InvoiceManage)]
[Route("api/v1/admin/invoices")]
public sealed class AdminInvoiceAllowancesController : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private readonly IInvoiceAllowanceWriter _writer;

    public AdminInvoiceAllowancesController(IInvoiceAllowanceWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    [HttpPost("{id:guid}/allowances")]
    [ProducesResponseType<SimulatedInvoiceAllowanceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SimulatedInvoiceAllowanceDto>> Create(
        Guid id,
        [FromBody] CreateSimulatedInvoiceAllowanceRequest request,
        [FromHeader(Name = IdempotencyKeyHeaderName), BindRequired] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            string.IsNullOrWhiteSpace(adminUserId))
        {
            throw DomainProblemException.Validation(
                $"The {IdempotencyKeyHeaderName} header and administrator identity are required.");
        }

        var traceId = Activity.Current?.TraceId.ToString()
            ?? ActivityTraceId.CreateRandom().ToString();
        var result = await _writer.CreateAsync(
            new CreateInvoiceAllowanceCommand(
                id,
                request.RefundPublicId,
                request.InvoiceRowVersion,
                idempotencyKey,
                adminUserId,
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                traceId,
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);

        return StatusCode(result.StatusCode, result.Body);
    }
}

public sealed record CreateSimulatedInvoiceAllowanceRequest(
    Guid RefundPublicId,
    byte[] InvoiceRowVersion);
