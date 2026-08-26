using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Security;
using DoSelect.Domain.Auditing;
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
        var stepUpCode = TotpTestHelper.GenerateCode(secret);
        using var beginResponse = await loginClient.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = stepUpCode, recoveryCode = (string?)null });
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
    public async Task Login_WhenTheFifthWrongPasswordTriggersLockout_ReturnsInvalidCredentialsPubliclyButWritesACentralAuditEntry()
    {
        // ⚠ alex review：帳號枚舉 + 30 分鐘 Lockout 必須跟中央 Audit 同一交易——公開回應永遠是
        // invalid_credentials（不能回 account_locked），但觸發鎖定當下必須留下一筆可稽核紀錄。
        var (client, email, _, userId, factory) = await CreateEnrolledAdminWithUserIdAsync();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var wrongResponse = await LoginAsync(client, email, "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        }

        using var fifthResponse = await LoginAsync(client, email, "wrong-password");
        using var fifthDocument = await ReadProblemDetailsAsync(fifthResponse);
        Assert.Equal(HttpStatusCode.Unauthorized, fifthResponse.StatusCode);
        Assert.Equal(
            AdminAuthErrorCodes.InvalidCredentials, fifthDocument.RootElement.GetProperty("code").GetString());

        // 就算帳號現在已經鎖定，第六次嘗試（正確密碼也一樣）公開回應仍然是同一種
        // invalid_credentials，不會變成 account_locked。
        using var sixthResponse = await LoginAsync(client, email, Password);
        using var sixthDocument = await ReadProblemDetailsAsync(sixthResponse);
        Assert.Equal(HttpStatusCode.Unauthorized, sixthResponse.StatusCode);
        Assert.Equal(
            AdminAuthErrorCodes.InvalidCredentials, sixthDocument.RootElement.GetProperty("code").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .GetLockoutEndDateAsync(user!));

        // 剛好觸發鎖定的那一次（第五次）寫了一筆 Audit；已經鎖定後的第六次不會再重複寫入。
        var lockoutAudits = await dbContext.AuditLogs
            .Where(a => a.Action == AuditActions.AdminAccountLockout && a.ResourcePublicId == user!.PublicId)
            .ToListAsync();
        Assert.Single(lockoutAudits);
        Assert.Equal(AuditResult.Rejected, lockoutAudits[0].Result);

        // ⚠ alex review 最新一輪 P1#2：Actor 必須是 System，不能把被鎖定的管理員自己記成
        // 施暴的 Actor——匿名密碼嘗試造成的鎖定，沒有真正「登入的人」可以當 Actor。
        Assert.Equal(AuditActorType.System, lockoutAudits[0].ActorType);
        Assert.Null(lockoutAudits[0].ActorPublicId);
    }

    [Fact]
    public async Task Login_WhenTheFifthWrongPasswordTriggersLockoutForAZeroRoleAdmin_StillLocksTheAccountWithoutA500()
    {
        // ⚠ alex review 最新一輪 P1#2 核心回歸測試：修正前，Lockout Audit 把被鎖定的管理員自己
        // 記成 Actor；AuditActor.Create(Admin, ..., roles) 對零角色的 Admin Actor 會直接拋例外
        // （見 AuditContracts.cs），而 Lockout 與 Audit 又在同一交易——例外會讓整筆鎖定 rollback，
        // 回應變成 500，鎖定門檻形同虛設。目前登入資格與 Admin policy 都沒有要求至少一個角色，
        // 所以零角色管理員是真實可能發生的狀態，不能靠「反正一定有角色」規避這個情境。
        var (client, email, userId, factory) = await CreateEnrolledAdminWithoutAnyRoleAsync();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var wrongResponse = await LoginAsync(client, email, "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        }

        using var fifthResponse = await LoginAsync(client, email, "wrong-password");
        using var fifthDocument = await ReadProblemDetailsAsync(fifthResponse);
        Assert.Equal(HttpStatusCode.Unauthorized, fifthResponse.StatusCode);
        Assert.Equal(
            AdminAuthErrorCodes.InvalidCredentials, fifthDocument.RootElement.GetProperty("code").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .GetLockoutEndDateAsync(user!));

        var lockoutAudits = await dbContext.AuditLogs
            .Where(a => a.Action == AuditActions.AdminAccountLockout && a.ResourcePublicId == user!.PublicId)
            .ToListAsync();
        Assert.Single(lockoutAudits);
        Assert.Equal(AuditActorType.System, lockoutAudits[0].ActorType);
        Assert.Null(lockoutAudits[0].ActorPublicId);
        Assert.Equal(user!.PublicId, lockoutAudits[0].ResourcePublicId);
    }

    [Fact]
    public async Task Login_WhenTheAccountIsSuspendedAndThePasswordIsCorrect_Returns403AccountSuspended()
    {
        var (client, email, _, userId, factory) = await CreateEnrolledAdminWithUserIdAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            var profile = await dbContext.AdminProfiles.SingleAsync(p => p.UserId == userId);
            profile.SetActive(false, DateTime.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var response = await LoginAsync(client, email, Password);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AdminAuthErrorCodes.AccountSuspended, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BeginRebind_WithoutStepUpCredentials_ReturnsStepUpRequiredAndDoesNotIssueAChallenge()
    {
        var (client, email, secret) = await CreateEnrolledAdminAsync();
        using var fullLoginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, fullLoginResponse.StatusCode);

        await PrimeAdminAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = (string?)null, recoveryCode = (string?)null });
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            AdminAuthErrorCodes.RebindStepUpRequired, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BeginRebind_WithAnInvalidTotpCode_ReturnsTwoFactorInvalidAndTheOriginalSecretStillWorks()
    {
        var (client, email, secret) = await CreateEnrolledAdminAsync();
        using var fullLoginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, fullLoginResponse.StatusCode);

        await PrimeAdminAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = "000000", recoveryCode = (string?)null });
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AdminAuthErrorCodes.TwoFactorInvalid, document.RootElement.GetProperty("code").GetString());

        // 沒有 pending secret 也沒有簽發 rebind challenge——原本的裝置重新登入仍然有效。
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        await PrimeAdminAntiforgeryAsync(client);
        using var reloginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    [Fact]
    public async Task BeginRebind_WhenStepUpAttemptsExceedTheRateLimit_Returns429EvenWithACorrectCode()
    {
        // ⚠ alex review 最新一輪 P1#1：Rebind step-up 原本只套用 per-IP 的 AuthLogin 限流
        // （每小時 20 次），沒有重用既有的三桶（IP＋step-up＋帳號）限流器——Admin Session 被偷後
        // 換 IP 就能對同一帳號無限猜舊 TOTP／Recovery Code，密碼 Lockout 也保護不到這個端點。
        // 這裡把門檻調小到 2 次，證明額度用滿後，就算第三次真的帶正確的 TOTP 碼，也會在驗證
        // 憑證「之前」被擋下（429），不會走到 BeginRebindAsync、不會建立 pending secret。三桶
        // 各自獨立、換 IP／換 Session 不會重置額度的細節已由 AdminChallengeRateLimiterTests
        // 涵蓋，這裡只證明 BeginRebind 端點真的有套用同一套限流器。
        var (client, email, secret) = await CreateEnrolledAdminAsync(rateLimitOverride: new RateLimitOptions
        {
            AdminChallengePermitLimit = 2,
            AdminChallengeWindowMinutes = 15,
        });
        using var fullLoginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, fullLoginResponse.StatusCode);

        await PrimeAdminAntiforgeryAsync(client);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var wrongResponse = await client.PostAsJsonAsync(
                "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = "000000", recoveryCode = (string?)null });
            Assert.Equal(HttpStatusCode.BadRequest, wrongResponse.StatusCode);
        }

        var correctCode = TotpTestHelper.GenerateCode(secret);
        using var rateLimitedResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = correctCode, recoveryCode = (string?)null });
        using var rateLimitedDocument = await ReadProblemDetailsAsync(rateLimitedResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);
        Assert.Equal(
            AdminAuthErrorCodes.ChallengeRateLimited, rateLimitedDocument.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BeginRebind_WithTheCurrentTotpCode_SucceedsAndIssuesARebindChallenge()
    {
        var (client, email, secret) = await CreateEnrolledAdminAsync();
        using var fullLoginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, fullLoginResponse.StatusCode);

        await PrimeAdminAntiforgeryAsync(client);
        var totpCode = TotpTestHelper.GenerateCode(secret);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode, recoveryCode = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("challengePublicId").GetGuid());
    }

    [Fact]
    public async Task BeginRebind_WithARecoveryCode_SucceedsAndConsumesTheCodeSoItCannotBeReusedAfterward()
    {
        var (client, email, secret, userId, factory) = await CreateEnrolledAdminWithUserIdAsync();
        string recoveryCode;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user!, 10);
            recoveryCode = codes!.First();
        }

        using var fullLoginResponse = await FullyLogInAsync(client, email, secret);
        Assert.Equal(HttpStatusCode.OK, fullLoginResponse.StatusCode);

        await PrimeAdminAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = (string?)null, recoveryCode });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 同一組 Recovery Code 是單次有效——用同一組碼再嘗試一次 step-up 必須失敗。
        using var reuseResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = (string?)null, recoveryCode });
        using var reuseDocument = await ReadProblemDetailsAsync(reuseResponse);
        Assert.Equal(HttpStatusCode.BadRequest, reuseResponse.StatusCode);
        Assert.Equal(
            AdminAuthErrorCodes.RecoveryCodeInvalid, reuseDocument.RootElement.GetProperty("code").GetString());
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
        var stepUpCode = TotpTestHelper.GenerateCode(secret);
        using var beginResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/totp/rebind/begin", new { totpCode = stepUpCode, recoveryCode = (string?)null });
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
    /// ⚠ 刻意不指派任何角色（不呼叫 <see cref="EnsureSuperAdminRoleAsync"/>）——用來重現
    /// Lockout Audit 誤把被鎖定管理員當 Actor 時，零角色會讓 AuditActor.Create 拋例外的那個
    /// bug（alex review 最新一輪 P1#2）。目前登入資格與 Admin policy 都沒有要求至少一個角色，
    /// 零角色管理員是真實可能發生的狀態。
    /// </summary>
    private async Task<(HttpClient Client, string Email, string UserId, WebApplicationFactory<Program> Factory)>
        CreateEnrolledAdminWithoutAnyRoleAsync()
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

        dbContext.AdminProfiles.Add(new AdminProfile(user.Id, Guid.NewGuid(), UniqueEmployeeCode(), "整合測試管理員", now));
        await dbContext.SaveChangesAsync();

        return (client, email, user.Id, factory);
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
