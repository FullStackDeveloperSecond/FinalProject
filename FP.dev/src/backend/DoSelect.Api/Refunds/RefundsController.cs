using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        [FromHeader(Name = IdempotencyKeyHeaderName)] string? idempotencyKey,
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

        // SQL Server 的 rowversion 固定 8 bytes。在 transport 邊界擋下 —— Application 層
        // 的同一條檢查是丟 ArgumentException，而那個例外沒有專屬 handler，會落到
        // GlobalExceptionHandler 變成 500 unexpected_error，但呼叫端只是送錯了長度。
        if (body is null || body.RefundRowVersion is not { Length: 8 })
        {
            ModelState.AddModelError(
                nameof(ExecuteRefundRequestBody.RefundRowVersion),
                "refundRowVersion must be an 8-byte value.");
        }

        if (body is null || !ModelState.IsValid)
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

/// <summary>
/// 執行退款的 Request Body。
/// </summary>
/// <remarks>
/// 刻意沒有 <c>allocations</c> 或任何金額欄位（DEC-P287）：分攤一律由後端依可信交易
/// 快照產生。<c>reasonCode</c> 與 <c>note</c> 只寫進中央 AuditLog，不存回 Refund。
/// </remarks>
public sealed record ExecuteRefundRequestBody
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string ReasonCode { get; init; }

    [StringLength(1000)]
    public string? Note { get; init; }

    [Required]
    public required byte[] RefundRowVersion { get; init; }
}

