using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Returns;
using DoSelect.Application.Support;
using DoSelect.Infrastructure.Persistence.Returns;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using ReturnFileStorage = DoSelect.Application.Files.IPrivateFileStorage;

namespace DoSelect.Api.Controllers;

/// <summary>
/// Generic private-attachment content route shared by every private-attachment domain (客服/
/// 退貨), per 檔案與圖片儲存設計.md. Resolves Support-ticket attachments first (unchanged actor/
/// authorization logic — see the many PrivateAttachmentsHttpAcceptanceTests scenarios this must
/// keep passing), then falls back to Return attachments (unchanged Member/Admin/Guest-cookie
/// authorization logic that used to live on its own, now-removed, colliding
/// PrivateReturnAttachmentsController). Neither domain's resolution/authorization code changed —
/// only the routing point where they meet did. Every unresolved/unauthorized id converges on the
/// same DomainProblemException.NotFound, from either domain, so a caller can never learn which
/// domain (or whether any domain) an id belongs to.
///
/// This endpoint intentionally does not use [Authorize]. A declarative policy that lists both
/// the Member and Admin cookie schemes would let ASP.NET Core merge both principals into one
/// ClaimsPrincipal when a caller happens to carry both cookies, and any anonymous/forbidden
/// failure would surface as the cookie handlers' shared 401/403 (configured for every other
/// endpoint in DoSelectSecurityConstants). Neither fits here: the contract requires anonymous,
/// wrong-member, and disallowed-admin-role requests to be indistinguishable from a missing
/// attachment. So each scheme is authenticated separately below and only its own, unmerged
/// principal is inspected — there is no merged principal for a "coherent NameIdentifier" bug to
/// hide in — and every rejection path converges on the same DomainProblemException.NotFound.
/// </summary>
[ApiController]
[Route("api/v1/private-attachments")]
public sealed class PrivateAttachmentsController : ControllerBase
{
    private const string NotFoundMessage = "The attachment was not found.";
    private const string FallbackDownloadFileName = "attachment";
    private const string FallbackContentType = "application/octet-stream";

    private readonly ISupportAttachmentReadService _supportService;
    private readonly IReturnStore _returnStore;
    private readonly ReturnFileStorage _returnFileStorage;

    public PrivateAttachmentsController(
        ISupportAttachmentReadService supportService, IReturnStore returnStore, ReturnFileStorage returnFileStorage)
    {
        _supportService = supportService;
        _returnStore = returnStore;
        _returnFileStorage = returnFileStorage;
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken)
    {
        var content = await TryResolveSupportAttachmentAsync(id, cancellationToken)
            ?? await TryResolveReturnAttachmentAsync(id, cancellationToken);

        if (content is null)
        {
            throw DomainProblemException.NotFound(NotFoundMessage);
        }

        try
        {
            return File(
                content.Content,
                SanitizeContentType(content.ContentType),
                SanitizeDownloadFileName(content.DownloadFileName),
                enableRangeProcessing: false);
        }
        catch
        {
            await content.Content.DisposeAsync();
            throw;
        }
    }

    private async Task<PrivateAttachmentContent?> TryResolveSupportAttachmentAsync(Guid id, CancellationToken cancellationToken)
    {
        var actors = await ResolveSupportActorsAsync(cancellationToken);
        foreach (var actor in actors)
        {
            try
            {
                return await _supportService.GetContentAsync(actor, id, cancellationToken);
            }
            catch (DomainProblemException exception) when (
                exception.StatusCode == StatusCodes.Status404NotFound)
            {
                // Each principal completes the full resource authorization independently.
                // A denial for one identity must not prevent another valid identity from reading.
            }
        }

        return null;
    }

    private async Task<PrivateAttachmentContent?> TryResolveReturnAttachmentAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = await _returnStore.FindAttachmentAccessAsync(id, cancellationToken);
        if (access is null || !await IsAuthorizedForReturnAsync(access, cancellationToken))
        {
            return null;
        }

