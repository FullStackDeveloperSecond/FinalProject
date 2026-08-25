using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Files;
using DoSelect.Application.Returns;
using DoSelect.Infrastructure.Persistence.Returns;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DoSelect.Api.Returns;

/// <summary>
/// Serves <c>GET /api/v1/private-attachments/{id}/content</c> for Return attachments only — the
/// shared route from 檔案與圖片儲存設計.md is meant to resolve across every private-attachment
/// domain (客服/檢舉/退貨), but Support's controller for the same route lives on the still-
/// unmerged feature/support-tickets branch and this PR must not reference it. Only ids that
/// resolve against ReturnAttachments are served here; anything else 404s. See the
/// implementation report for the reconciliation this creates once the Support PR merges.
/// </summary>
[ApiController]
[Route("api/v1/private-attachments")]
public sealed class PrivateReturnAttachmentsController : ControllerBase
{
    private readonly IReturnStore _store;
    private readonly IPrivateFileStorage _fileStorage;

    public PrivateReturnAttachmentsController(IReturnStore store, IPrivateFileStorage fileStorage)
    {
        _store = store;
        _fileStorage = fileStorage;
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken)
    {
        var access = await _store.FindAttachmentAccessAsync(id, cancellationToken);
        if (access is null)
        {
            return NotFoundProblem();
        }

        if (!await IsAuthorizedAsync(access, cancellationToken))
        {
            // Same 404 as "does not exist" — never reveal that another owner's attachment exists.
            return NotFoundProblem();
        }

        var stream = await _fileStorage.OpenReadAsync(access.StorageKey, cancellationToken);
        if (stream is null)
        {
            return NotFoundProblem();
        }

        Response.Headers.Append(
            HeaderNames.ContentDisposition,
            new ContentDispositionHeaderValue("attachment") { FileNameStar = SafeFileName(access.OriginalFileName) }.ToString());
        return File(stream, string.IsNullOrWhiteSpace(access.ContentType) ? "application/octet-stream" : access.ContentType);
    }

    private async Task<bool> IsAuthorizedAsync(ReturnAttachmentAccess access, CancellationToken cancellationToken)
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

    private NotFoundObjectResult NotFoundProblem()
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext, StatusCodes.Status404NotFound, ReturnsWriteException.ErrorCodes.ResourceNotFound);
        return NotFound(problem);
    }

    /// <summary>Strips CR/LF/control characters and quotes so a crafted OriginalFileName can
    /// never inject extra headers or break out of the Content-Disposition value; falls back to
    /// a generic name if nothing safe remains.</summary>
    private static string SafeFileName(string originalFileName)
    {
        var cleaned = new string([.. originalFileName.Where(c => !char.IsControl(c) && c is not ('"' or '\\'))]).Trim();
        return string.IsNullOrEmpty(cleaned) ? "attachment" : cleaned;
    }
}
