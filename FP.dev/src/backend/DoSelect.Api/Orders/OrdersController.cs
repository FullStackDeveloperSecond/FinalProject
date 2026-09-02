using System.Diagnostics;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Api.Shopping;
using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Application.Members;
using DoSelect.Application.Orders;
using DoSelect.Application.Payments;
using DoSelect.Domain.Payments;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoSelect.Api.Orders;

/// <summary>
/// Order list remains member-only. Detail and cancellation accept either an authenticated member
/// or a GuestOrderAccess Cookie that has been validated against the exact target order.
/// </summary>
[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController : ControllerBase
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private readonly IOrderService _orderService;
    private readonly GuestOrderAccessScopeAuthorizer _guestAuthorizer;
    private readonly CheckoutService _checkoutService;
    private readonly IMemberProfileGateway _memberProfileGateway;
    private readonly IPaymentAttemptWriter _paymentAttemptWriter;
    private readonly LatestPaymentAttemptService _latestPaymentAttempts;

    public OrdersController(
        IOrderService orderService,
        GuestOrderAccessScopeAuthorizer guestAuthorizer,
        CheckoutService checkoutService,
        IMemberProfileGateway memberProfileGateway,
        IPaymentAttemptWriter paymentAttemptWriter,
        LatestPaymentAttemptService latestPaymentAttempts)
    {
        _orderService = orderService;
        _guestAuthorizer = guestAuthorizer;
        _checkoutService = checkoutService;
        _memberProfileGateway = memberProfileGateway;
        _paymentAttemptWriter = paymentAttemptWriter;
        _latestPaymentAttempts = latestPaymentAttempts;
    }

    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = IdempotencyKeyHeaderName), BindRequired]
        [StringLength(128, MinimumLength = 1)]
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            ModelState.AddModelError(
                "idempotencyKey",
                $"{IdempotencyKeyHeaderName} is required.");
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState));
        }

        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                detail: $"A member session or the '{CartIdentityResolver.GuestCartKeyHeaderName}' header is required.");
            return BadRequest(problem);
        }

        CheckoutActor actor;
        if (identity.MemberUserId is { } memberUserId)
        {
            var profile = await _memberProfileGateway.GetProfileAsync(memberUserId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The authenticated member does not have the profile required for Checkout.");
            actor = CheckoutActor.ForMember(memberUserId, profile.PublicId);
        }
        else
        {
            actor = CheckoutActor.ForGuest(identity.GuestCartKey!);
        }

        var result = await _checkoutService.CreateOrderAsync(
            actor,
            request,
            idempotencyKey,
            cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }

    [HttpPost("{id:guid}/payment-attempts")]
    [ProducesResponseType<PaymentAttemptDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentAttemptDto>> CreatePaymentAttempt(
        Guid id,
        [FromBody] CreatePaymentAttemptRequest request,
        [FromHeader(Name = IdempotencyKeyHeaderName), BindRequired]
        [StringLength(128, MinimumLength = 1)]
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            ModelState.AddModelError(
                "idempotencyKey",
                $"{IdempotencyKeyHeaderName} is required.");
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState));
        }

        var resolution = await ResolveActorAsync(id, cancellationToken);
        if (resolution.Actor is null)
        {
            return resolution.HadAuthenticatedCookie ? OrderNotFound() : Unauthorized();
        }

        var result = await _paymentAttemptWriter.CreateAsync(
            new CreatePaymentAttemptCommand(
                id,
                request.Method,
                request.OrderRowVersion,
                idempotencyKey,
                resolution.Actor),
            cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }

    /// <summary>
    /// 這張訂單最新的一筆付款嘗試，供付款頁重新整理後恢復畫面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>包含所有終態</b>（<c>Failed</c>／<c>Expired</c>／<c>Cancelled</c>／<c>Paid</c>）
    /// —— alex 2026-09-01 Issue #86 A1。終態當成「沒有付款嘗試」的話，使用者付款失敗後
    /// 重新整理就再也看不到失敗原因，只會回到一張空的建立表單。
    /// </para>
    /// <para>
    /// 授權沿用發票查詢的語意（同 Issue #86 C1）：會員不是擁有者時<b>不立即拒絕</b>，
    /// 仍讓同一個瀏覽器中有效的 Guest token 證明權限；Guest Scope 過期回 401
    /// <c>guest_order_access_expired</c>，Scope 不符與資源不存在一律折成 404。
    /// </para>
    /// </remarks>
    [HttpGet("{id:guid}/payment-attempts/latest")]
    [ProducesResponseType<PaymentAttemptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentAttemptDto>> GetLatestPaymentAttempt(
        Guid id,
        CancellationToken cancellationToken)
    {
        var hadMember = false;
        var member = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (member.Succeeded &&
            member.Principal?.HasClaim(
                DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member) == true)
        {
            hadMember = true;
            var memberUserId = member.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                throw new InvalidOperationException(
                    "Authenticated member request is missing its identifier claim.");
            }

            var memberResult = await _latestPaymentAttempts.FindLatestAsync(
                new PaymentAttemptViewer.Member(memberUserId), id, cancellationToken);
            switch (memberResult)
            {
                case LatestPaymentAttemptResult.Found found:
                    return Ok(found.Attempt);
                case LatestPaymentAttemptResult.NotFound:
                    return PaymentAttemptNotFound();
                case LatestPaymentAttemptResult.MemberAccessDenied:
                    // 同一個瀏覽器可以同時有 Member 與 Guest cookie。會員不是擁有者時，
                    // 仍要讓有效的 Guest token 證明它對這張訪客訂單有權限。
                    break;
            }
        }

        var guest = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.GuestOrderAccess);
        if (!guest.Succeeded || guest.Principal is null)
        {
            return hadMember ? PaymentAttemptNotFound() : UnauthorizedPaymentAttempt();
        }

        var authorization = await _guestAuthorizer.AuthorizeAsync(
            guest.Principal,
            id,
            new GuestOrderAccessAuthorizationAuditContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);

        if (authorization is GuestOrderAccessAuthorizationResult.Failure failure)
        {
            return failure.ErrorCode == GuestOrderErrorCodes.AccessExpired
                ? UnauthorizedPaymentAttempt(GuestOrderErrorCodes.AccessExpired)
                : PaymentAttemptNotFound(GuestOrderErrorCodes.ScopeMismatch);
        }

        var guestResult = await _latestPaymentAttempts.FindLatestAsync(
            new PaymentAttemptViewer.Guest(), id, cancellationToken);
        return guestResult is LatestPaymentAttemptResult.Found guestFound
            ? Ok(guestFound.Attempt)
            : PaymentAttemptNotFound();
    }

    private ActionResult PaymentAttemptNotFound(
        string code = PaymentErrorCodes.ResourceNotFound) =>
        NotFound(ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status404NotFound,
            code,
            detail: "The referenced payment attempt was not found."));

    private ActionResult UnauthorizedPaymentAttempt(
        string code = PaymentErrorCodes.ResourceNotFound) =>
        Unauthorized(ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status401Unauthorized,
            code,
            detail: "The request requires an authenticated owner or a valid guest order token."));

    [HttpGet]
    [Authorize(Policy = DoSelectPolicies.Member)]
    public async Task<ActionResult<PageResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] int pageNumber,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var memberUserId = RequireMemberUserId();
        var query = new OrderQuery(
            pageNumber == 0 ? 1 : pageNumber,
            pageSize == 0 ? 20 : pageSize);
        var result = await _orderService.GetOrdersAsync(memberUserId, query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var resolution = await ResolveActorAsync(id, cancellationToken);
        if (resolution.Actor is null)
        {
            return resolution.HadAuthenticatedCookie ? OrderNotFound() : Unauthorized();
        }

        try
        {
            var order = await _orderService.GetOrderAsync(resolution.Actor, id, cancellationToken);
            return Ok(order);
        }
        catch (OrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/actions/cancel")]
    public async Task<ActionResult<OrderDto>> CancelOrder(
        Guid id,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveActorAsync(id, cancellationToken);
        if (resolution.Actor is null)
        {
            return resolution.HadAuthenticatedCookie ? OrderNotFound() : Unauthorized();
        }

        try
        {
            var auditContext = new OrderCancellationAuditContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress);
            var order = await _orderService.CancelOrderAsync(
                resolution.Actor,
                id,
                request,
                auditContext,
                cancellationToken);
            return Ok(order);
        }
        catch (OrderWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>
    /// [Authorize(Policy = Member)] on the class already guarantees an authenticated member
    /// principal with this claim, so this never actually falls through to
    /// <see cref="UnauthorizedResult"/> in production — it exists only so a missing claim fails
    /// loudly instead of NullReferenceException-ing deeper in the service.
    /// </summary>
    private string RequireMemberUserId()
    {
        var memberUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            throw new InvalidOperationException("Authenticated member request is missing its identifier claim.");
        }

        return memberUserId;
    }

    private async Task<OrderActorResolution> ResolveActorAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        var memberAuthentication = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.Member);
        if (memberAuthentication.Succeeded &&
            memberAuthentication.Principal?.HasClaim(
                DoSelectClaimTypes.AccountType,
                DoSelectClaimValues.Member) == true)
        {
            var memberUserId = memberAuthentication.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                throw new InvalidOperationException(
                    "Authenticated member request is missing its identifier claim.");
            }

            return new OrderActorResolution(new OrderActor.Member(memberUserId), true);
        }

        var guestAuthentication = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.GuestOrderAccess);
        if (!guestAuthentication.Succeeded || guestAuthentication.Principal is null)
        {
            return new OrderActorResolution(Actor: null, HadAuthenticatedCookie: false);
        }

        var authorization = await _guestAuthorizer.AuthorizeAsync(
            guestAuthentication.Principal,
            orderPublicId,
            new GuestOrderAccessAuthorizationAuditContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);
        return authorization is GuestOrderAccessAuthorizationResult.Success success
            ? new OrderActorResolution(new OrderActor.Guest(success.TokenPublicId), true)
            : new OrderActorResolution(Actor: null, HadAuthenticatedCookie: true);
    }

    private ActionResult OrderNotFound()
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status404NotFound,
            OrderWriteException.ErrorCodes.ResourceNotFound,
            detail: "The referenced order was not found.");
        return NotFound(problem);
    }

    private sealed record OrderActorResolution(OrderActor? Actor, bool HadAuthenticatedCookie);
}
