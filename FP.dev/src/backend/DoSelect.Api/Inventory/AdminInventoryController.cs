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
    private readonly IInventoryReservationService _reservationService;
    private readonly IInventoryReconciliationService _reconciliationService;

    public AdminInventoryController(
        IInventoryAdminQueryService queryService,
        IInventoryReservationService reservationService,
        IInventoryReconciliationService reconciliationService)
    {
        _queryService = queryService;
        _reservationService = reservationService;
        _reconciliationService = reconciliationService;
    }

    [HttpGet("balances")]
    public async Task<ActionResult<PageResult<InventoryBalanceDto>>> ListBalances(
        [FromQuery] InventoryBalanceListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryBalanceQuery(
            request.Q, request.StockState, request.CategoryCode, request.PageNumber, request.PageSize);
        return Ok(await _queryService.ListBalancesAsync(query, cancellationToken));
    }

    [HttpGet("movements")]
    public async Task<ActionResult<PageResult<InventoryMovementDto>>> ListMovements(
        [FromQuery] InventoryMovementListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryMovementQuery(
            request.SkuPublicId, request.MovementTypes, request.From, request.To,
            request.PageNumber, request.PageSize);
        return Ok(await _queryService.ListMovementsAsync(query, cancellationToken));
    }

    [HttpGet("reservations")]
    public async Task<ActionResult<CursorPage<InventoryReservationDto>>> ListReservations(
        [FromQuery] InventoryReservationListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryReservationListQuery(request.Cursor, request.Status, request.PageSize);
        return Ok(await _queryService.ListReservationsAsync(query, cancellationToken));
    }

    [HttpPost("reservations/{id:guid}/actions/release")]
    public async Task<IActionResult> ReleaseReservation(
        Guid id, [FromBody] ReleaseReservationRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = RequireAdminUserId();
        try
        {
            await _reservationService.ReleaseAsync(
                id, request.ReasonCode, request.Note, adminUserId, request.RowVersion,
                DateTime.UtcNow, cancellationToken);
            return NoContent();
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("reconciliation-cases")]
    public async Task<ActionResult<PageResult<InventoryReconciliationCaseDto>>> ListReconciliationCases(
        [FromQuery] InventoryReconciliationCaseListRequest request, CancellationToken cancellationToken)
    {
        var query = new InventoryReconciliationCaseQuery(request.Status, request.PageNumber, request.PageSize);
        return Ok(await _reconciliationService.ListCasesAsync(query, cancellationToken));
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

    [HttpPost("reconciliation-cases/{id:guid}/actions/resolve")]
    public async Task<IActionResult> ResolveReconciliationCase(
        Guid id, [FromBody] ResolveReconciliationCaseRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = RequireAdminUserId();
        try
        {
            await _reconciliationService.ResolveAsync(id, adminUserId, request, DateTime.UtcNow, cancellationToken);
            return NoContent();
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

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

    [Range(1, 200)]
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

    [Range(1, 200)]
    public int PageSize { get; init; } = 20;
}

public sealed class InventoryReservationListRequest
{
    [StringLength(512)]
    public string? Cursor { get; init; }

    public string? Status { get; init; }

    [Range(1, 200)]
    public int PageSize { get; init; } = 20;
}

public sealed class InventoryReconciliationCaseListRequest
{
    public string? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 200)]
    public int PageSize { get; init; } = 20;
}

public sealed class AcknowledgeReconciliationCaseRequest
{
    [Required]
    public byte[] RowVersion { get; init; } = [];
}
