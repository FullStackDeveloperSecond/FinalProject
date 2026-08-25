using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests;

public sealed class SecurityFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityFoundationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AntiforgeryToken_IsNoStoreAndAllowsUnsafeRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client, "member");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/write")
        {
            Content = JsonContent.Create(new { value = "accepted" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public async Task AntiforgeryToken_RequiresKnownClientScheme(string? clientType)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        if (clientType is not null)
        {
            request.Headers.Add(SecurityController.ClientHeaderName, clientType);
        }

        using var response = await client.SendAsync(request);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task UnsafeRequest_WithoutValidAntiforgeryToken_ReturnsCodedProblemDetails(
        string? token)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        if (token is not null)
        {
            _ = await GetAntiforgeryTokenAsync(client, "member");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/write")
        {
            Content = JsonContent.Create(new { value = "rejected" }),
        };
        if (token is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", token);
        }

        using var response = await client.SendAsync(request);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.AntiforgeryValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MemberCookie_CannotAuthenticateAdminPolicy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client, "member", includeMfa: false);

        using var memberResponse = await client.GetAsync("/__tests/security/member");
        using var adminResponse = await client.GetAsync("/__tests/security/admin");

        Assert.Equal(HttpStatusCode.OK, memberResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, adminResponse.StatusCode);
    }

    [Fact]
    public async Task AdminCookie_CannotAuthenticateMemberPolicy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client, "admin", includeMfa: true, DoSelectRoles.SuperAdmin);

        using var adminResponse = await client.GetAsync("/__tests/security/admin");
        using var memberResponse = await client.GetAsync("/__tests/security/member");

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, memberResponse.StatusCode);
    }

    [Fact]
    public async Task AdminCookie_WithoutMfaClaim_IsForbidden()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client, "admin", includeMfa: false, DoSelectRoles.SuperAdmin);

        using var response = await client.GetAsync("/__tests/security/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedMember_UsesTokenBoundToMemberSchemeForUnsafeRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client, "member", includeMfa: false);
        var token = await GetAntiforgeryTokenAsync(client, "member");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/member-write")
        {
            Content = JsonContent.Create(new { value = "member-write" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(DoSelectRoles.CustomerService, true, false)]
    [InlineData(DoSelectRoles.CustomerServiceSupervisor, true, true)]
    [InlineData(DoSelectRoles.SuperAdmin, false, true)]
    public async Task SupportPolicies_RespectDailyHandlingAndSupervisionBoundary(
        string role,
        bool canHandle,
        bool canSupervise)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client, "admin", includeMfa: true, role);

        using var handleResponse = await client.GetAsync("/__tests/security/support-handle");
        using var superviseResponse = await client.GetAsync("/__tests/security/support-supervise");

        Assert.Equal(canHandle ? HttpStatusCode.OK : HttpStatusCode.Forbidden, handleResponse.StatusCode);
        Assert.Equal(canSupervise ? HttpStatusCode.OK : HttpStatusCode.Forbidden, superviseResponse.StatusCode);
    }

    [Fact]
    public async Task Cors_AllowsConfiguredFrontendAndRejectsUnknownOrigin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var allowed = CreatePreflight("http://localhost:5173");
        using var allowedResponse = await client.SendAsync(allowed);
        using var rejected = CreatePreflight("https://malicious.example");
        using var rejectedResponse = await client.SendAsync(rejected);

        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal(
            "http://localhost:5173",
            allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("true", allowedResponse.Headers.GetValues("Access-Control-Allow-Credentials"));
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
            });
        });

    private static async Task SignInAsync(
        HttpClient client,
        string accountType,
        bool includeMfa,
        params string[] roles)
    {
        var token = await GetAntiforgeryTokenAsync(client, accountType);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/__tests/security/sign-in/{accountType}")
        {
            Content = JsonContent.Create(new { includeMfa, roles }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string clientType)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, clientType);
        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        return document.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static HttpRequestMessage CreatePreflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/__tests/security/write");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}

[ApiController]
[Route("__tests/security")]
public sealed class SecurityFoundationTestController : ControllerBase
{
    [HttpPost("write")]
    public IActionResult Write(SecurityFoundationWriteRequest request) => Ok(request);

    [HttpPost("sign-in/{accountType}")]
    public async Task<IActionResult> SignInForTest(
        string accountType,
        SecurityFoundationSignInRequest request)
    {
        var isMember = string.Equals(accountType, "member", StringComparison.Ordinal);
        var scheme = isMember
            ? DoSelectAuthenticationSchemes.Member
            : DoSelectAuthenticationSchemes.Admin;
        var accountTypeValue = isMember
            ? DoSelectClaimValues.Member
            : DoSelectClaimValues.Admin;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, request.UserId ?? Guid.NewGuid().ToString("D")),
            new(DoSelectClaimTypes.AccountType, accountTypeValue),
        };
        claims.AddRange(request.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        if (request.IncludeMfa)
        {
            claims.Add(new Claim(
                DoSelectClaimTypes.AuthenticationMethod,
                DoSelectClaimValues.MultiFactor));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
        await HttpContext.SignInAsync(scheme, principal);
        return NoContent();
    }

    [HttpGet("member")]
    [Authorize(Policy = DoSelectPolicies.Member)]
    public IActionResult MemberOnly() => Ok();

    [HttpPost("member-write")]
    [Authorize(Policy = DoSelectPolicies.Member)]
    public IActionResult MemberWrite(SecurityFoundationWriteRequest request) => Ok(request);

    [HttpGet("admin")]
    [Authorize(Policy = DoSelectPolicies.Admin)]
    public IActionResult AdminOnly() => Ok();

    [HttpGet("support-handle")]
    [Authorize(Policy = DoSelectPolicies.SupportTicketHandle)]
    public IActionResult SupportHandle() => Ok();

    [HttpGet("support-supervise")]
    [Authorize(Policy = DoSelectPolicies.SupportTicketSupervise)]
    public IActionResult SupportSupervise() => Ok();
}

public sealed record SecurityFoundationWriteRequest(string Value);

/// <summary>
/// UserId is optional and defaults to a random GUID (the original behavior). Pass a real
/// ApplicationUser.Id when the signed-in principal needs to satisfy a foreign key — e.g.
/// Carts.OwnerUserId references AspNetUsers, so member-owned-cart tests must sign in as
/// a real seeded user rather than an arbitrary fake identifier.
/// </summary>
public sealed record SecurityFoundationSignInRequest(bool IncludeMfa, string[] Roles, string? UserId = null);
