using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Refunds;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Refunds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoSelect.Api.Refunds;

/// <summary>
/// 後台退款操作。權限為 <see cref="DoSelectPolicies.RefundExecute"/>，
/// 該 Policy 已要求 FinanceManager／SuperAdmin 角色與 MFA 宣告，
/// 因此 TOTP 二次確認由授權層保證，Controller 不再自行檢查。
/// </summary>
[ApiController]
[Route("api/v1/admin/refunds")]
[Authorize(Policy = DoSelectPolicies.RefundExecute)]
public sealed class RefundsController(
    IRefundExecutor refundExecutor,
    IRefundReader refundReader) : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    /// <summary>查詢後台退款清單（A-21）。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PageResult<RefundDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PageResult<RefundDto>>> List(
        [FromQuery] AdminRefundListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await refundReader.ListAsync(
            new AdminRefundQuery(
                request.Statuses,
                request.FromUtc,
                request.ToUtc,
                request.Q,
                request.PageNumber,
                request.PageSize),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>讀取退款與可信分攤明細（A-22）。</summary>
    [HttpGet("{refundPublicId:guid}")]
    [ProducesResponseType(typeof(RefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RefundDto>> GetById(
        Guid refundPublicId,
        CancellationToken cancellationToken)
    {
        var refund = await refundReader.FindByPublicIdAsync(
            refundPublicId, cancellationToken);
        if (refund is null)
        {
            return Problem(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound));
        }

        return Ok(refund);
    }

    /// <summary>
    /// 執行一筆已核准的退款。核准與執行是不同的 Use Case，本端點不做核准。
    /// 相同 Idempotency-Key 重送回同一結果；不同金鑰不會對已完成的退款產生第二次副作用。
    /// </summary>
    [HttpPost("{refundPublicId:guid}/actions/execute")]
    [ProducesResponseType(typeof(RefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RefundDto>> Execute(
        Guid refundPublicId,
        [FromHeader(Name = IdempotencyKeyHeaderName), BindRequired]
        [StringLength(128, MinimumLength = 1)]
        string idempotencyKey,
        [FromBody] ExecuteRefundRequestBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            ModelState.AddModelError(
                "idempotencyKey",
                $"{IdempotencyKeyHeaderName} is required.");
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState));
        }

        // rowversion 的 8 bytes 檢查由 Body 上的 [RowVersionRequired] 負責，
        // [ApiController] 會把它變成 400 validation_failed。Application 層的同一條
        // 檢查是丟 ArgumentException —— 那個例外沒有專屬 handler，會落到
        // GlobalExceptionHandler 變成 500，因此必須在這之前就擋掉。
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState));
        }

        var executedBy = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(executedBy))
        {
            return Problem(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.AuthorizationForbidden));
        }

        // CorrelationId 與 TraceId 不是同一個值。CorrelationIdMiddleware 會把合法的
        // X-Correlation-ID 寫進 TraceIdentifier，而那不是中央 Audit 要求的 32 位
        // W3C TraceId —— 兩者混用會讓稽核建構失敗，把一次正常退款變成 500。
        var result = await refundExecutor.ExecuteAsync(
            new ExecuteRefundRequest(
                refundPublicId,
                body.RefundRowVersion,
                idempotencyKey,
                executedBy,
                body.ReasonCode,
                body.Note,
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);

        if (result.ErrorCode is { } errorCode)
        {
            return Problem(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodeFor(errorCode),
                errorCode));
        }

        // 首次執行與重播都回同一份正式 RefundDto，呼叫端不需要分辨 ——
        // 重播代表副作用已經發生過一次，狀態與金額都與首次相同。
        var refund = await refundReader.FindByPublicIdAsync(refundPublicId, cancellationToken);
        if (refund is null)
        {
            return Problem(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound));
        }

        return Ok(refund);
    }

    private static int StatusCodeFor(string errorCode) => errorCode switch
    {
        RefundErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status409Conflict,
    };

    private ObjectResult Problem(Microsoft.AspNetCore.Mvc.ProblemDetails problemDetails) =>
        StatusCode(problemDetails.Status ?? StatusCodes.Status409Conflict, problemDetails);
}

public sealed record AdminRefundListRequest
{
    public IReadOnlyList<RefundStatus>? Statuses { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    public string? Q { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

/// <summary>
/// 執行退款的 Request Body。
/// </summary>
/// <remarks>
/// 刻意沒有 <c>allocations</c> 或任何金額欄位（DEC-P287）：分攤一律由後端依可信交易
/// 快照產生。<c>reasonCode</c> 與 <c>note</c> 只寫進中央 AuditLog，不存回 Refund。
/// </remarks>
public sealed record ExecuteRefundRequestBody
{
    // safe-code 檢查與中央 Audit 共用同一份規則。只有 [StringLength] 不夠：
    // 含空白或中文的理由碼長度合法，卻會在寫稽核時丟 ArgumentException 變成 500。
    [Required]
    [StringLength(64, MinimumLength = 1)]
    [AuditSafeReason]
    public required string ReasonCode { get; init; }

    // 同上：note 有自己的禁用字元與敏感詞規則，違反時一樣是 500 而不是 400。
    [StringLength(1000)]
    [AuditSafeNote]
    public string? Note { get; init; }

    // 專案共用的 rowversion 驗證（8 bytes）。[Required] 不夠：record 的 byte[] 預設是
    // 非 null 的空陣列，完全省略欄位仍會通過，然後在樂觀鎖比對時變成誤導的 409。
    [RowVersionRequired]
    public required byte[] RefundRowVersion { get; init; }
}
