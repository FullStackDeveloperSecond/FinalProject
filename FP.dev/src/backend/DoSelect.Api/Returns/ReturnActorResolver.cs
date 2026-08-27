using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Application.Returns;
using DoSelect.Infrastructure.Persistence.Returns;
using Microsoft.AspNetCore.Authentication;

namespace DoSelect.Api.Returns;

/// <summary>
/// Resolves the caller of a customer-facing Returns request — member session takes precedence,
/// mirroring CartIdentityResolver's shape (a member session always wins over a guest cookie
/// when both happen to be present). The guest branch requires the request's own orderPublicId
/// so the resolved actor is scoped to exactly the order the guest cookie was validated against
/// — never a bare "is this guest logged in" check.
/// </summary>
public sealed class ReturnActorResolver
{
    private readonly IReturnOrderEligibilityPort _orderPort;
    private readonly IGuestOrderAccessValidator _guestValidator;
    private readonly TimeProvider _timeProvider;

    public ReturnActorResolver(
        IReturnOrderEligibilityPort orderPort,
        IGuestOrderAccessValidator guestValidator,
        TimeProvider timeProvider)
    {
        _orderPort = orderPort;
        _guestValidator = guestValidator;
        _timeProvider = timeProvider;
    }

    public async Task<ReturnActor?> ResolveForOrderAsync(HttpContext httpContext, Guid orderPublicId, CancellationToken cancellationToken)
    {
        var authenticationResult = await httpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (authenticationResult.Succeeded &&
            authenticationResult.Principal?.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member) == true)
        {
            var memberUserId = authenticationResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(memberUserId))
            {
                return new ReturnActor(memberUserId, null);
            }
        }

        if (httpContext.Request.Cookies.TryGetValue(GuestOrderAccessValidator.GuestOrderAccessCookieName, out var rawToken) &&
            !string.IsNullOrWhiteSpace(rawToken))
        {
            var order = await _orderPort.FindByPublicIdAsync(orderPublicId, cancellationToken);
            if (order is null)
            {
                return null;
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var validatedOrderId = await _guestValidator.ValidateAsync(rawToken, order.OrderId, nowUtc, cancellationToken);
            if (validatedOrderId is { } guestOrderId)
            {
                return new ReturnActor(null, guestOrderId);
            }
        }

        return null;
    }

    /// <summary>Used by routes keyed on the return itself (GET/POST returns/{id}...) where the
    /// order is not yet known — the guest branch resolves the order from the cookie's own bound
    /// OrderId column instead of a caller-supplied one, then the Application layer's
    /// FindOwnedAsync still enforces that this exact order owns the requested return.</summary>
    public async Task<ReturnActor?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var authenticationResult = await httpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (authenticationResult.Succeeded &&
            authenticationResult.Principal?.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member) == true)
        {
            var memberUserId = authenticationResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(memberUserId))
            {
                return new ReturnActor(memberUserId, null);
            }
        }

        if (httpContext.Request.Cookies.TryGetValue(GuestOrderAccessValidator.GuestOrderAccessCookieName, out var rawToken) &&
            !string.IsNullOrWhiteSpace(rawToken))
        {
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var guestOrderId = await _guestValidator.ResolveOrderIdAsync(rawToken, nowUtc, cancellationToken);
            if (guestOrderId is { } resolvedOrderId)
            {
                return new ReturnActor(null, resolvedOrderId);
            }
        }

        return null;
    }
}