        var stream = await _returnFileStorage.OpenReadAsync(access.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        return new PrivateAttachmentContent(stream, access.ContentType, access.OriginalFileName);
    }

    private async Task<bool> IsAuthorizedForReturnAsync(ReturnAttachmentAccess access, CancellationToken cancellationToken)
    {
        var memberAuth = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (memberAuth.Succeeded &&
            memberAuth.Principal?.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member) == true)
        {
            var memberUserId = memberAuth.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(memberUserId) && memberUserId == access.MemberUserId)
            {
                return true;
            }
        }

        var adminAuth = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Admin);
        if (adminAuth.Succeeded &&
            adminAuth.Principal?.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin) == true &&
            adminAuth.Principal.HasClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor) &&
            (adminAuth.Principal.IsInRole(DoSelectRoles.OrderManager) || adminAuth.Principal.IsInRole(DoSelectRoles.SuperAdmin)))
        {
            return true;
        }

        if (access.MemberUserId is null &&
            HttpContext.Request.Cookies.TryGetValue(GuestOrderAccessValidator.GuestOrderAccessCookieName, out var rawToken) &&
            !string.IsNullOrWhiteSpace(rawToken))
        {
            var guestValidator = HttpContext.RequestServices.GetRequiredService<IGuestOrderAccessValidator>();
            var timeProvider = HttpContext.RequestServices.GetRequiredService<TimeProvider>();
            var validatedOrderId = await guestValidator.ValidateAsync(
                rawToken, access.OrderId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            if (validatedOrderId == access.OrderId)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<SupportAttachmentActor>> ResolveSupportActorsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actors = new List<SupportAttachmentActor>(capacity: 2);

        // Member and admin are independent principals. Resource authorization tries each actor
        // separately and succeeds when either principal can read the attachment.
        var adminResult = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Admin);
        if (adminResult.Succeeded &&
            adminResult.Principal is { } adminPrincipal &&
            IsSupportHandler(adminPrincipal))
        {
            var adminUserId = adminPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(adminUserId))
            {
                actors.Add(new SupportAttachmentActor(
                    SupportAttachmentActorType.SupportHandler,
                    adminUserId,
                    adminPrincipal.IsInRole(DoSelectRoles.CustomerServiceSupervisor)));
            }
        }

        var memberResult = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (memberResult.Succeeded &&
            memberResult.Principal is { } memberPrincipal &&
            memberPrincipal.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member))
        {
            var memberUserId = memberPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(memberUserId))
            {
                actors.Add(new SupportAttachmentActor(SupportAttachmentActorType.Member, memberUserId));
            }
        }

        return actors;
    }

    private static bool IsSupportHandler(ClaimsPrincipal principal) =>
        principal.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin) &&
        principal.HasClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor) &&
        (principal.IsInRole(DoSelectRoles.CustomerService) ||
         principal.IsInRole(DoSelectRoles.CustomerServiceSupervisor));

    private static string SanitizeDownloadFileName(string originalFileName)
    {
        // Fail-closed: split only on delimiters that could carry path or HTTP-header-injection
        // material (path separators, colon, and all control characters including CR/LF).
        // Ordinary printable whitespace is preserved inside a segment — only the segment's own
        // leading/trailing whitespace is trimmed — so a legitimate name like "quarterly support
        // report.pdf" survives intact. Segments are scanned from the end so the most specific
        // (rightmost) safe token wins, and "." / ".." are never accepted as a candidate.
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in originalFileName)
        {
            if (c is '/' or '\\' or ':' || char.IsControl(c))
            {
                segments.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        segments.Add(current.ToString());

        for (var i = segments.Count - 1; i >= 0; i--)
        {
            var candidate = segments[i].Trim();
            if (candidate.Length > 0 && candidate != "." && candidate != "..")
            {
                return candidate;
            }
        }

        return FallbackDownloadFileName;
    }

    private static string SanitizeContentType(string contentType) =>
        string.IsNullOrWhiteSpace(contentType) || contentType.Any(char.IsControl)
            ? FallbackContentType
            : contentType;
}
