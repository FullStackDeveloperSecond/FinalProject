using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Api.Shopping;
using DoSelect.Application.Checkout;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Checkout;

[ApiController]
[Route("api/v1/checkout/guest-email")]
public sealed class GuestCheckoutEmailVerificationController(
    GuestCheckoutEmailVerificationService service) : ControllerBase
{
    [HttpPost("verification-requests")]
    [AllowAnonymous]
    [ProducesResponseType<GuestCheckoutEmailVerificationAcceptedDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestVerification(
        [FromBody] GuestCheckoutEmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var guestCartKey = await ResolveGuestCartKeyAsync();
        if (guestCartKey is null)
        {
            return GuestCartRequired();
        }

        var result = await service.RequestAsync(
            request.Email,
            guestCartKey,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            cancellationToken);
        return result switch
        {
            GuestCheckoutEmailRequestResult.Accepted accepted => Accepted(
                new GuestCheckoutEmailVerificationAcceptedDto(
                    accepted.RequestPublicId, accepted.ExpiresAtUtc)),
            GuestCheckoutEmailRequestResult.RateLimited => StatusCode(
                StatusCodes.Status429TooManyRequests,
                ApiProblemDetailsFactory.Create(
                    HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    ApiErrorCodes.RateLimitExceeded,
                    detail: "驗證信寄送次數過多，請稍後再試。")),
            _ => Problem(),
        };
    }

    [HttpPost("verifications")]
    [AllowAnonymous]
    [ProducesResponseType<GuestCheckoutEmailVerifiedDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(
        [FromBody] GuestCheckoutEmailVerificationCodeRequest request,
        CancellationToken cancellationToken)
    {
        var guestCartKey = await ResolveGuestCartKeyAsync();
        if (guestCartKey is null)
        {
            return GuestCartRequired();
        }

        var result = await service.VerifyAsync(
            request.RequestPublicId, request.Code, guestCartKey, cancellationToken);
        if (result is not GuestCheckoutEmailVerifyResult.Success success)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "guest_checkout_email_verification_invalid",
                detail: "驗證碼無效或已過期。"));
        }

        var identity = new ClaimsIdentity(DoSelectAuthenticationSchemes.GuestCheckoutEmail);
        identity.AddClaim(new Claim(GuestCheckoutEmailClaimTypes.ProofToken, success.RawProofToken));
        await HttpContext.SignInAsync(
            DoSelectAuthenticationSchemes.GuestCheckoutEmail,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = success.ExpiresAtUtc,
            });
        return Ok(new GuestCheckoutEmailVerifiedDto(true, success.ExpiresAtUtc));
    }

    [HttpGet("verification-status")]
    [AllowAnonymous]
    [ProducesResponseType<GuestCheckoutEmailVerificationStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var guestCartKey = await ResolveGuestCartKeyAsync();
        if (guestCartKey is null)
        {
            return Ok(new GuestCheckoutEmailVerificationStatusDto(false, null, null));
        }

        var authentication = await HttpContext.AuthenticateAsync(
            DoSelectAuthenticationSchemes.GuestCheckoutEmail);
        var rawProof = authentication.Succeeded
            ? authentication.Principal?.FindFirstValue(GuestCheckoutEmailClaimTypes.ProofToken)
            : null;
        return Ok(await service.GetStatusAsync(rawProof, guestCartKey, cancellationToken));
    }

    private async Task<string?> ResolveGuestCartKeyAsync()
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        return identity is { MemberUserId: null, GuestCartKey: not null }
            ? identity.GuestCartKey
            : null;
    }

    private BadRequestObjectResult GuestCartRequired() => BadRequest(
        ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            detail: "需要有效的訪客購物車。"));
}
