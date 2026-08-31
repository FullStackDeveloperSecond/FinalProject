using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Configuration;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Payments;
using DoSelect.Application.Orders;
using DoSelect.Domain.Payments;
using Microsoft.AspNetCore.Authentication;
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
/// 這道關卡<b>不宣稱藏起端點的存在</b>：Action 會先嘗試 Member 與 Guest 兩種票證，未登入的
/// 呼叫者會拿到 401；已有有效票證但 Profile 未開啟時才回 404。路由本來就寫在公開的 OpenAPI 文件裡。
/// 它要保證的是「在 Demo 以外沒有作用」，不是「沒有人知道它在」——
/// 與 <c>AiSupportController</c> 對 <c>Features:AiEnabled</c> 的做法同一層。
/// </para>
/// <para>
/// 授權是<b>訂單擁有者</b>。擁有者比對在 Writer 的交易內做，這裡只解析身分 ——
/// 比對如果留在這裡，就會變成一份跟實際寫入不同步的平行判斷。
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/simulated-payments")]
public sealed class SimulatedPaymentsController : ControllerBase
{
    private readonly ISimulatedPaymentWriter _writer;
    private readonly ISimulatedPaymentAuthorizationReader _authorizationReader;
    private readonly GuestOrderAccessScopeAuthorizer _guestAuthorizer;
    private readonly DemoOptions _demoOptions;

    public SimulatedPaymentsController(
        ISimulatedPaymentWriter writer,
        ISimulatedPaymentAuthorizationReader authorizationReader,
        GuestOrderAccessScopeAuthorizer guestAuthorizer,
        IOptions<DemoOptions> demoOptions)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(authorizationReader);
        ArgumentNullException.ThrowIfNull(guestAuthorizer);
        ArgumentNullException.ThrowIfNull(demoOptions);

        _writer = writer;
        _authorizationReader = authorizationReader;
        _guestAuthorizer = guestAuthorizer;
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
        var member = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        var guest = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.GuestOrderAccess);
        if (!member.Succeeded && !guest.Succeeded)
        {
            return UnauthorizedProblem();
        }

        if (!_demoOptions.SimulationEndpointsEnabled)
        {
            return NotFound(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                PaymentErrorCodes.ResourceNotFound,
                detail: "The requested resource was not found."));
        }

        var traceId = Activity.Current?.TraceId.ToString()
            ?? ActivityTraceId.CreateRandom().ToString();
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(HttpContext);
        var reference = await _authorizationReader.FindOrderAsync(attemptId, cancellationToken);
        if (reference is null)
        {
            return NotFoundProblem();
        }

        var actor = await ResolveActorAsync(
            reference,
            member,
            guest,
            correlationId,
            traceId,
            cancellationToken);
        if (actor.Result is { } failure)
        {
            return failure;
        }

        var result = await _writer.CompleteAsync(
            new CompleteSimulatedPaymentCommand(
                attemptId,
                request.Outcome,
                request.SimulationKey,
                actor.Value!,
                correlationId,
                traceId,
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);

        return StatusCode(result.StatusCode, result.Body);
    }

    private async Task<(SimulatedPaymentActor? Value, ActionResult? Result)> ResolveActorAsync(
        SimulatedPaymentOrderReference reference,
        AuthenticateResult member,
        AuthenticateResult guest,
        string correlationId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var memberUserId = member.Succeeded
            ? member.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        if (!string.IsNullOrWhiteSpace(memberUserId) &&
            string.Equals(reference.MemberUserId, memberUserId, StringComparison.Ordinal))
        {
            return (new SimulatedPaymentActor.Member(memberUserId), null);
        }

        if (!guest.Succeeded || guest.Principal is null)
        {
            return (null, member.Succeeded ? NotFoundProblem() : UnauthorizedProblem());
        }

        var authorization = await _guestAuthorizer.AuthorizeAsync(
            guest.Principal,
            reference.OrderPublicId,
            new GuestOrderAccessAuthorizationAuditContext(
                correlationId,
                traceId,
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);
        return authorization switch
        {
            GuestOrderAccessAuthorizationResult.Success success =>
                (new SimulatedPaymentActor.Guest(
                    success.TokenPublicId,
                    success.OrderPublicId), null),
            GuestOrderAccessAuthorizationResult.Failure failure
                when failure.ErrorCode == GuestOrderErrorCodes.AccessExpired =>
                (null, UnauthorizedProblem(GuestOrderErrorCodes.AccessExpired)),
            GuestOrderAccessAuthorizationResult.Failure =>
                (null, NotFoundProblem(GuestOrderErrorCodes.ScopeMismatch)),
            _ => throw new InvalidOperationException("Unknown guest authorization result."),
        };
    }

    private ActionResult NotFoundProblem(string code = PaymentErrorCodes.ResourceNotFound) =>
        NotFound(ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status404NotFound,
            code,
            detail: "The payment attempt was not found."));

    private ActionResult UnauthorizedProblem(string code = ApiErrorCodes.AuthenticationRequired) =>
        Unauthorized(ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status401Unauthorized,
            code));
}
