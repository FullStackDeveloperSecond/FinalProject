using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Invoicing;
using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Invoicing;

/// <summary>
/// 前台查詢一張訂單的模擬發票（`API Endpoint目錄` 第 74 行）。
/// </summary>
/// <remarks>
/// <para>
/// 會員與訪客都可以看自己的訂單。訪客走既有的
/// <see cref="GuestOrderAccessScopeAuthorizer"/> —— alex 2026-08-29 Issue #65：
/// <b>直接注入重用，不要再建立 wrapper 或平行的 Guest validator</b>。
/// 那套已經涵蓋 token hash 的資料庫重查、過期／撤銷、scope mismatch、違規次數與中央 Audit。
/// </para>
/// <para>
/// 「找不到」與「不是你的」對外<b>折疊成同一個 404</b>：分開回答等於告訴外人這個 id 存在。
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/orders/{orderId:guid}/invoice")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class InvoicesController : ControllerBase
{
    private readonly InvoiceQueryService _invoices;
    private readonly GuestOrderAccessScopeAuthorizer _guestAuthorizer;

    public InvoicesController(
        InvoiceQueryService invoices,
        GuestOrderAccessScopeAuthorizer guestAuthorizer)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(guestAuthorizer);

        _invoices = invoices;
        _guestAuthorizer = guestAuthorizer;
    }

    [HttpGet]
    [ProducesResponseType<SimulatedInvoiceDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SimulatedInvoiceDto>> GetInvoice(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveViewerAsync(orderId, cancellationToken);
        if (resolution.Viewer is null)
        {
            // 完全沒有身分 → 401；有身分但這張訂單不是他的 → 404。
            return resolution.HadAuthenticatedCookie ? NotFoundProblem() : Unauthorized();
        }

        var invoice = await _invoices.FindForOrderAsync(
            resolution.Viewer, orderId, cancellationToken);

        return invoice is null ? NotFoundProblem() : Ok(invoice);
    }

    private async Task<ViewerResolution> ResolveViewerAsync(
        Guid orderPublicId, CancellationToken cancellationToken)
    {
        var member = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (member.Succeeded &&
            member.Principal?.HasClaim(
                DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member) == true)
        {
            var memberUserId = member.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                throw new InvalidOperationException(
                    "Authenticated member request is missing its identifier claim.");
            }

            // 擁有者比對在 Application 層做，這裡只解析身分。
            return new ViewerResolution(new InvoiceViewer.Member(memberUserId), true);
        }

        var guest = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.GuestOrderAccess);
        if (!guest.Succeeded || guest.Principal is null)
        {
            return new ViewerResolution(Viewer: null, HadAuthenticatedCookie: false);
        }

        var authorization = await _guestAuthorizer.AuthorizeAsync(
            guest.Principal,
            orderPublicId,
            new GuestOrderAccessAuthorizationAuditContext(
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                HttpContext.Connection.RemoteIpAddress),
            cancellationToken);

        return authorization is GuestOrderAccessAuthorizationResult.Success
            ? new ViewerResolution(new InvoiceViewer.Guest(), true)
            : new ViewerResolution(Viewer: null, HadAuthenticatedCookie: true);
    }

    private ActionResult NotFoundProblem() =>
        NotFound(ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status404NotFound,
            InvoiceErrorCodes.ResourceNotFound,
            detail: "The referenced invoice was not found."));

    private sealed record ViewerResolution(InvoiceViewer? Viewer, bool HadAuthenticatedCookie);
}
