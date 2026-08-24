using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoSelect.Api.Common;
using DoSelect.Api.Contracts.Auth;
using DoSelect.Application.Members;
using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    public async Task Register_WhenEmailIsAlreadyRegistered_ReturnsTheSameAcceptedShapeAsAFreshRegistration()
    {
        // Non-enumerable by design (Alex review, 2026-08-21): the public response for a
        // duplicate registration must be indistinguishable from a fresh one — same status code,
        // same body shape, and no real PublicId of the existing account.
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        using var client = factory.CreateClient();
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
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var realPublicId = firstBody.GetProperty("publicId").GetGuid();

        using var secondResponse = await client.PostAsJsonAsync("/api/v1/auth/register", payload);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.Equal(
            firstBody.GetProperty("emailMasked").GetString(),
            secondBody.GetProperty("emailMasked").GetString());
        Assert.Equal(
            firstBody.GetProperty("accountStatus").GetString(),
            secondBody.GetProperty("accountStatus").GetString());
        Assert.NotEqual(realPublicId, secondBody.GetProperty("publicId").GetGuid());

        // The synthetic PublicId's UUID version must match a real one, too — a v4 fallback would
        // itself be an oracle even though every other part of the response is identical (Alex
        // review, 2026-08-24).
        Assert.Equal(UuidVersion(realPublicId), UuidVersion(secondBody.GetProperty("publicId").GetGuid()));

        // A duplicate registration attempt must not trigger a second verification email either.
        var singleMessage = await capturingEmailSender.WaitForSingleMessageAsync();
        Assert.Contains(realPublicId.ToString("D"), singleMessage.TextBody);
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
    public async Task ConfirmEmailVerification_WhenAccountWasSuspendedAfterTheTokenWasIssued_RejectsTheTokenAndLeavesTheAccountSuspended()
    {
        // A token issued while pending verification must not be able to reactivate an account
        // that was suspended in the meantime (Alex review, 2026-08-21): ApplicationUser.ConfirmEmail
        // now requires PendingEmailVerification, and MemberRegistrationGateway guards on status
        // before even calling UserManager.ConfirmEmailAsync.
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
        var (_, token) = ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        await SuspendUserAsync(factory, publicId);

        using var confirmResponse = await client.PostAsJsonAsync("/api/v1/auth/email-verifications/confirm", new
        {
            userPublicId = publicId,
            token,
        });
        using var document = await ReadProblemDetailsAsync(confirmResponse);

        Assert.Equal(HttpStatusCode.BadRequest, confirmResponse.StatusCode);
        Assert.Equal(
            AuthErrorCodes.EmailTokenInvalid,
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal("suspended", await GetAccountStatusAsync(factory, publicId));
    }

    [Fact]
    public async Task ConfirmEmailVerification_WhenPersistingTheConfirmationFails_DoesNotReportSuccess()
    {
        // MemberRegistrationGateway.ConfirmEmailAsync now wraps Identity's ConfirmEmailAsync (which
        // persists EmailConfirmed=true as a side effect of successful token validation), the
        // AccountStatus transition, and the security-stamp rotation in one transaction, throwing
        // instead of silently reporting success when either later step fails (Alex review,
        // 2026-08-21, hardened 2026-08-24 after a follow-up review found EmailConfirmed itself
        // could still be left true even though AccountStatus correctly rolled back). This test
        // fails the AccountStatus-persisting UpdateAsync call and checks both fields land back at
        // their pre-confirmation values, not just AccountStatus. FailingUpdateUserManager
        // intercepts that one call with a synthetic failure while every other UserManager
        // operation (including the built-in ConfirmEmailAsync token validation) still runs against
        // the real, SQL-Server-backed store underneath. The confirm call is driven directly against
        // the gateway (not through HTTP) so the same manually-created scope resolves both the
        // gateway and the UserManager it depends on, making the failure controllable from the test.
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
            services.Replace(ServiceDescriptor.Scoped<UserManager<ApplicationUser>>(
                sp => new FailingUpdateUserManager(sp)));
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
        var (_, token) = ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = (FailingUpdateUserManager)scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            manager.FailNextUpdate = true;

            var gateway = scope.ServiceProvider.GetRequiredService<IMemberRegistrationGateway>();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => gateway.ConfirmEmailAsync(publicId, token, CancellationToken.None));
        }

        // The failed attempt must not have left the account confirmed in either field: the
        // AccountStatus transition and the EmailConfirmed flag Identity wrote inside the same
        // transaction must both have rolled back together.
        Assert.Equal("pendingEmailVerification", await GetAccountStatusAsync(factory, publicId));
        Assert.False(await GetEmailConfirmedAsync(factory, publicId));
    }

    [Fact]
    public async Task ConfirmEmailVerification_WhenSecurityStampRotationFails_RollsBackTheWholeConfirmation()
    {
        // The other failure boundary in the same atomic confirmation: if the security-stamp
        // rotation fails *after* EmailConfirmed and AccountStatus were already written earlier in
        // the transaction, all three must still roll back together — otherwise a confirmed account
        // could be left with a stale SecurityStamp, letting the original (now-consumed) token
        // replay past the guard the stamp rotation exists to enforce (Alex review, 2026-08-24).
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
            services.Replace(ServiceDescriptor.Scoped<UserManager<ApplicationUser>>(
                sp => new FailingUpdateUserManager(sp)));
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
        var (_, token) = ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = (FailingUpdateUserManager)scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            manager.FailNextSecurityStampUpdate = true;

            var gateway = scope.ServiceProvider.GetRequiredService<IMemberRegistrationGateway>();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => gateway.ConfirmEmailAsync(publicId, token, CancellationToken.None));
        }

        Assert.Equal("pendingEmailVerification", await GetAccountStatusAsync(factory, publicId));
        Assert.False(await GetEmailConfirmedAsync(factory, publicId));
    }

    [Fact]
    public async Task ConfirmPasswordReset_WhenAccountWasSuspendedAfterTheTokenWasIssued_RejectsTheToken()
    {
        // A password reset token generated while the account was eligible must stop working the
        // instant the account is suspended (Alex review, 2026-08-21): MemberPasswordResetGateway
        // now re-checks eligibility when the token is consumed, not just when it is issued.
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
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
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var publicId = registerBody.GetProperty("publicId").GetGuid();
        var (verifyPublicId, verifyToken) =
            ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);
        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/email-verifications/confirm",
            new { userPublicId = verifyPublicId, token = verifyToken });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        capturingEmailSender.SentMessages.Clear();

        using var requestResetResponse = await client.PostAsJsonAsync("/api/v1/auth/password-resets", new { email });
        Assert.Equal(HttpStatusCode.Accepted, requestResetResponse.StatusCode);
        var (resetPublicId, resetToken) =
            ExtractVerificationLink((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        await SuspendUserAsync(factory, resetPublicId);

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
        // still consume budget even though the account already exists, because the throttle is
        // checked before the gateway call and must not itself leak account existence. Duplicate
        // attempts return the same 202 shape as the first (non-enumerable registration).
        using var firstResponse = await client.PostAsJsonAsync("/api/v1/auth/register", Payload());
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/v1/auth/register", Payload());
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);

        using var thirdResponse = await client.PostAsJsonAsync("/api/v1/auth/register", Payload());
        Assert.Equal(HttpStatusCode.Accepted, thirdResponse.StatusCode);

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

    // No admin-suspend endpoint is exposed on this branch yet, so tests that need a suspended
    // account reach past the HTTP surface and drive the domain method directly — the same one a
    // future admin flow would call — to prove the fix at the layer that actually matters.
    private static async Task SuspendUserAsync(WebApplicationFactory<Program> factory, Guid userPublicId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.Users.SingleAsync(candidate => candidate.PublicId == userPublicId);
        user.Suspend(DateTime.UtcNow);
        var result = await userManager.UpdateAsync(user);
        Assert.True(result.Succeeded);
    }

    private static async Task<string> GetAccountStatusAsync(WebApplicationFactory<Program> factory, Guid userPublicId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.Users.SingleAsync(candidate => candidate.PublicId == userPublicId);
        return AccountStatusTokens.ToToken(user.AccountStatus);
    }

    private static async Task<bool> GetEmailConfirmedAsync(WebApplicationFactory<Program> factory, Guid userPublicId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.Users.SingleAsync(candidate => candidate.PublicId == userPublicId);
        return user.EmailConfirmed;
    }

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

    private static int UuidVersion(Guid guid) => Convert.ToInt32(guid.ToString("N")[12].ToString(), 16);

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

    // UserManager<TUser>.UpdateAsync/UpdateSecurityStampAsync are virtual specifically to support
    // this kind of test seam: every other operation (including the built-in ConfirmEmailAsync
    // token validation) still goes through the real, DI-resolved store, so this only fakes the
    // exact two calls MemberRegistrationGateway.ConfirmEmailAsync now checks the result of.
    private sealed class FailingUpdateUserManager(IServiceProvider services) : UserManager<ApplicationUser>(
        services.GetRequiredService<IUserStore<ApplicationUser>>(),
        services.GetRequiredService<IOptions<IdentityOptions>>(),
        services.GetRequiredService<IPasswordHasher<ApplicationUser>>(),
        services.GetServices<IUserValidator<ApplicationUser>>(),
        services.GetServices<IPasswordValidator<ApplicationUser>>(),
        services.GetRequiredService<ILookupNormalizer>(),
        services.GetRequiredService<IdentityErrorDescriber>(),
        services,
        services.GetRequiredService<ILogger<UserManager<ApplicationUser>>>())
    {
        private static readonly IdentityError SyntheticFailure = new()
        {
            Code = "SyntheticTestFailure",
            Description = "Synthetic persistence failure injected by a test.",
        };

        public bool FailNextUpdate { get; set; }

        public bool FailNextSecurityStampUpdate { get; set; }

        public override Task<IdentityResult> UpdateAsync(ApplicationUser user)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                return Task.FromResult(IdentityResult.Failed(SyntheticFailure));
            }

            return base.UpdateAsync(user);
        }

        public override Task<IdentityResult> UpdateSecurityStampAsync(ApplicationUser user)
        {
            if (FailNextSecurityStampUpdate)
            {
                FailNextSecurityStampUpdate = false;
                return Task.FromResult(IdentityResult.Failed(SyntheticFailure));
            }

            return base.UpdateSecurityStampAsync(user);
        }
    }
}
