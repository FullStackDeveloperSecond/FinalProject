using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Inventory;

[ApiController]
[Authorize(Policy = DoSelectPolicies.InventoryManager)]
[Route("api/v1/admin/inventory")]
public sealed class AdminInventoryController : ControllerBase
{
    private readonly IInventoryAdminQueryService _queryService;
    private readonly IInventoryReconciliationService _reconciliationService;

    public AdminInventoryController(
        IInventoryAdminQueryService queryService,
        IInventoryReconciliationService reconciliationService)
    {
        _queryService = queryService;
        _reconciliationService = reconciliationService;
    }

    [HttpGet("balances")]
    public async Task<ActionResult<PageResult<InventoryBalanceDto>>> ListBalances(
        [FromQuery] InventoryBalanceListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryBalanceQuery(
            request.Q, request.StockState, request.CategoryCode, request.PageNumber, request.PageSize);
        try
        {
            return Ok(await _queryService.ListBalancesAsync(query, cancellationToken));
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("movements")]
    public async Task<ActionResult<PageResult<InventoryMovementDto>>> ListMovements(
        [FromQuery] InventoryMovementListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryMovementQuery(
            request.SkuPublicId, request.MovementTypes, request.From, request.To,
            request.PageNumber, request.PageSize);
        try
        {
            return Ok(await _queryService.ListMovementsAsync(query, cancellationToken));
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("reservations")]
    public async Task<ActionResult<CursorPage<InventoryReservationDto>>> ListReservations(
        [FromQuery] InventoryReservationListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryReservationListQuery(request.Cursor, request.Status, request.PageSize);
        try
        {
            return Ok(await _queryService.ListReservationsAsync(query, cancellationToken));
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    // UC-ADM-INV-01's manual-release action is intentionally not exposed here yet. Its acceptance
    // criteria (商品訂單物流後台驗收規格.md) require a successful release to persist an Audit Log
    // entry (operator, from-state, order, SKU, quantity, time, TraceId), which no shared Audit Log
    // subsystem exists to satisfy — 組長's PR #36 round-3 ruling was to withdraw the HTTP endpoint
    // until that dependency lands, not to ship a release action that can't meet its own acceptance
    // criteria. IInventoryReservationService.ReleaseAsync itself (and its ReasonCode whitelist) is
    // already built and tested at the service layer — re-adding this action is the only remaining
    // step once Audit Log exists.

    [HttpGet("reconciliation-cases")]
    public async Task<ActionResult<PageResult<InventoryReconciliationCaseDto>>> ListReconciliationCases(
        [FromQuery] InventoryReconciliationCaseListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryReconciliationCaseQuery(request.Status, request.PageNumber, request.PageSize);
        try
        {
            return Ok(await _reconciliationService.ListCasesAsync(query, cancellationToken));
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("reconciliation-cases/{id:guid}/actions/acknowledge")]
    public async Task<IActionResult> AcknowledgeReconciliationCase(
        Guid id, [FromBody] AcknowledgeReconciliationCaseRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = RequireAdminUserId();
        try
        {
            await _reconciliationService.AcknowledgeAsync(id, adminUserId, request.RowVersion, DateTime.UtcNow, cancellationToken);
            return NoContent();
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    // Reconciliation Resolve (both the Dismiss and the real-correction path share this one action
    // via ResolveReconciliationCaseRequest.Dismissed) is intentionally not exposed here yet — same
    // class of gap as the manual-release action above. The real-correction path is a high-risk
    // manual stock adjustment; UC-ADM-INV-01's acceptance criteria require it to persist an Audit
    // Log entry (operator, before/after values, SKU, case, time, TraceId), which no shared Audit Log
    // subsystem exists to satisfy yet — 組長's PR #36 round-4 ruling was to withdraw the whole action
    // (not just the correction half) until that dependency lands, rather than let a Dismiss-only
    // route imply Resolve is otherwise done. IInventoryReconciliationService.ResolveAsync itself is
    // already built, transaction-safe, and tested at the service layer (see round-1/round-4 fixes) —
    // re-adding this route is the only remaining step once Audit Log exists. List and Acknowledge
    // stay available since neither touches Balance or claims to record an audited correction.

    private string RequireAdminUserId()
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new InvalidOperationException("An authenticated admin request must carry a NameIdentifier claim.");
        }

        return adminUserId;
    }
}

public sealed class InventoryBalanceListRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    public string? StockState { get; init; }

    [StringLength(64)]
    public string? CategoryCode { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class InventoryMovementListRequest
{
    public Guid? SkuPublicId { get; init; }

    [MaxLength(9)]
    public IReadOnlyList<string>? MovementTypes { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class InventoryReservationListRequest
{
    [StringLength(512)]
    public string? Cursor { get; init; }

    public string? Status { get; init; }

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class InventoryReconciliationCaseListRequest
{
    public string? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class AcknowledgeReconciliationCaseRequest
{
    [Required]
    public byte[] RowVersion { get; init; } = [];
}
