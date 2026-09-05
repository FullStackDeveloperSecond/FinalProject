using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Application.Auditing;
using DoSelect.Api.Security;
using DoSelect.Application.Orders;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoSelect.Api.Shipping;

/// <summary>
/// UC-ADM-SHIP-02 批次出貨（A-16 頁）。Policy 是 ShippingManage（OrderManager／SuperAdmin），
/// 與 Endpoint 目錄那一列一致。
/// </summary>
[ApiController]
[Route("api/v1/admin/shipments")]
public sealed class AdminShipmentsController : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private readonly IBatchShipmentService _service;
    private readonly IShipmentStatusService _statusService;
    private readonly TimeProvider _timeProvider;

    public AdminShipmentsController(
        IBatchShipmentService service,
        IShipmentStatusService statusService,
        TimeProvider timeProvider)
    {
        _service = service;
        _statusService = statusService;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// M-11 物流狀態命令（組長 2026-09-04 裁定 A1）：in-transit／delivered／pickup-ready／picked-up／
    /// delivery-failed／returned。必帶 Idempotency-Key；同鍵同 payload 不重複副作用、重播回傳目前最新的
    /// AdminOrderDto（不是第一次的快照），不同 payload 回 idempotency_payload_conflict（GlobalExceptionHandler 轉 409）。狀態轉移、歷程、Order 投影、COD
    /// 收款、Completed、Audit 與 Outbox 同一交易（B1）。成功回傳更新後的 AdminOrderDto（C1）。
    /// </summary>
    // 路由參數不能叫 `action`（ASP.NET Core 路由保留值，會撞 controller/action 環境路由值而 404）。
    [HttpPost("{shipmentPublicId:guid}/actions/{shipmentAction}")]
    [Authorize(Policy = DoSelectPolicies.ShippingManage)]
    [ProducesResponseType<AdminOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<AdminOrderDto>> ExecuteStatusAction(
        Guid shipmentPublicId,
        string shipmentAction,
        [FromHeader(Name = IdempotencyKeyHeaderName), BindRequired]
        [StringLength(128, MinimumLength = 1)]
        string idempotencyKey,
        [FromBody] ShipmentStatusActionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            ModelState.AddModelError("idempotencyKey", $"{IdempotencyKeyHeaderName} is required.");
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState));
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await _statusService.ExecuteAsync(
            new ShipmentStatusCommand(
                shipmentPublicId,
                shipmentAction,
                request.ShipmentRowVersion,
                request.ReasonCode,
                request.Note,
                idempotencyKey),
            adminUserId,
            BuildAuditContext(),
            cancellationToken);
        return Ok(result.Order);
    }

    /// <summary>
    /// 逐筆獨立交易，回傳每一筆的成功或穩定錯誤碼。整個 Request 只有幾種會整批失敗的情況：
    /// 超過 100 筆（`shipping_batch_limit_exceeded`）、請求本身不合法，以及冪等鍵的衝突
    /// （`idempotency_payload_conflict`、`idempotency_request_in_progress`，由 GlobalExceptionHandler
    /// 轉成 409）。同一把鍵重送同一份請求會重播上一次的逐筆結果，不會再出一次貨。
    /// </summary>
    [HttpPost("batches")]
    [Authorize(Policy = DoSelectPolicies.ShippingManage)]
    [ProducesResponseType<BatchShipmentResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<BatchShipmentResultDto>> ShipBatch(
        [FromBody] AdminBatchShipmentRequest request,
        CancellationToken cancellationToken)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await _service.ShipBatchAsync(
            new BatchShipmentRequest(
                (request.Orders ?? []).Select(order =>
                    new BatchShipmentOrderInput(order.OrderPublicId, order.RowVersion)).ToArray(),
                request.ShippingAction ?? string.Empty,
                request.IdempotencyKey ?? string.Empty),
            adminUserId,
            BuildAuditContext(),
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        return Ok(result);
    }

    private AuditRequestContext BuildAuditContext()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new AuditRequestContext(
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
    }
}

/// <summary>`BatchShipmentRequest`（API DTO與Schema契約）的 HTTP 形狀。</summary>
public sealed class AdminBatchShipmentRequest
{
    // 上限刻意不用 [MaxLength(100)]：模型繫結的驗證會搶在服務之前回 validation_failed，而
    // API錯誤碼目錄指名這個情況要回 shipping_batch_limit_exceeded。筆數由服務把關。
    [Required]
    public IReadOnlyList<AdminBatchShipmentOrder>? Orders { get; init; }

    [Required]
    [StringLength(32)]
    public string? ShippingAction { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string? IdempotencyKey { get; init; }
}

public sealed class AdminBatchShipmentOrder
{
    public Guid OrderPublicId { get; init; }

    [Required]
    public byte[] RowVersion { get; init; } = [];
}
