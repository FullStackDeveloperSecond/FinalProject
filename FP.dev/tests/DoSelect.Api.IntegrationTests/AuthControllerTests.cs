using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoSelect.Api.Common;
using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Persistence.Identity;
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
        var (linkPublicId, token) = ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);
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

    [Fact]
    public async Task RegisterThenConfirm_WhenSameTokenIsSubmittedAgain_ReturnsEmailTokenInvalidCode()
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
        var (linkPublicId, token) = ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);
        Assert.Equal(publicId, linkPublicId);

        using var firstConfirmResponse = await client.PostAsJsonAsync("/api/v1/auth/email-verifications/confirm", new
        {
            userPublicId = publicId,
            token,
        });
        Assert.Equal(HttpStatusCode.OK, firstConfirmResponse.StatusCode);

        // The same token replayed after a successful confirmation must be rejected: a token is a
        // single-use credential, not a standing key that keeps working until it expires.
        using var replayResponse = await client.PostAsJsonAsync("/api/v1/auth/email-verifications/confirm", new
        {
            userPublicId = publicId,
            token,
        });
        using var document = await ReadProblemDetailsAsync(replayResponse);

        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
        Assert.Equal(
            AuthErrorCodes.EmailTokenInvalid,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmPasswordReset_WhenTokenHasExceededItsConfiguredLifespan_ReturnsPasswordResetTokenInvalidCode()
    {
        var shortLifespan = TimeSpan.FromMilliseconds(200);
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));

            // Overrides the production 1-hour PasswordResetTokenProviderOptions.TokenLifespan
            // (PersistenceServiceCollectionExtensions) with a short-lived one so the boundary can
            // be exercised deterministically instead of waiting a real hour. This only proves the
            // regression fix if the configured lifespan is actually observed by the provider that
            // issues/validates the token — which was the bug: a named Configure<> call against the
            // shared DataProtectionTokenProviderOptions type was silently ignored.
            services.Configure<PasswordResetTokenProviderOptions>(options => options.TokenLifespan = shortLifespan);
        });
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var email = UniqueEmail();
        using var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct-horse-battery-staple",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);
        var (verifyPublicId, verifyToken) =
            ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);
        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/email-verifications/confirm",
            new { userPublicId = verifyPublicId, token = verifyToken });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        capturingEmailSender.SentMessages.Clear();

        using var requestResetResponse = await client.PostAsJsonAsync("/api/v1/auth/password-resets", new
        {
            email,
        });
        Assert.Equal(HttpStatusCode.Accepted, requestResetResponse.StatusCode);
        var (resetPublicId, resetToken) =
            ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        await Task.Delay(shortLifespan + TimeSpan.FromMilliseconds(300));

        using var confirmResetResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/password-resets/confirm",
            new { userPublicId = resetPublicId, token = resetToken, newPassword = "new-correct-horse-battery" });
        using var document = await ReadProblemDetailsAsync(confirmResetResponse);

        Assert.Equal(HttpStatusCode.BadRequest, confirmResetResponse.StatusCode);
        Assert.Equal(
            AuthErrorCodes.PasswordResetTokenInvalid,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_WhenSameEmailIsSubmittedTooManyTimesWithinTheHour_ReturnsRateLimitExceededCode()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        var email = UniqueEmail();
        object Payload() => new
        {
            email,
            password = "correct-horse-battery-staple",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        };

        // IEmailRequestThrottle currently permits 3 requests per email per hour for the
        // "register" purpose (EmailRequestThrottle). The first consumes the account; the next two
        // still consume budget even though the account already exists (EmailInUse), because the
        // throttle is checked before the gateway call and must not itself leak account existence.
        using var firstResponse = await client.PostAsJsonAsync("/api/v1/auth/register", Payload());
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/v1/auth/register", Payload());
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        using var thirdResponse = await client.PostAsJsonAsync("/api/v1/auth/register", Payload());
        Assert.Equal(HttpStatusCode.Conflict, thirdResponse.StatusCode);

        using var fourthResponse = await client.PostAsJsonAsync("/api/v1/auth/register", Payload());
        using var document = await ReadProblemDetailsAsync(fourthResponse);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourthResponse.StatusCode);
        Assert.Equal(
            ApiErrorCodes.RateLimitExceeded,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_WhenTheSameIpExceedsThePerIpBudgetWithinTheHour_ReturnsRateLimitExceededCode()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        // The per-IP policy (SecurityServiceCollectionExtensions.AuthRegister) currently permits 5
        // requests per hour; each call here uses a distinct email so the per-email throttle (limit
        // 3) never trips first — only the per-IP budget is being exercised.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                email = UniqueEmail(),
                password = "correct-horse-battery-staple",
                displayName = "整合測試會員",
                acceptTermsVersion = 1,
            });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        using var sixthResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = UniqueEmail(),
            password = "correct-horse-battery-staple",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        using var document = await ReadProblemDetailsAsync(sixthResponse);

        Assert.Equal(HttpStatusCode.TooManyRequests, sixthResponse.StatusCode);
        Assert.Equal(
            ApiErrorCodes.RateLimitExceeded,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RequestPasswordReset_WhenEmailIsUnknown_StillReturnsAccepted()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/auth/password-resets", new
        {
            email = UniqueEmail(),
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmPasswordReset_WhenTokenIsInvalid_ReturnsPasswordResetTokenInvalidCode()
    {
        using var client = CreateIsolatedFactory().CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/auth/password-resets/confirm", new
        {
            userPublicId = Guid.NewGuid(),
            token = "not-a-real-token",
            newPassword = "correct-horse-battery-staple",
        });
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            AuthErrorCodes.PasswordResetTokenInvalid,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ResetPassword_WhenTokenFromResetEmailIsUsed_AllowsLoginWithNewPasswordAndSignsOutOldSession()
    {
        const string originalPassword = "correct-horse-battery-staple";
        const string newPassword = "new-correct-horse-battery";

        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });

        using var registrationClient = factory.CreateClient();
        await PrimeAntiforgeryAsync(registrationClient);
        var email = UniqueEmail();
        using var registerResponse = await registrationClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = originalPassword,
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = registerBody.GetProperty("publicId").GetGuid();
        var (verifyPublicId, verifyToken) =
            ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);
        using var verifyResponse = await registrationClient.PostAsJsonAsync(
            "/api/v1/auth/email-verifications/confirm",
            new { userPublicId = verifyPublicId, token = verifyToken });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        capturingEmailSender.SentMessages.Clear();

        // A separate client models "another device" that is already logged in when the password
        // gets reset elsewhere; its session cookie must stop working afterwards.
        using var otherDeviceClient = factory.CreateClient();
        await PrimeAntiforgeryAsync(otherDeviceClient);
        using var otherDeviceLoginResponse = await otherDeviceClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = originalPassword,
            rememberMe = false,
        });
        Assert.Equal(HttpStatusCode.OK, otherDeviceLoginResponse.StatusCode);
        using var otherDeviceSessionBefore = await otherDeviceClient.GetAsync("/api/v1/auth/session");
        var otherDeviceSessionBeforeBody = await otherDeviceSessionBefore.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(otherDeviceSessionBeforeBody.GetProperty("isAuthenticated").GetBoolean());

        using var requestResetClient = factory.CreateClient();
        await PrimeAntiforgeryAsync(requestResetClient);
        using var requestResetResponse = await requestResetClient.PostAsJsonAsync("/api/v1/auth/password-resets", new
        {
            email,
        });
        Assert.Equal(HttpStatusCode.Accepted, requestResetResponse.StatusCode);
        var (resetPublicId, resetToken) =
            ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);
        Assert.Equal(publicId, resetPublicId);

        using var confirmResetResponse = await requestResetClient.PostAsJsonAsync(
            "/api/v1/auth/password-resets/confirm",
            new { userPublicId = resetPublicId, token = resetToken, newPassword });
        Assert.Equal(HttpStatusCode.OK, confirmResetResponse.StatusCode);

        using var otherDeviceSessionAfter = await otherDeviceClient.GetAsync("/api/v1/auth/session");
        var otherDeviceSessionAfterBody = await otherDeviceSessionAfter.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(otherDeviceSessionAfterBody.GetProperty("isAuthenticated").GetBoolean());

        using var oldPasswordLoginClient = factory.CreateClient();
        await PrimeAntiforgeryAsync(oldPasswordLoginClient);
        using var oldPasswordLoginResponse = await oldPasswordLoginClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = originalPassword,
            rememberMe = false,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLoginResponse.StatusCode);

        using var newPasswordLoginClient = factory.CreateClient();
        await PrimeAntiforgeryAsync(newPasswordLoginClient);
        using var newPasswordLoginResponse = await newPasswordLoginClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = newPassword,
            rememberMe = false,
        });
        Assert.Equal(HttpStatusCode.OK, newPasswordLoginResponse.StatusCode);
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
