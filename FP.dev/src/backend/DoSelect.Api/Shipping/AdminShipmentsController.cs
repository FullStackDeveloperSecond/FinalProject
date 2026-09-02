using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Application.Auditing;
using DoSelect.Api.Security;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>
/// UC-ADM-SHIP-02 批次出貨（A-16 頁）。Policy 是 ShippingManage（OrderManager／SuperAdmin），
/// 與 Endpoint 目錄那一列一致。
/// </summary>
[ApiController]
[Route("api/v1/admin/shipments")]
public sealed class AdminShipmentsController : ControllerBase
{
    private readonly IBatchShipmentService _service;
    private readonly TimeProvider _timeProvider;

    public AdminShipmentsController(IBatchShipmentService service, TimeProvider timeProvider)
    {
        _service = service;
        _timeProvider = timeProvider;
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
