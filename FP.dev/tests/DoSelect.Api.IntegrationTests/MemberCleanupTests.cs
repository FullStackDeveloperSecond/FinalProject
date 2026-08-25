using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Contracts.Auth;
using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
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

public sealed class MemberCleanupTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PurgeAsync_AnonymizesTheAccountItBackdatedButLeavesEverythingElseInTheSharedDatabaseAlone()
    {
        // Backdates only the one row this test creates, using the real system clock for the purge
        // query — unlike advancing a shared TimeProvider forward, this can never sweep up other
        // tests' leftover PendingEmailVerification accounts in the same shared database (Alex
        // review, 2026-08-24: xUnit 平行執行時可能把其他 Auth 測試正在使用的帳號匿名化).
        using var isolatedFactory = CreateIsolatedFactory();
        using var client = isolatedFactory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var staleEmail = UniqueEmail();
        var stalePublicId = await RegisterAsync(client, staleEmail, "會員小美");
        await BackdateCreatedAtAsync(
            isolatedFactory,
            stalePublicId,
            DateTime.UtcNow - PurgeStaleUnverifiedMembersService.UnverifiedRetentionPeriod - TimeSpan.FromHours(1));

        var freshEmail = UniqueEmail();
        var freshPublicId = await RegisterAsync(client, freshEmail, "會員小明");

        var anonymizedCount = await RunPurgeAsync(isolatedFactory);
        Assert.Equal(1, anonymizedCount);

        // The stale account: ApplicationUser is anonymized (email cleared, so the same address can
        // be registered again) and — the part that was previously missing — MemberProfile's
        // removable PII (DisplayName, BirthDate) is cleared too, not just left in place.
        var (staleStatus, staleEmailField, staleDisplayName, staleBirthDate) =
            await GetMemberSnapshotAsync(isolatedFactory, stalePublicId);
        Assert.Equal("anonymized", staleStatus);
        Assert.Null(staleEmailField);
        Assert.Equal("匿名會員", staleDisplayName);
        Assert.Null(staleBirthDate);

        // The fresh account is untouched.
        var (freshStatus, freshEmailField, freshDisplayName, _) =
            await GetMemberSnapshotAsync(isolatedFactory, freshPublicId);
        Assert.Equal("pendingEmailVerification", freshStatus);
        Assert.Equal(freshEmail, freshEmailField);
        Assert.Equal("會員小明", freshDisplayName);
    }

    [Fact]
    public async Task PurgeAsync_FreesTheEmailSoTheSameAddressCanRegisterAgainAfterwards()
    {
        using var isolatedFactory = CreateIsolatedFactory();
        using var client = isolatedFactory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var email = UniqueEmail();
        var publicId = await RegisterAsync(client, email, "會員小美");
        await BackdateCreatedAtAsync(
            isolatedFactory,
            publicId,
            DateTime.UtcNow - PurgeStaleUnverifiedMembersService.UnverifiedRetentionPeriod - TimeSpan.FromHours(1));

        var anonymizedCount = await RunPurgeAsync(isolatedFactory);
        Assert.Equal(1, anonymizedCount);

        using var reRegisterResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct-horse-battery-staple",
            displayName = "重新註冊會員",
            acceptTermsVersion = 1,
        });
        var reRegisterBody = await reRegisterResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, reRegisterResponse.StatusCode);
        // A genuinely new account, not the synthetic non-enumerable placeholder: the previous
        // owner of this email was anonymized, so this really is a fresh registration.
        Assert.NotEqual(publicId, reRegisterBody.GetProperty("publicId").GetGuid());
        Assert.Equal("pendingEmailVerification", reRegisterBody.GetProperty("accountStatus").GetString());
    }

    [Fact]
    public async Task PurgeAsync_WhenOneAccountsUpdateFails_StillAnonymizesTheOtherAccountIndependently()
    {
        // MemberCleanupGateway used to process the whole batch through the one DbContext it was
        // constructed with: rolling back a failed account's transaction only undoes what reached
        // the database, not the EF ChangeTracker's in-memory Modified state on the entities it had
        // already mutated, so the *next* account's SaveChanges could flush that stale tracked
        // state right alongside its own. Each account now gets its own DI scope/DbContext, so one
        // account's failure must not touch another's outcome at all (Alex review, 2026-08-25).
        var userIdsToFailUpdate = new HashSet<string>();
        using var isolatedFactory = CreateIsolatedFactory(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<UserManager<ApplicationUser>>(
                sp => new SelectivelyFailingUserManager(sp, userIdsToFailUpdate)));
        });
        using var client = isolatedFactory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var failingEmail = UniqueEmail();
        var failingPublicId = await RegisterAsync(client, failingEmail, "會員小美");
        var okEmail = UniqueEmail();
        var okPublicId = await RegisterAsync(client, okEmail, "會員小明");

        var cutoff = DateTime.UtcNow - PurgeStaleUnverifiedMembersService.UnverifiedRetentionPeriod - TimeSpan.FromHours(1);
        await BackdateCreatedAtAsync(isolatedFactory, failingPublicId, cutoff);
        await BackdateCreatedAtAsync(isolatedFactory, okPublicId, cutoff);

        userIdsToFailUpdate.Add(await GetIdentityUserIdAsync(isolatedFactory, failingPublicId));

        var anonymizedCount = await RunPurgeAsync(isolatedFactory);
        Assert.Equal(1, anonymizedCount);

        // The account whose UpdateAsync failed is left exactly as it was — not partially mutated.
        var (failingStatus, failingEmailField, failingDisplayName, _) =
            await GetMemberSnapshotAsync(isolatedFactory, failingPublicId);
        Assert.Equal("pendingEmailVerification", failingStatus);
        Assert.Equal(failingEmail, failingEmailField);
        Assert.Equal("會員小美", failingDisplayName);

        // The other account succeeds independently, unaffected by the first one's failure.
        var (okStatus, okEmailField, okDisplayName, _) = await GetMemberSnapshotAsync(isolatedFactory, okPublicId);
        Assert.Equal("anonymized", okStatus);
        Assert.Null(okEmailField);
        Assert.Equal("匿名會員", okDisplayName);
    }

    [Fact]
    public async Task PurgeAsync_WhenCandidateBecomesActiveBeforeItsChildScopeLoads_DoesNotAnonymizeIt()
    {
        using var isolatedFactory = CreateIsolatedFactory();
        using var client = isolatedFactory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var email = UniqueEmail();
        var publicId = await RegisterAsync(client, email, "剛完成驗證的會員");
        var cutoff = DateTime.UtcNow - PurgeStaleUnverifiedMembersService.UnverifiedRetentionPeriod;
        await BackdateCreatedAtAsync(isolatedFactory, publicId, cutoff - TimeSpan.FromHours(1));
        var userId = await GetIdentityUserIdAsync(isolatedFactory, publicId);

        await using var gatewayScope = isolatedFactory.Services.CreateAsyncScope();
        var services = gatewayScope.ServiceProvider;
        var gateway = new MemberCleanupGateway(
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<TimeProvider>(),
            new ActivateMemberBeforeFirstChildScopeFactory(
                services.GetRequiredService<IServiceScopeFactory>(),
                userId),
            services.GetRequiredService<ILogger<MemberCleanupGateway>>());

        var anonymizedCount = await gateway.AnonymizeStaleUnverifiedMembersAsync(cutoff);

        Assert.Equal(0, anonymizedCount);
        var (status, emailField, displayName, _) = await GetMemberSnapshotAsync(isolatedFactory, publicId);
        Assert.Equal("active", status);
        Assert.Equal(email, emailField);
        Assert.Equal("剛完成驗證的會員", displayName);
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<IServiceCollection>? configureServices = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                configureServices?.Invoke(services);
            });
        });

    private static async Task<Guid> RegisterAsync(HttpClient client, string email, string displayName)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct-horse-battery-staple",
            displayName,
            acceptTermsVersion = 1,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("publicId").GetGuid();
    }

    private static async Task BackdateCreatedAtAsync(
        WebApplicationFactory<Program> targetFactory,
        Guid userPublicId,
        DateTime createdAtUtc)
    {
        await using var scope = targetFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE AspNetUsers SET CreatedAtUtc = {createdAtUtc} WHERE PublicId = {userPublicId}");
    }

    private static async Task<int> RunPurgeAsync(WebApplicationFactory<Program> targetFactory)
    {
        await using var scope = targetFactory.Services.CreateAsyncScope();
        var purgeService = scope.ServiceProvider.GetRequiredService<PurgeStaleUnverifiedMembersService>();
        return await purgeService.PurgeAsync();
    }

    private static async Task<(string Status, string? Email, string? DisplayName, DateOnly? BirthDate)>
        GetMemberSnapshotAsync(WebApplicationFactory<Program> targetFactory, Guid userPublicId)
    {
        await using var scope = targetFactory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();

        var user = await userManager.Users.SingleAsync(candidate => candidate.PublicId == userPublicId);
        var profile = await dbContext.MemberProfiles
            .SingleOrDefaultAsync(candidate => candidate.UserId == user.Id);

        return (AccountStatusTokens.ToToken(user.AccountStatus), user.Email, profile?.DisplayName, profile?.BirthDate);
    }

    private static async Task<string> GetIdentityUserIdAsync(
        WebApplicationFactory<Program> targetFactory, Guid userPublicId)
    {
        await using var scope = targetFactory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.Users.SingleAsync(candidate => candidate.PublicId == userPublicId);
        return user.Id;
    }

    private static async Task PrimeAntiforgeryAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "member");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", body.GetProperty("requestToken").GetString());
    }

    private static string UniqueEmail() => $"member-cleanup-test-{Guid.NewGuid():N}@example.com";

    private sealed class ActivateMemberBeforeFirstChildScopeFactory(
        IServiceScopeFactory inner,
        string userId) : IServiceScopeFactory
    {
        private int _shouldActivate = 1;

        public IServiceScope CreateScope()
        {
            if (Interlocked.Exchange(ref _shouldActivate, 0) == 1)
            {
                using var activationScope = inner.CreateScope();
                var dbContext = activationScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
                var nowUtc = DateTime.UtcNow;
                var concurrencyStamp = Guid.NewGuid().ToString("D");
                var activeStatus = nameof(AccountStatus.Active);

                dbContext.Database.ExecuteSqlInterpolated(
                    $"UPDATE AspNetUsers SET AccountStatus = {activeStatus}, EmailConfirmed = {true}, UpdatedAtUtc = {nowUtc}, ConcurrencyStamp = {concurrencyStamp} WHERE Id = {userId}");
            }

            return inner.CreateScope();
        }
    }

    // Every DI scope MemberCleanupGateway creates (one per account, see its TryAnonymizeOneAsync)
    // resolves its own instance of this — correctly bound to that scope's own store/DbContext —
    // but all instances share the same userIdsToFail set passed in by reference, so the test can
    // target exactly one account's UpdateAsync for failure regardless of which scope processes it.
    private sealed class SelectivelyFailingUserManager(
        IServiceProvider services,
        HashSet<string> userIdsToFail) : UserManager<ApplicationUser>(
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

        public override Task<IdentityResult> UpdateAsync(ApplicationUser user) =>
            userIdsToFail.Contains(user.Id)
                ? Task.FromResult(IdentityResult.Failed(SyntheticFailure))
                : base.UpdateAsync(user);
    }
}
