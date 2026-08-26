using System.ComponentModel.DataAnnotations;
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
public sealed class RefundsController(IRefundExecutor refundExecutor) : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    /// <summary>
    /// 執行一筆已核准的退款。核准與執行是不同的 Use Case，本端點不做核准。
    /// 相同 Idempotency-Key 重送回同一結果；不同金鑰不會對已完成的退款產生第二次副作用。
    /// </summary>
    [HttpPost("{refundPublicId:guid}/actions/execute")]
    [ProducesResponseType(typeof(RefundExecutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RefundExecutionResponse>> Execute(
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

        var result = await refundExecutor.ExecuteAsync(
            new ExecuteRefundRequest(
                refundPublicId,
                idempotencyKey,
                executedBy,
                body.ReasonCode,
                body.Note,
                HttpContext.TraceIdentifier,
                HttpContext.TraceIdentifier),
            cancellationToken);

        if (result.ErrorCode is { } errorCode)
        {
            return Problem(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodeFor(errorCode),
                errorCode));
        }

        // 重播與首次執行都回 200 與同一份結果，呼叫端不需要分辨。
        return Ok(new RefundExecutionResponse(
            refundPublicId,
            result.SettledAmount ?? result.Plan!.Amount,
            result.IsReplay));
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

/// <summary>
/// 退款執行結果。<paramref name="Replayed"/> 為 <c>true</c> 表示這次請求命中既有結果，
/// 沒有再產生一次金流副作用。
/// </summary>
public sealed record RefundExecutionResponse(
    Guid RefundPublicId,
    decimal SettledAmount,
    bool Replayed);
