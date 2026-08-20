using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Controllers;

/// <summary>
/// Generic private-attachment content route. V1 resolves support-ticket attachments only;
/// every other case — including a PublicId that belongs to a domain this endpoint does not
/// yet know about — returns the same 404 as a missing attachment (Actor Scope + no existence
/// leakage).
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

    private readonly ISupportAttachmentReadService _service;

    public PrivateAttachmentsController(ISupportAttachmentReadService service)
    {
        _service = service;
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken)
    {
        var actor = await ResolveActorAsync(cancellationToken);
        if (actor is null)
        {
            // Covers anonymous callers, a member cookie alone, an admin without MFA, and an
            // admin whose role is not CustomerService/CustomerServiceSupervisor (SuperAdmin
            // included) — none of these ever reach the Application layer or storage.
            throw DomainProblemException.NotFound(NotFoundMessage);
        }

        var content = await _service.GetContentAsync(actor, id, cancellationToken);
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

    private async Task<SupportAttachmentActor?> ResolveActorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var memberResult = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (memberResult.Succeeded &&
            memberResult.Principal is { } memberPrincipal &&
            memberPrincipal.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member))
        {
            var memberUserId = memberPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(memberUserId))
            {
                return new SupportAttachmentActor(SupportAttachmentActorType.Member, memberUserId);
            }
        }

        var adminResult = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Admin);
        if (adminResult.Succeeded &&
            adminResult.Principal is { } adminPrincipal &&
            IsSupportHandler(adminPrincipal))
        {
            var adminUserId = adminPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(adminUserId))
            {
                return new SupportAttachmentActor(SupportAttachmentActorType.SupportHandler, adminUserId);
            }
        }

        return null;
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
