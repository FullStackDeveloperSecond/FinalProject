using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Contracts.Orders;
using DoSelect.Api.Security;
using DoSelect.Application.Orders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Orders;

[ApiController]
[Route("api/v1/guest-orders")]
public sealed class GuestOrderAccessController(
    GuestOrderAccessUseCase useCase) : ControllerBase
{
    [HttpPost("access-requests")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GuestOrderAccessRequestAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestAccess(
        [FromBody] GuestOrderAccessRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await useCase.RequestAccessAsync(
            request.OrderNumber.Trim(),
            request.Email.Trim(),
            GetClientIpAddress(),
            cancellationToken);

        return ToAcceptedResult(result);
    }

    [HttpPost("access-requests/{requestPublicId:guid}/actions/resend")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GuestOrderAccessRequestAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Resend(
        Guid requestPublicId,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ResendAsync(
            requestPublicId, GetClientIpAddress(), cancellationToken);

        return ToAcceptedResult(result);
    }

    [HttpPost("access-verifications")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GuestOrderAccessVerifiedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Verify(
        [FromBody] GuestOrderAccessVerificationDto request,
        CancellationToken cancellationToken)
    {
        var result = await useCase.VerifyAsync(
            request.RequestPublicId, request.Code.Trim(), cancellationToken);

        if (result is not GuestOrderAccessVerifyResult.Success success)
        {
            var errorCode = result is GuestOrderAccessVerifyResult.Failure failure
                ? failure.ErrorCode
                : GuestOrderErrorCodes.VerificationInvalid;
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, errorCode));
        }

        var identity = new ClaimsIdentity(DoSelectAuthenticationSchemes.GuestOrderAccess);
        identity.AddClaim(new Claim(GuestOrderAccessClaimTypes.TokenValue, success.RawToken));
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            DoSelectAuthenticationSchemes.GuestOrderAccess,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = success.ExpiresAtUtc,
            });

        return Ok(new GuestOrderAccessVerifiedDto(success.OrderPublicId, success.ExpiresAtUtc));
    }

    private IActionResult ToAcceptedResult(GuestOrderAccessAcceptedResult result) => result switch
    {
        GuestOrderAccessAcceptedResult.Accepted accepted => Accepted(new GuestOrderAccessRequestAcceptedDto(
            accepted.RequestPublicId,
            accepted.ExpiresAtUtc,
            accepted.ResendAvailableAtUtc ?? accepted.ExpiresAtUtc)),

        GuestOrderAccessAcceptedResult.RateLimited => ProblemResult(
            StatusCodes.Status429TooManyRequests,
            ApiErrorCodes.RateLimitExceeded,
            "Too many guest order access requests. Try again later."),

        _ => Problem(),
    };

    private ObjectResult ProblemResult(int statusCode, string code, string detail)
    {
        var problemDetails = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code, detail: detail);
        var result = new ObjectResult(problemDetails) { StatusCode = statusCode };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private string GetClientIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
