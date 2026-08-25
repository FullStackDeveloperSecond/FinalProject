using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoSelect.Api.Common;
using DoSelect.Application.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

public sealed class LoginControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Password = "correct-horse-battery-staple";

    private readonly WebApplicationFactory<Program> _factory;

    public LoginControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WhenCredentialsAreValid_ReturnsAuthenticatedSession()
    {
        var (client, email) = await CreateClientWithActivatedMemberAsync();

        using var response = await LoginAsync(client, email, Password, rememberMe: true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("isAuthenticated").GetBoolean());
        Assert.Contains("*", body.GetProperty("user").GetProperty("emailMasked").GetString());
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task Login_WhenPasswordIsWrong_ReturnsInvalidCredentials()
    {
        var (client, email) = await CreateClientWithActivatedMemberAsync();

        using var response = await LoginAsync(client, email, "totally-wrong-password", rememberMe: false);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            AuthErrorCodes.InvalidCredentials,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_WhenEmailIsUnverified_ReturnsAccountEmailUnverified()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });

        using var response = await LoginAsync(client, email, Password, rememberMe: false);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            AuthErrorCodes.AccountEmailUnverified,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_AfterFiveWrongPasswords_StillReturnsGenericInvalidCredentialsButBlocksEvenTheCorrectPassword()
    {
        // Locked-out and merely-wrong-password must be indistinguishable externally, otherwise
        // the distinct response itself becomes an oracle an attacker can use both to enumerate
        // accounts and to confirm a lockout attack landed (Alex review, 2026-08-21). Identity's
        // Lockout must still actually block the account internally, though — this proves both
        // halves: the external response never leaks 423/account_locked, and a subsequent attempt
        // with the *correct* password is still rejected while the lockout window is active.
        var (client, email) = await CreateClientWithActivatedMemberAsync();

        HttpResponseMessage? lastResponse = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = await LoginAsync(client, email, "totally-wrong-password", rememberMe: false);
        }

        using (var response = lastResponse!)
        {
            using var document = await ReadProblemDetailsAsync(response);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(
                AuthErrorCodes.InvalidCredentials,
                document.RootElement.GetProperty("code").GetString());
        }

        using var correctPasswordResponse = await LoginAsync(client, email, Password, rememberMe: false);
        using var correctPasswordDocument = await ReadProblemDetailsAsync(correctPasswordResponse);

        Assert.Equal(HttpStatusCode.Unauthorized, correctPasswordResponse.StatusCode);
        Assert.Equal(
            AuthErrorCodes.InvalidCredentials,
            correctPasswordDocument.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_LockedAccountAndUnknownAccount_ReturnIndistinguishableResponses()
    {
        var (client, email) = await CreateClientWithActivatedMemberAsync();

        HttpResponseMessage? lastResponse = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = await LoginAsync(client, email, "totally-wrong-password", rememberMe: false);
        }

        using var lockedResponse = lastResponse!;
        using var lockedDocument = await ReadProblemDetailsAsync(lockedResponse);

        using var unknownAccountResponse =
            await LoginAsync(client, UniqueEmail(), "totally-wrong-password", rememberMe: false);
        using var unknownAccountDocument = await ReadProblemDetailsAsync(unknownAccountResponse);

        Assert.Equal(lockedResponse.StatusCode, unknownAccountResponse.StatusCode);
        Assert.Equal(
            lockedDocument.RootElement.GetProperty("code").GetString(),
            unknownAccountDocument.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_WhenTheSameIpExceedsThePerIpBudgetWithinTheHour_ReturnsRateLimitExceededCode()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        // The per-IP login policy (SecurityServiceCollectionExtensions.AuthLogin) currently
        // permits 20 requests per hour; each call targets a distinct, nonexistent account to
        // model a password-spray sweep across many accounts from a single source.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response =
                await LoginAsync(client, UniqueEmail(), "totally-wrong-password", rememberMe: false);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var rateLimitedResponse =
            await LoginAsync(client, UniqueEmail(), "totally-wrong-password", rememberMe: false);
        using var document = await ReadProblemDetailsAsync(rateLimitedResponse);

        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);
        Assert.Equal(
            ApiErrorCodes.RateLimitExceeded,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Session_WhenNotLoggedIn_ReturnsIsAuthenticatedFalse()
    {
        using var client = CreateIsolatedFactory().CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/session");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Session_AfterLogin_ReturnsCurrentMember()
    {
        var (client, email) = await CreateClientWithActivatedMemberAsync();
        using var loginResponse = await LoginAsync(client, email, Password, rememberMe: false);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var response = await client.GetAsync("/api/v1/auth/session");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("整合測試會員", body.GetProperty("user").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Logout_AfterLogin_EndsTheSession()
    {
        var (client, email) = await CreateClientWithActivatedMemberAsync();
        using var loginResponse = await LoginAsync(client, email, Password, rememberMe: false);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Logout requires the Member policy, so its antiforgery validation now runs against the
        // authenticated principal; the anonymous token from before login no longer matches
        // (API共通規範.md: 登入、登出或切換會員／管理員 Scheme 後重新取得 Token).
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        await PrimeAntiforgeryAsync(client);

        using var logoutResponse = await client.PostAsync("/api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var sessionResponse = await client.GetAsync("/api/v1/auth/session");
        var body = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isAuthenticated").GetBoolean());
    }

    private async Task<(HttpClient Client, string Email)> CreateClientWithActivatedMemberAsync()
    {
        var capturingEmailSender = new CapturingEmailSender();
        var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var email = UniqueEmail();
        using var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = registerBody.GetProperty("publicId").GetGuid();
        var (_, token) = ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        using var confirmResponse = await client.PostAsJsonAsync("/api/v1/auth/email-verifications/confirm", new
        {
            userPublicId = publicId,
            token,
        });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        return (client, email);
    }

    // Each test that mutates state gets its own host with an in-memory DataProtection key ring.
    // Without this, hosts spun up by different test classes race over the real on-disk key ring
    // (from PersistenceServiceCollectionExtensions.AddDataProtection), which silently corrupts
    // antiforgery token validation across concurrently-running test classes.
    private WebApplicationFactory<Program> CreateIsolatedFactory(
        Action<IServiceCollection>? configureServices = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                configureServices?.Invoke(services);
            });
        });

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password,
        bool rememberMe) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, rememberMe });

    private static (Guid PublicId, string Token) ExtractVerificationLink(string emailTextBody)
    {
        var match = Regex.Match(
            emailTextBody,
            @"publicId=(?<publicId>[0-9a-fA-F-]{36})&token=(?<token>\S+)");
        Assert.True(match.Success, $"No verification link found in email body: {emailTextBody}");

        return (
            Guid.Parse(match.Groups["publicId"].Value),
            Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    private static async Task PrimeAntiforgeryAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "member");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", body.GetProperty("requestToken").GetString());
    }

    private static string UniqueEmail() => $"login-controller-test-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonDocument> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public Task<EmailDeliveryResult> SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
        {
            lock (SentMessages)
            {
                SentMessages.Add(message);
            }

            return Task.FromResult(new EmailDeliveryResult(EmailDeliveryStatus.Sent));
        }

        // Email is now dispatched via EmailDispatchBackgroundService (an in-memory Channel
        // consumer running outside the HTTP request), so it can arrive a beat after the request
        // that triggered it completes. Poll instead of asserting immediately to avoid flaking.
        public async Task<EmailMessage> WaitForSingleMessageAsync(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                lock (SentMessages)
                {
                    if (SentMessages.Count > 0)
                    {
                        return Assert.Single(SentMessages);
                    }
                }

                await Task.Delay(20);
            }

            return Assert.Single(SentMessages);
        }
    }
}
