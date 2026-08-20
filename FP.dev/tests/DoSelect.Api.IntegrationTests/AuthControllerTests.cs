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

public sealed class AuthControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WhenSubmissionIsValid_ReturnsAcceptedWithMemberSummary()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = UniqueEmail(),
            password = "correct-horse-battery-staple",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(body.GetProperty("publicId").GetGuid() != Guid.Empty);
        Assert.Equal("pendingEmailVerification", body.GetProperty("accountStatus").GetString());
        Assert.Contains("*", body.GetProperty("emailMasked").GetString());
    }

    [Fact]
    public async Task Register_WhenEmailIsAlreadyRegistered_ReturnsConflictWithAccountEmailInUseCode()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);
        var email = UniqueEmail();
        var payload = new
        {
            email,
            password = "correct-horse-battery-staple",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        };

        using var firstResponse = await client.PostAsJsonAsync("/api/v1/auth/register", payload);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/v1/auth/register", payload);
        using var document = await ReadProblemDetailsAsync(secondResponse);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(
            AuthErrorCodes.AccountEmailInUse,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_WhenPasswordIsTooShort_ReturnsValidationProblemForPasswordField()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = UniqueEmail(),
            password = "short",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task ConfirmEmailVerification_WhenTokenIsInvalid_ReturnsEmailTokenInvalidCode()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/auth/email-verifications/confirm", new
        {
            userPublicId = Guid.NewGuid(),
            token = "not-a-real-token",
        });
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            AuthErrorCodes.EmailTokenInvalid,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RegisterThenConfirm_WhenTokenFromVerificationEmailIsUsed_ActivatesTheAccount()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = UniqueEmail(),
            password = "correct-horse-battery-staple",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = registerBody.GetProperty("publicId").GetGuid();
        var (linkPublicId, token) = ExtractVerificationLink(Assert.Single(capturingEmailSender.SentMessages).TextBody);
        Assert.Equal(publicId, linkPublicId);

        using var confirmResponse = await client.PostAsJsonAsync("/api/v1/auth/email-verifications/confirm", new
        {
            userPublicId = publicId,
            token,
        });
        var confirmBody = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Equal("active", confirmBody.GetProperty("accountStatus").GetString());
    }

    // Each test gets its own host with an in-memory DataProtection key ring. Without this, hosts
    // spun up by different test classes race over the real on-disk key ring (from
    // PersistenceServiceCollectionExtensions.AddDataProtection), which silently corrupts
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

    private static string UniqueEmail() => $"auth-controller-test-{Guid.NewGuid():N}@example.com";

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
            SentMessages.Add(message);
            return Task.FromResult(new EmailDeliveryResult(EmailDeliveryStatus.Sent));
        }
    }
}
