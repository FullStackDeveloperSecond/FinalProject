using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Security;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Admin;

/// <summary>
/// M-01B（管理員登入／TOTP／Recovery Code／Enrollment／Rebind）的端對端回歸測試
/// （alex review P1#6）。目前完全沒有對應測試，這裡覆蓋審查要求的核心情境；
/// challenge 逾時（時間流逝）不易在測試中可靠模擬，未涵蓋，詳見 PR 說明。
/// </summary>
public sealed class AdminAuthControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Password = "correct-horse-battery-staple";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminAuthControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TwoStageLogin_WithCorrectPasswordAndTotpCode_ReturnsAuthenticatedSession()
    {
        var (client, email, secret) = await CreateEnrolledAdminAsync();

        using var loginResponse = await LoginAsync(client, email, Password);
        var challengePublicId = await ReadChallengePublicIdAsync(loginResponse, requiresEnrollment: false);

        var code = TotpTestHelper.GenerateCode(secret);
        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/verify", new { challengePublicId, code });
        var verifyBody = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.Equal(EmailMasking.Mask(email), verifyBody.GetProperty("user").GetProperty("emailMasked").GetString());

        using var sessionResponse = await client.GetAsync("/api/v1/admin/auth/session");
        var sessionBody = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sessionBody.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task VerifyTotp_WhenTheCodeIsWrong_ReturnsTwoFactorInvalid()
    {
        var (client, email, _) = await CreateEnrolledAdminAsync();

        using var loginResponse = await LoginAsync(client, email, Password);
        var challengePublicId = await ReadChallengePublicIdAsync(loginResponse, requiresEnrollment: false);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/verify", new { challengePublicId, code = "000000" });
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("admin_two_factor_invalid", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FirstTimeEnrollment_WithTheCorrectCode_ReturnsRecoveryCodesAndAuthenticatedSession()
    {
        var (client, email) = await CreateActiveAdminWithoutTotpAsync();

        using var loginResponse = await LoginAsync(client, email, Password);
        var challengePublicId = await ReadChallengePublicIdAsync(loginResponse, requiresEnrollment: true);

        using var beginResponse = await client.PostAsync(
            $"/api/v1/admin/auth/totp/enroll/begin?challengePublicId={challengePublicId}", content: null);
        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);
        var beginBody = await beginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secretKey = beginBody.GetProperty("secretKey").GetString()!;

        var code = TotpTestHelper.GenerateCode(secretKey);
        using var confirmResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/enroll/confirm", new { challengePublicId, code });
        var confirmBody = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Equal(10, confirmBody.GetProperty("recoveryCodes").GetArrayLength());

        using var sessionResponse = await client.GetAsync("/api/v1/admin/auth/session");
        var sessionBody = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sessionBody.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task VerifyTotp_WhenAttemptsExceedTheRateLimit_InvalidatesTheChallengeAndReturns429()
    {
        var (client, email, secret) = await CreateEnrolledAdminAsync(rateLimitOverride: new RateLimitOptions
        {
            AdminChallengePermitLimit = 3,
            AdminChallengeWindowMinutes = 15,
        });

        using var loginResponse = await LoginAsync(client, email, Password);
        var challengePublicId = await ReadChallengePublicIdAsync(loginResponse, requiresEnrollment: false);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var wrongResponse = await client.PostAsJsonAsync(
                "/api/v1/admin/auth/totp/verify", new { challengePublicId, code = "000000" });
            Assert.Equal(HttpStatusCode.BadRequest, wrongResponse.StatusCode);
        }

        using var rateLimitedResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/verify", new { challengePublicId, code = "000000" });
        using var rateLimitedDocument = await ReadProblemDetailsAsync(rateLimitedResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);
        Assert.Equal(
            "admin_challenge_rate_limited", rateLimitedDocument.RootElement.GetProperty("code").GetString());

        // Challenge 已被強制失效——即使接下來送出「正確」的碼也一樣被拒絕，證明不是單純
        // 沒扣到配額，而是 challenge 本身真的死了。
        var correctCode = TotpTestHelper.GenerateCode(secret);
        using var afterRateLimitResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/verify", new { challengePublicId, code = correctCode });
        using var afterRateLimitDocument = await ReadProblemDetailsAsync(afterRateLimitResponse);
        Assert.Equal(HttpStatusCode.BadRequest, afterRateLimitResponse.StatusCode);
        Assert.Equal(
            "admin_challenge_invalid", afterRateLimitDocument.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VerifyTotp_WhenTheAdminWasSuspendedAfterPasswordCheck_ReturnsAccountSuspended()
    {
        var (client, email, secret, userId, factory) = await CreateEnrolledAdminWithUserIdAsync();

        using var loginResponse = await LoginAsync(client, email, Password);
        var challengePublicId = await ReadChallengePublicIdAsync(loginResponse, requiresEnrollment: false);

        // 密碼驗證通過、challenge 已簽發之後才停權——模擬「MFA 完成前被停權」（alex review P1#4）。
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            var profile = await dbContext.AdminProfiles.SingleAsync(p => p.UserId == userId);
            profile.SetActive(false, DateTime.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        var code = TotpTestHelper.GenerateCode(secret);
        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/verify", new { challengePublicId, code });
        using var document = await ReadProblemDetailsAsync(verifyResponse);

        Assert.Equal(HttpStatusCode.BadRequest, verifyResponse.StatusCode);
        Assert.Equal("account_suspended", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Session_WhenTheAdminIsSuspendedAfterACompletedLogin_IsRevokedImmediatelyEvenWithoutASecurityStampChange()
    {
        // alex review 第二輪 P1#3 核心回歸測試：原本的 Cookie 驗證只比對 SecurityStamp，
        // 如果停權路徑忘記 bump Stamp，舊 Cookie 在到期前（最長 2 小時）仍會通過驗證。
        // 這裡完全不動 SecurityStamp，只改 AdminProfile.IsActive，證明 Cookie 驗證本身
        // 就會重新確認資格，不依賴其他程式碼是否記得撤銷 Stamp。
        var (client, email, secret, userId, factory) = await CreateEnrolledAdminWithUserIdAsync();
        using var verifyResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        using var beforeSuspendSession = await client.GetAsync("/api/v1/admin/auth/session");
        var beforeSuspendBody = await beforeSuspendSession.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(beforeSuspendBody.GetProperty("isAuthenticated").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            var profile = await dbContext.AdminProfiles.SingleAsync(p => p.UserId == userId);
            profile.SetActive(false, DateTime.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var afterSuspendSession = await client.GetAsync("/api/v1/admin/auth/session");
        var afterSuspendBody = await afterSuspendSession.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(afterSuspendBody.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Rebind_ConfirmWithTheCorrectCode_RevokesTheOldSessionCookie()
    {
        var (loginClient, email, secret, _, factory) = await CreateEnrolledAdminWithUserIdAsync();
        using var verifyResponse = await FullyLogInAsync(loginClient, email, secret);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var oldAdminCookie = ExtractSetCookieValue(verifyResponse, ".DoSelect.Admin");

        // "另一台裝置"：跟 loginClient 出自同一個 factory（共用同一組 DataProtection 金鑰，
        // Cookie 才能互相解密），但帶著 Rebind 之前的舊 Cookie。
        using var otherDeviceClient = factory.CreateClient();
        otherDeviceClient.DefaultRequestHeaders.Add("Cookie", oldAdminCookie);

        using var beforeRebindSession = await otherDeviceClient.GetAsync("/api/v1/admin/auth/session");
        var beforeRebindBody = await beforeRebindSession.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(beforeRebindBody.GetProperty("isAuthenticated").GetBoolean());

        await PrimeAdminAntiforgeryAsync(loginClient);
        using var beginResponse = await loginClient.PostAsync("/api/v1/admin/auth/totp/rebind/begin", content: null);
        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);
        var beginBody = await beginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var newSecret = beginBody.GetProperty("secretKey").GetString()!;
        var rebindChallengePublicId = beginBody.GetProperty("challengePublicId").GetGuid();

        var newCode = TotpTestHelper.GenerateCode(newSecret);
        using var confirmResponse = await loginClient.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/confirm",
            new { challengePublicId = rebindChallengePublicId, code = newCode });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        // 「另一台裝置」用舊 Cookie 打受保護端點應該已經失效。
        using var afterRebindSession = await otherDeviceClient.GetAsync("/api/v1/admin/auth/session");
        var afterRebindBody = await afterRebindSession.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(afterRebindBody.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Rebind_ConfirmWithTheWrongCode_RollsBackAndTheOriginalSecretStillWorks()
    {
        // 這支測試就是能抓到 P2#7 那個 bug 的測試：修正前，BeginRebindAsync 會立即無條件
        // 覆蓋正式 authenticator key，Confirm 失敗也不會復原，原本的 Authenticator App
        // 就此永久失效。
        var (client, email, secret) = await CreateEnrolledAdminAsync();
        using var fullLoginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, fullLoginResponse.StatusCode);

        await PrimeAdminAntiforgeryAsync(client);
        using var beginResponse = await client.PostAsync("/api/v1/admin/auth/totp/rebind/begin", content: null);
        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);
        var beginBody = await beginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rebindChallengePublicId = beginBody.GetProperty("challengePublicId").GetGuid();

        using var confirmResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/confirm",
            new { challengePublicId = rebindChallengePublicId, code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, confirmResponse.StatusCode);

        // 用「原本」的秘鑰重新走一次完整登入，證明舊裝置依然能正常運作。
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        await PrimeAdminAntiforgeryAsync(client);
        using var reloginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    private async Task<HttpResponseMessage> FullyLogInAsync(HttpClient client, string email, string secret)
    {
        using var loginResponse = await LoginAsync(client, email, Password);
        var challengePublicId = await ReadChallengePublicIdAsync(loginResponse, requiresEnrollment: false);
        var code = TotpTestHelper.GenerateCode(secret);
        return await client.PostAsJsonAsync("/api/v1/admin/auth/totp/verify", new { challengePublicId, code });
    }

    private async Task<(HttpClient Client, string Email, string Secret)> CreateEnrolledAdminAsync(
        RateLimitOptions? rateLimitOverride = null)
    {
        var (client, email, secret, _, _) = await CreateEnrolledAdminWithUserIdAsync(rateLimitOverride);
        return (client, email, secret);
    }

    private async Task<(HttpClient Client, string Email, string Secret, string UserId, WebApplicationFactory<Program> Factory)>
        CreateEnrolledAdminWithUserIdAsync(RateLimitOptions? rateLimitOverride = null)
    {
        var factory = CreateIsolatedFactory(rateLimitOverride);
        var client = factory.CreateClient();
        await PrimeAdminAntiforgeryAsync(client);

        var email = UniqueEmail();
        string secret;
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();

            var now = DateTime.UtcNow;
            var user = ApplicationUser.CreateAdmin(Guid.NewGuid(), email, now);
            user.ConfirmEmail(now);
            var createResult = await userManager.CreateAsync(user, Password);
            Assert.True(createResult.Succeeded, string.Join(";", createResult.Errors.Select(e => e.Description)));
            await EnsureSuperAdminRoleAsync(scope.ServiceProvider, user);

            dbContext.AdminProfiles.Add(
                new AdminProfile(user.Id, Guid.NewGuid(), UniqueEmployeeCode(), "整合測試管理員", now));
            await dbContext.SaveChangesAsync();

            await userManager.ResetAuthenticatorKeyAsync(user);
            secret = (await userManager.GetAuthenticatorKeyAsync(user))!;
            var enableResult = await userManager.SetTwoFactorEnabledAsync(user, true);
            Assert.True(enableResult.Succeeded);

            userId = user.Id;
        }

        return (client, email, secret, userId, factory);
    }

    private async Task<(HttpClient Client, string Email)> CreateActiveAdminWithoutTotpAsync()
    {
        var factory = CreateIsolatedFactory();
        var client = factory.CreateClient();
        await PrimeAdminAntiforgeryAsync(client);

        var email = UniqueEmail();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();

        var now = DateTime.UtcNow;
        var user = ApplicationUser.CreateAdmin(Guid.NewGuid(), email, now);
        user.ConfirmEmail(now);
        var createResult = await userManager.CreateAsync(user, Password);
        Assert.True(createResult.Succeeded, string.Join(";", createResult.Errors.Select(e => e.Description)));
        await EnsureSuperAdminRoleAsync(scope.ServiceProvider, user);

        dbContext.AdminProfiles.Add(new AdminProfile(user.Id, Guid.NewGuid(), UniqueEmployeeCode(), "整合測試管理員", now));
        await dbContext.SaveChangesAsync();

        return (client, email);
    }

    /// <summary>
    /// 中央 AuditLog 的 AuditActor 要求 Admin Actor 至少要有一個角色（見
    /// AuditContracts.AuditActor.Create）——這裡的測試管理員也要跟
    /// MinimalDevelopmentDataSeeder 的正式管理員一樣至少有一個角色，稽核寫入
    /// （enrollment／verify 429／rebind 等）才不會因為零角色而丟例外。角色資料表本身
    /// 不保證在測試環境已被種好，所以先確保角色存在再指派。
    /// </summary>
    private static async Task EnsureSuperAdminRoleAsync(IServiceProvider services, ApplicationUser user)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(DoSelectRoles.SuperAdmin))
        {
            await roleManager.CreateAsync(new IdentityRole(DoSelectRoles.SuperAdmin));
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleResult = await userManager.AddToRoleAsync(user, DoSelectRoles.SuperAdmin);
        Assert.True(roleResult.Succeeded, string.Join(";", roleResult.Errors.Select(e => e.Description)));
    }

    private static async Task<Guid> ReadChallengePublicIdAsync(HttpResponseMessage loginResponse, bool requiresEnrollment)
    {
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(requiresEnrollment, body.GetProperty("requiresEnrollment").GetBoolean());
        return body.GetProperty("twoFactorChallengePublicId").GetGuid();
    }

    private static string ExtractSetCookieValue(HttpResponseMessage response, string cookieNamePrefix)
    {
        var setCookieValues = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
            : [];
        var match = setCookieValues.FirstOrDefault(
            v => v.StartsWith(cookieNamePrefix + "=", StringComparison.Ordinal));
        Assert.NotNull(match);

        // 只需要 "name=value" 這段給 Cookie 請求標頭用，不需要 Set-Cookie 的其餘屬性
        // （Path／HttpOnly／SameSite…）。
        return match!.Split(';')[0];
    }

    // Each test that mutates state gets its own host with an in-memory DataProtection key ring
    // (see LoginControllerTests.CreateIsolatedFactory for why this is required).
    private WebApplicationFactory<Program> CreateIsolatedFactory(RateLimitOptions? rateLimitOverride = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                if (rateLimitOverride is not null)
                {
                    // 疊加在既有設定綁定之後執行，同一個 IOptions<RateLimitOptions> 實例
                    // 的這兩個屬性會被覆寫成測試要的值（Options 的 Configure<T> 委派依註冊
                    // 順序依序套用，不是整個取代）。
                    services.Configure<RateLimitOptions>(options =>
                    {
                        options.AdminChallengePermitLimit = rateLimitOverride.AdminChallengePermitLimit;
                        options.AdminChallengeWindowMinutes = rateLimitOverride.AdminChallengeWindowMinutes;
                    });
                }
            });
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email, password });

    private static async Task PrimeAdminAntiforgeryAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "admin");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", body.GetProperty("requestToken").GetString());
    }

    private static string UniqueEmail() => $"admin-auth-test-{Guid.NewGuid():N}@example.com";

    private static string UniqueEmployeeCode() => $"EMP-{Guid.NewGuid():N}"[..12];

    private static async Task<JsonDocument> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
