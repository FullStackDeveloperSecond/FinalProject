using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
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

    /// <summary>
    /// UC-ADM-INV-01 人工釋放（Endpoint 目錄「UC-ADM-INV-01 保留」列）。PR #36 round 3 時因為中央
    /// Audit 尚未落地而撤回；現在釋放與 <c>inventory_reservation.release</c> 稽核同一次 SaveChanges
    /// 寫入，驗收條件「保存 InventoryMovement 與 Audit Log」成立，路由補回。
    /// 重送保護靠 RowVersion：同一筆保留釋放後 RowVersion 就變了，帶舊值重送會被拒，不會再減一次。
    /// </summary>
    [HttpPost("reservations/{id:guid}/actions/release")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReleaseReservation(
        Guid id, [FromBody] ReleaseReservationRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = RequireAdminUserId();
        try
        {
            await _reservationService.ReleaseAsync(
                id,
                request.ReasonCode,
                request.Note,
                adminUserId,
                request.RowVersion,
                BuildAuditContext(),
                DateTime.UtcNow,
                cancellationToken);
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

    /// <summary>
    /// UC-ADM-INV-01 對帳 dismiss（Endpoint 目錄「UC-ADM-INV-01 對帳」列）：核對基準錯誤，結案不動庫存。
    /// PR #36 round 4 撤回的動作，依組長對帳裁定 A1～H1 拆成 dismiss／resolve 兩條路由補回；Body 不再帶
    /// `dismissed` 布林。重送保護靠 RowVersion：案件結案後 RowVersion 就變了，帶舊值重送會被拒。
    /// </summary>
    [HttpPost("reconciliation-cases/{id:guid}/actions/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DismissReconciliationCase(
        Guid id, [FromBody] ReconciliationCaseResolutionRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = RequireAdminUserId();
        try
        {
            await _reconciliationService.DismissAsync(
                id, request.ToCommand(), adminUserId, BuildAuditContext(), DateTime.UtcNow, cancellationToken);
            return NoContent();
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>
    /// UC-ADM-INV-01 對帳 resolve：以帳本重算值修正 Balance，建立零差額 Adjustment Movement 並寫中央稽核，
    /// 全部同一個 SQL transaction。Balance／帳本快照過期回 409 `concurrency_conflict`（重新偵測後再操作）；
    /// 重算後 Reserved &gt; OnHand 回 409 `inventory_reconciliation_ledger_inconsistent`（重送修不好，案件留著人工調查）。
    /// </summary>
    [HttpPost("reconciliation-cases/{id:guid}/actions/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveReconciliationCase(
        Guid id, [FromBody] ReconciliationCaseResolutionRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = RequireAdminUserId();
        try
        {
            await _reconciliationService.ResolveAsync(
                id, request.ToCommand(), adminUserId, BuildAuditContext(), DateTime.UtcNow, cancellationToken);
            return NoContent();
        }
        catch (InventoryWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private AuditRequestContext BuildAuditContext()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new AuditRequestContext(
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
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

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class InventoryMovementListRequest
{
    public Guid? SkuPublicId { get; init; }

    // Caps the filter at "every type at once" rather than an arbitrary number, so it tracks
    // InventoryMovementTypes.All.Count — it cannot reference that constant directly (attribute
    // arguments must be compile-time constants), so MovementTypeFilterCapMatchesTheVocabulary
    // guards the two against drifting apart. Raised 9 -> 10 when 組長's PR #36 ruling A1 made
    // CostChange a first-class movement type.
    [MaxLength(10)]
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

/// <summary>
/// `ReleaseReservationRequest`（Endpoint 目錄「UC-ADM-INV-01 保留」列）。reasonCode 是
/// <see cref="InventoryReleaseReasonCodes.All"/> 白名單（服務層驗）；note 是必填的自由文字說明
/// （驗收：「未填原因 → API 拒絕操作且庫存數量不變」），會進中央稽核的 note；長度 1..500 依
/// API DTO與Schema契約.md 的 `ReleaseReservationRequest` 列。
/// </summary>
public sealed class ReleaseReservationRequest
{
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string ReasonCode { get; init; } = string.Empty;

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Note { get; init; } = string.Empty;

    [Required]
    [MinLength(1)]
    public byte[] RowVersion { get; init; } = [];
}

/// <summary>
/// `ReconciliationCaseResolutionRequest`（API DTO與Schema契約）：dismiss／resolve 共用。reasonCode 是
/// <see cref="InventoryReconciliationReasonCodes"/> 依動作的白名單（服務層驗）；note 必填，trim 後存進案件
/// `ResolutionReason` 也寫進中央稽核 note；長度 1..500 與人工釋放相同。
/// </summary>
public sealed class ReconciliationCaseResolutionRequest
{
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string ReasonCode { get; init; } = string.Empty;

    [Required]
    [StringLength(ReconciliationCaseResolutionCommand.NoteMaxLength, MinimumLength = 1)]
    public string Note { get; init; } = string.Empty;

    [Required]
    [MinLength(1)]
    public byte[] RowVersion { get; init; } = [];

    internal ReconciliationCaseResolutionCommand ToCommand() => new(ReasonCode, Note, RowVersion);
}

public sealed class AcknowledgeReconciliationCaseRequest
{
    [Required]
    public byte[] RowVersion { get; init; } = [];
}
