using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Application.Shopping;
using Microsoft.AspNetCore.Authentication;

namespace DoSelect.Api.Shopping;

/// <summary>
/// Resolves the caller of a Cart request that must accept both anonymous guests and
/// authenticated members on the same route (no [Authorize] attribute can express that).
/// Mirrors <see cref="SecurityController.GetAntiforgeryToken"/>'s manual
/// <c>HttpContext.AuthenticateAsync</c> call — a member session always wins over a guest
/// header when both happen to be present.
/// </summary>
public static class CartIdentityResolver
{
    public const string GuestCartKeyHeaderName = "X-DoSelect-Guest-Cart-Key";

    public static async Task<CartIdentity?> ResolveAsync(HttpContext httpContext)
    {
        var authenticationResult = await httpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (authenticationResult.Succeeded &&
            authenticationResult.Principal?.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member) == true)
        {
            var memberUserId = authenticationResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(memberUserId))
            {
                return new CartIdentity(memberUserId, null);
            }
        }

        if (httpContext.Request.Headers.TryGetValue(GuestCartKeyHeaderName, out var headerValue) &&
            headerValue.ToString() is { Length: >= 32 and <= 256 } guestCartKey)
        {
            return new CartIdentity(null, guestCartKey);
        }

        return null;
    }
}
