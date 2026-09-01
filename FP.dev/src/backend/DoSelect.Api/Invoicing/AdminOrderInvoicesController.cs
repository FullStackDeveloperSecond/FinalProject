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
[Route("api/v1/admin/orders")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminOrderInvoicesController : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private readonly IAdminInvoiceWriter _writer;
    private readonly InvoiceIssuanceOrderQueryService _query;

    public AdminOrderInvoicesController(
        IAdminInvoiceWriter writer,
        InvoiceIssuanceOrderQueryService query)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(query);
        _writer = writer;
        _query = query;
    }

    [HttpGet("{orderId:guid}/invoice-issuance")]
    [ProducesResponseType<InvoiceIssuanceOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceIssuanceOrderDto>> GetIssuanceSnapshot(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await _query.FindAsync(orderId, cancellationToken);
        if (order is null)
        {
            return NotFound(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound,
                detail: "The referenced order was not found."));
        }

        return Ok(order);
    }

    [HttpPost("{orderId:guid}/invoices")]
    [ProducesResponseType<AdminInvoiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminInvoiceDto>> Issue(
        Guid orderId,
        [FromBody] IssueSimulatedInvoiceRequest request,
        [FromHeader(Name = IdempotencyKeyHeaderName), BindRequired] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(adminUserId))
        {
            throw DomainProblemException.Validation(
                $"The {IdempotencyKeyHeaderName} header and administrator identity are required.");
        }

        var result = await _writer.IssueAsync(
            new IssueSimulatedInvoiceCommand(
                orderId,
                request.OrderRowVersion,
                idempotencyKey,
                adminUserId,
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }
}

public sealed record IssueSimulatedInvoiceRequest(byte[] OrderRowVersion);
