using System.Security.Claims;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.Common;

/// <summary>
/// Development-only convenience so a human can click through the customer-web UI without
/// haru's real login flow existing yet. Only reachable when IsDevelopment(); only ever signs
/// in/creates accounts whose email ends with @doselect.local, so it can never be pointed at a
/// real account even if a build somehow left it wired up. Not part of any M/S feature — throw
/// this away once real sign-in ships.
/// </summary>
public static class DevSignInEndpoints
{
    private const string TestEmailSuffix = "@doselect.local";

    public static IEndpointRouteBuilder MapDevSignInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/dev/test-sign-in", async (
            TestSignInRequest request,
            DoSelectDbContext dbContext,
            HttpContext httpContext,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !email.EndsWith(TestEmailSuffix, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = $"Email must end with {TestEmailSuffix}." });
            }

            var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (user is null)
            {
                user = ApplicationUser.CreateMember(Guid.CreateVersion7(), email, timeProvider.GetUtcNow().UtcDateTime);
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Email, email)],
                IdentityConstants.ApplicationScheme);
            await httpContext.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(new { memberUserId = user.Id, email });
        });

        endpoints.MapPost("/api/v1/dev/test-sign-out", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.Ok();
        });

        return endpoints;
    }
}

public sealed record TestSignInRequest(string Email);
