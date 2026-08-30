using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Configuration;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Payments;
using DoSelect.Domain.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.Payments;

/// <summary>
/// 展示用的模擬付款完成端點（`API Endpoint目錄` 第 78 行）。
/// </summary>
/// <remarks>
/// <para>
/// <b>只在 Demo Profile 開放。</b><c>Demo:SimulationEndpointsEnabled</c> 為 false 時回 404：
/// 這個資源在這個 Profile 下就是不存在。<c>DemoOptionsValidator</c> 已經限制該設定
/// 只有 Demo 環境能開成 true，所以正式環境一定關著。
/// </para>
/// <para>
/// 這道關卡<b>不宣稱藏起端點的存在</b>：<c>[Authorize]</c> 比 Action 早執行，未登入的
/// 呼叫者會先拿到 401 而不是 404，而且這條路由本來就寫在公開的 OpenAPI 文件裡。
/// 它要保證的是「在 Demo 以外沒有作用」，不是「沒有人知道它在」——
/// 與 <c>AiSupportController</c> 對 <c>Features:AiEnabled</c> 的做法同一層。
/// </para>
/// <para>
/// 授權是<b>訂單擁有者</b>。擁有者比對在 Writer 的交易內做，這裡只解析身分 ——
/// 比對如果留在這裡，就會變成一份跟實際寫入不同步的平行判斷。
/// </para>
/// </remarks>
[ApiController]
[Authorize(AuthenticationSchemes = DoSelectAuthenticationSchemes.Member)]
[Route("api/v1/simulated-payments")]
public sealed class SimulatedPaymentsController : ControllerBase
{
    private readonly ISimulatedPaymentWriter _writer;
    private readonly DemoOptions _demoOptions;

    public SimulatedPaymentsController(
        ISimulatedPaymentWriter writer,
        IOptions<DemoOptions> demoOptions)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(demoOptions);

        _writer = writer;
        _demoOptions = demoOptions.Value;
    }

    [HttpPost("{attemptId:guid}/actions/complete")]
    [ProducesResponseType<PaymentAttemptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentAttemptDto>> Complete(
        Guid attemptId,
        [FromBody] CompleteSimulatedPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!_demoOptions.SimulationEndpointsEnabled)
        {
            return NotFound(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                PaymentErrorCodes.ResourceNotFound,
                detail: "The requested resource was not found."));
        }

        var memberUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            throw DomainProblemException.Validation("The member identity is required.");
        }

        var traceId = Activity.Current?.TraceId.ToString()
            ?? ActivityTraceId.CreateRandom().ToString();
        var result = await _writer.CompleteAsync(
            new CompleteSimulatedPaymentCommand(
                attemptId,
                request.Outcome,
                request.SimulationKey,
                memberUserId,
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                traceId,
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);

        return StatusCode(result.StatusCode, result.Body);
    }
}
