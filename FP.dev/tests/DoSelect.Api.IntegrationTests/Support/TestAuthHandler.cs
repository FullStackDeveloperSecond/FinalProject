using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// Test-only authentication scheme registered exclusively via WebApplicationFactory's
/// ConfigureTestServices — Program.cs is never touched. Authenticates as whatever member id
/// is supplied in the X-Test-Member-Id header, or produces AuthenticateResult.NoResult() so
/// [Authorize] issues a genuine 401 when the header is absent (anonymous-request tests).
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string MemberHeaderName = "X-Test-Member-Id";
    public const string RolesHeaderName = "X-Test-Roles";
    public const string WithoutMfaHeaderName = "X-Test-Without-Mfa";

    public static void Configure(IServiceCollection services)
    {
        services
            .AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(SchemeName, _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(DoSelectPolicies.Member, new AuthorizationPolicyBuilder(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member)
                .Build());
            // DES-23 change-priority: bare Admin policy (no role requirement) — the entry gate
            // for the endpoint that itself performs an imperative "Handle OR Supervise" role
            // check, since ASP.NET Core cannot compose two named policies with OR declaratively.
            options.AddPolicy(DoSelectPolicies.Admin, new AuthorizationPolicyBuilder(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin)
                .RequireClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor)
                .Build());
            options.AddPolicy(DoSelectPolicies.SupportTicketHandle, new AuthorizationPolicyBuilder(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin)
                .RequireClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor)
                .RequireRole(DoSelectRoles.CustomerService, DoSelectRoles.CustomerServiceSupervisor)
                .Build());
            options.AddPolicy(DoSelectPolicies.SupportTicketSupervise, new AuthorizationPolicyBuilder(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin)
                .RequireClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor)
                .RequireRole(DoSelectRoles.CustomerServiceSupervisor, DoSelectRoles.SuperAdmin)
                .Build());
            options.AddPolicy(DoSelectPolicies.InvoiceManage, new AuthorizationPolicyBuilder(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin)
                .RequireClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor)
                .RequireRole(DoSelectRoles.FinanceManager, DoSelectRoles.SuperAdmin)
                .Build());
            // Coupon.Manage 與 Invoice.Manage 只差在多允許 MarketingAnalyst（DEC-P284）。
            options.AddPolicy(DoSelectPolicies.CouponManage, new AuthorizationPolicyBuilder(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin)
                .RequireClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor)
                .RequireRole(
                    DoSelectRoles.FinanceManager,
                    DoSelectRoles.MarketingAnalyst,
                    DoSelectRoles.SuperAdmin)
                .Build());
        });
    }

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(MemberHeaderName, out var memberId) ||
            string.IsNullOrWhiteSpace(memberId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var hasRoles = Request.Headers.TryGetValue(RolesHeaderName, out var roles)
            && !string.IsNullOrWhiteSpace(roles);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, memberId.ToString()),
            new(DoSelectClaimTypes.AccountType,
                hasRoles ? DoSelectClaimValues.Admin : DoSelectClaimValues.Member),
        };
        if (hasRoles)
        {
            if (!Request.Headers.ContainsKey(WithoutMfaHeaderName))
            {
                claims.Add(new Claim(
                    DoSelectClaimTypes.AuthenticationMethod,
                    DoSelectClaimValues.MultiFactor));
            }
            claims.AddRange(roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ClaimTypes.Role, role)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class AntiforgeryTestClient
{
    public static async Task<HttpResponseMessage> PostAsJsonWithAntiforgeryAsync<T>(
        this HttpClient client,
        string requestUri,
        T value,
        string clientType)
    {
        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, clientType);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var token = tokenDocument.RootElement.GetProperty("requestToken").GetString()!;
        var antiforgeryCookie = tokenResponse.Headers.TryGetValues("Set-Cookie", out var setCookieValues)
            ? setCookieValues
                .Select(value => value.Split(';', 2)[0])
                .SingleOrDefault(value => value.StartsWith(".DoSelect.Antiforgery=", StringComparison.Ordinal))
            : null;

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(value),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        if (antiforgeryCookie is not null)
        {
            request.Headers.Add("Cookie", antiforgeryCookie);
        }

        return await client.SendAsync(request);
    }
}
