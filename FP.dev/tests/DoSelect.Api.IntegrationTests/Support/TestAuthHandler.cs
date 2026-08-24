using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
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

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, memberId.ToString())],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
