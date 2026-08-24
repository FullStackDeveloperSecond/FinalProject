using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Contracts.Auth;
using DoSelect.Application.Members;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

public sealed class MemberCleanupTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PurgeAsync_AnonymizesUnverifiedAccountsPastTheSevenDayRetentionWindowButLeavesNewerOnesAlone()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var isolatedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            });
        });
        using var client = isolatedFactory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var stalePublicId = await RegisterAsync(client);

        // Advance the clock past the 7-day retention window before registering the second
        // account, so only the first one is stale when the purge job runs "now".
        clock.Advance(PurgeStaleUnverifiedMembersService.UnverifiedRetentionPeriod + TimeSpan.FromHours(1));

        var freshPublicId = await RegisterAsync(client);

        // The database is shared across the whole test run, so other tests' never-confirmed
        // PendingEmailVerification accounts are also fair game once the clock advances past their
        // real creation time — assert on the two accounts this test controls, not on the total
        // count purged.
        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var purgeService = scope.ServiceProvider.GetRequiredService<PurgeStaleUnverifiedMembersService>();
            var anonymizedCount = await purgeService.PurgeAsync();
            Assert.True(anonymizedCount >= 1);
        }

        Assert.Equal("anonymized", await GetAccountStatusAsync(isolatedFactory, stalePublicId));
        Assert.Equal("pendingEmailVerification", await GetAccountStatusAsync(isolatedFactory, freshPublicId));
    }

    private static async Task<Guid> RegisterAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"member-cleanup-test-{Guid.NewGuid():N}@example.com",
            password = "correct-horse-battery-staple",
            displayName = "整合測試會員",
            acceptTermsVersion = 1,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("publicId").GetGuid();
    }

    private static async Task<string> GetAccountStatusAsync(WebApplicationFactory<Program> targetFactory, Guid userPublicId)
    {
        await using var scope = targetFactory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.Users.SingleAsync(candidate => candidate.PublicId == userPublicId);
        return AccountStatusTokens.ToToken(user.AccountStatus);
    }

    private static async Task PrimeAntiforgeryAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "member");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", body.GetProperty("requestToken").GetString());
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
