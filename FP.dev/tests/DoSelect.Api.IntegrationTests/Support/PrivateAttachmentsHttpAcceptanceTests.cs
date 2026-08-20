using System.Net;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Support;

public sealed class PrivateAttachmentsHttpAcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string MemberHeader = "X-Attachment-Member";
    private const string AdminHeader = "X-Attachment-Admin";
    private const string AdminRolesHeader = "X-Attachment-Admin-Roles";
    private const string AdminMfaHeader = "X-Attachment-Admin-Mfa";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public PrivateAttachmentsHttpAcceptanceTests(WebApplicationFactory<Program> baseFactory) => _baseFactory = baseFactory;

    [Fact]
    public async Task AnonymousAndMissing_AreEquivalent404WithoutRedirectOrServiceCall()
    {
        var fake = new AttachmentServiceFake();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var id = Guid.NewGuid();

        using var anonymous = await client.GetAsync($"/api/v1/private-attachments/{id}/content");
        var anonymousBody = await anonymous.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
        Assert.Null(anonymous.Headers.Location);
        Assert.Equal(0, fake.Calls);

        client.DefaultRequestHeaders.Add(MemberHeader, $"member-{Guid.NewGuid():N}");
        fake.ThrowNotFound = true;
        using var missing = await client.GetAsync($"/api/v1/private-attachments/{Guid.NewGuid()}/content");
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        using var anonymousJson = JsonDocument.Parse(anonymousBody);
        using var missingJson = JsonDocument.Parse(missingBody);
        Assert.Equal(DomainErrorCodes.ResourceNotFound, anonymousJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ResourceNotFound, missingJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(anonymousJson.RootElement.GetProperty("detail").GetString(), missingJson.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task MemberCookieWinsWhenBothSchemesAuthenticate()
    {
        var fake = new AttachmentServiceFake();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        var member = $"member-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add(MemberHeader, member);
        client.DefaultRequestHeaders.Add(AdminHeader, $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(AdminRolesHeader, DoSelectRoles.CustomerService);
        client.DefaultRequestHeaders.Add(AdminMfaHeader, "true");

        using var response = await client.GetAsync($"/api/v1/private-attachments/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SupportAttachmentActorType.Member, fake.Actor?.Type);
        Assert.Equal(member, fake.Actor?.UserId);
    }

    [Theory]
    [InlineData(DoSelectRoles.CustomerService, true, HttpStatusCode.OK)]
    [InlineData(DoSelectRoles.CustomerServiceSupervisor, true, HttpStatusCode.OK)]
    [InlineData(DoSelectRoles.SuperAdmin, true, HttpStatusCode.NotFound)]
    [InlineData(DoSelectRoles.CustomerService, false, HttpStatusCode.NotFound)]
    [InlineData("Member", true, HttpStatusCode.NotFound)]
    public async Task AdminLiteralScheme_EnforcesHandleMfaAndRoles(string role, bool mfa, HttpStatusCode expected)
    {
        var fake = new AttachmentServiceFake();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminHeader, $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(AdminRolesHeader, role);
        client.DefaultRequestHeaders.Add(AdminMfaHeader, mfa.ToString());

        using var response = await client.GetAsync($"/api/v1/private-attachments/{Guid.NewGuid()}/content");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fake.Calls);
    }

    [Fact]
    public async Task Success_StreamsBytesSafeHeadersAndNoSensitiveLeakage()
    {
        var fake = new AttachmentServiceFake
        {
            Bytes = [0, 1, 254, 255],
            ContentType = "text/plain",
            FileName = "..\\private/path\r\nX-Leak: storage-key.txt",
        };
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MemberHeader, $"member-{Guid.NewGuid():N}");

        using var response = await client.GetAsync($"/api/v1/private-attachments/{Guid.NewGuid()}/content");
        var body = await response.Content.ReadAsByteArrayAsync();
        var headers = response.ToString();

        Assert.Equal(fake.Bytes, body);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("storage-key.txt", response.Content.Headers.ContentDisposition?.FileNameStar);
        foreach (var forbidden in new[] { "private/path", "X-Leak", "StorageKey", "Sha256", "UploadedByUserId", "PhysicalPath", "9223372036854775807" })
            Assert.DoesNotContain(forbidden, headers, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SafeFileName_WithOrdinarySpaces_IsPreserved()
    {
        var fake = new AttachmentServiceFake { FileName = "quarterly support report.pdf" };
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MemberHeader, $"member-{Guid.NewGuid():N}");

        using var response = await client.GetAsync($"/api/v1/private-attachments/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("quarterly support report.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task EntirelyUnsafeFileName_UsesNeutralFallback()
    {
        var fake = new AttachmentServiceFake { FileName = "../\\:\r\n\t" };
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MemberHeader, $"member-{Guid.NewGuid():N}");

        using var response = await client.GetAsync($"/api/v1/private-attachments/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task NonCrLfControlCharacter_DelimitsUnsafePrefix()
    {
        var fake = new AttachmentServiceFake { FileName = "unsafe-prefix\u0001safe-report.pdf" };
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MemberHeader, $"member-{Guid.NewGuid():N}");

        using var response = await client.GetAsync($"/api/v1/private-attachments/{Guid.NewGuid()}/content");
        var headers = response.ToString();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("safe-report.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.DoesNotContain("unsafe-prefix", headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\u0001", headers, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateFactory(AttachmentServiceFake fake) =>
        _baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthenticationHandlerProvider>();
            services.AddSingleton<IAuthenticationHandlerProvider, HeaderAuthenticationHandlerProvider>();
            services.RemoveAll<ISupportAttachmentReadService>();
            services.AddSingleton<ISupportAttachmentReadService>(fake);
        }));

    private sealed class AttachmentServiceFake : ISupportAttachmentReadService
    {
        public int Calls { get; private set; }
        public bool ThrowNotFound { get; set; }
        public byte[] Bytes { get; init; } = [7, 8, 9];
        public string ContentType { get; init; } = "application/octet-stream";
        public string FileName { get; init; } = "attachment.bin";
        public SupportAttachmentActor? Actor { get; private set; }
        public Task<PrivateAttachmentContent> GetContentAsync(SupportAttachmentActor actor, Guid attachmentPublicId, CancellationToken cancellationToken)
        {
            Calls++;
            Actor = actor;
            if (ThrowNotFound) throw DomainProblemException.NotFound("The attachment was not found.");
            return Task.FromResult(new PrivateAttachmentContent(new MemoryStream(Bytes), ContentType, FileName));
        }
    }

    private sealed class HeaderAuthenticationHandlerProvider : IAuthenticationHandlerProvider
    {
        public async Task<IAuthenticationHandler?> GetHandlerAsync(HttpContext context, string authenticationScheme)
        {
            var handler = new HeaderAuthenticationHandler(authenticationScheme);
            var scheme = new AuthenticationScheme(authenticationScheme, displayName: null, typeof(HeaderAuthenticationHandler));
            await handler.InitializeAsync(scheme, context);
            return handler;
        }
    }

    private sealed class HeaderAuthenticationHandler(string scheme) : IAuthenticationHandler
    {
        private HttpContext _context = null!;
        public Task InitializeAsync(AuthenticationScheme authenticationScheme, HttpContext context) { _context = context; return Task.CompletedTask; }
        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
        public Task<AuthenticateResult> AuthenticateAsync()
        {
            var isMember = scheme == DoSelectAuthenticationSchemes.Member;
            var identityHeader = isMember ? MemberHeader : AdminHeader;
            if (!_context.Request.Headers.TryGetValue(identityHeader, out var userId) || string.IsNullOrWhiteSpace(userId))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(DoSelectClaimTypes.AccountType, isMember ? DoSelectClaimValues.Member : DoSelectClaimValues.Admin),
            };
            if (!isMember)
            {
                if (_context.Request.Headers[AdminMfaHeader].ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
                    claims.Add(new(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor));
                claims.AddRange(_context.Request.Headers[AdminRolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new Claim(ClaimTypes.Role, x)));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        }
    }
}
