using DoSelect.Api.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Security;

[ApiController]
[AllowAnonymous]
[Route("api/v1/security")]
public sealed class SecurityController(IAntiforgery antiforgery) : ControllerBase
{
    public const string ClientHeaderName = "X-DoSelect-Client";

    [HttpGet("antiforgery-token")]
    [ProducesResponseType(typeof(AntiforgeryTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AntiforgeryTokenResponse>> GetAntiforgeryToken(
        [FromHeader(Name = ClientHeaderName)] string client)
    {
        var authenticationScheme = client switch
        {
            DoSelectClaimValues.Member => DoSelectAuthenticationSchemes.Member,
            DoSelectClaimValues.Admin => DoSelectAuthenticationSchemes.Admin,
            _ => null,
        };
        if (authenticationScheme is null)
        {
            ModelState.AddModelError(
                "client",
                $"{ClientHeaderName} must be either 'member' or 'admin'.");
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState));
        }

        var authenticationResult = await HttpContext.AuthenticateAsync(authenticationScheme);
        if (authenticationResult.Succeeded && authenticationResult.Principal is not null)
        {
            HttpContext.User = authenticationResult.Principal;
        }

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException("The antiforgery request token could not be generated.");
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken));
    }
}

public sealed record AntiforgeryTokenResponse(string RequestToken);
