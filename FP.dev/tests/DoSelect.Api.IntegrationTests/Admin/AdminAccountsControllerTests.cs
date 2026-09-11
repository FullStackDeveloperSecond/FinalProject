using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.IntegrationTests.Support;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Admin;

[Collection(nameof(AdminAuthApiCollection))]
public sealed class AdminAccountsControllerTests(AdminAuthApiFixture fixture)
{
    private const string BasePath = "/api/v1/admin/administrators";

    [Fact]
    public async Task AnonymousAndNonSuperAdminCallersCannotListAdministrators()
    {
        using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var forbidden = factory.CreateClient();
        forbidden.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, "ordinary-admin");
        forbidden.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, DoSelectRoles.OrderManager);

        using var anonymousResponse = await anonymous.GetAsync(BasePath);
        using var forbiddenResponse = await forbidden.GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task SuperAdminCanCreateAPendingAdministratorWithRolesAndAudit()
    {
        using var factory = CreateFactory();
        var actorId = await SeedActiveAdminAsync(factory, [DoSelectRoles.SuperAdmin]);
        using var client = CreateAdminClient(factory, actorId);
        var marker = Guid.NewGuid().ToString("N");

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            BasePath,
            new
            {
                email = $"new-{marker}@example.invalid",
                displayName = "新管理員",
                employeeCode = $"EMP-{marker}",
                roles = new[] { DoSelectRoles.OrderManager },
                confirmSuperAdmin = false,
            },
            DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var created = await db.Users.SingleAsync(row => row.Email == $"new-{marker}@example.invalid");
        Assert.Equal(AccountStatus.PendingEmailVerification, created.AccountStatus);
        Assert.False(created.EmailConfirmed);
        Assert.True(await db.AdminProfiles.AnyAsync(row => row.UserId == created.Id && row.EmployeeCode == $"EMP-{marker}"));
        Assert.True(await db.AuditLogs.AnyAsync(row =>
            row.ResourcePublicId == created.PublicId && row.Action == AuditActions.AdminAccountCreate));
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal([DoSelectRoles.OrderManager], await userManager.GetRolesAsync(created));
    }

    [Fact]
    public async Task InvitationCanBeAcceptedOnlyOnceAndActivatesTheAdministrator()
    {
        using var factory = CreateFactory();
        var marker = Guid.NewGuid().ToString("N");
        string token;
        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            await EnsureRoleAsync(roleManager, DoSelectRoles.OrderManager);
            var now = DateTime.UtcNow;
            var user = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"invite-{marker}@example.invalid", now);
            Assert.True((await userManager.CreateAsync(user)).Succeeded);
            db.AdminProfiles.Add(new AdminProfile(user.Id, user.PublicId, $"INV-{marker}", "受邀管理員", now));
            await db.SaveChangesAsync();
            Assert.True((await userManager.AddToRoleAsync(user, DoSelectRoles.OrderManager)).Succeeded);
            token = await userManager.GeneratePasswordResetTokenAsync(user);
            publicId = user.PublicId;
        }

        using var client = factory.CreateClient();
        var body = new { publicId, token, newPassword = "Valid-Admin-Passw0rd!" };
        using var accepted = await client.PostAsJsonWithAntiforgeryAsync(
            "/api/v1/admin/auth/invitations/accept", body, DoSelectClaimValues.Admin);
        using var replay = await client.PostAsJsonWithAntiforgeryAsync(
            "/api/v1/admin/auth/invitations/accept", body, DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationManager = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var activated = await verificationManager.Users.SingleAsync(row => row.PublicId == publicId);
        Assert.Equal(AccountStatus.Active, activated.AccountStatus);
        Assert.True(activated.EmailConfirmed);
        Assert.True(await verificationManager.CheckPasswordAsync(activated, "Valid-Admin-Passw0rd!"));
    }

    [Fact]
    public async Task RoleChangeRotatesTheSecurityStampAndSelfDemotionIsRejected()
    {
        using var factory = CreateFactory();
        var actorId = await SeedActiveAdminAsync(factory, [DoSelectRoles.SuperAdmin]);
        var targetId = await SeedActiveAdminAsync(factory, [DoSelectRoles.OrderManager]);
        Guid targetPublicId;
        string rowVersion;
        string oldStamp;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await EnsureRoleAsync(roleManager, DoSelectRoles.FinanceManager);
            var target = await db.Users.SingleAsync(row => row.Id == targetId);
            targetPublicId = target.PublicId;
            rowVersion = Convert.ToBase64String(target.RowVersion);
            oldStamp = target.SecurityStamp!;
        }
        using var client = CreateAdminClient(factory, actorId);

        using var updated = await client.PutAsJsonWithAntiforgeryAsync(
            $"{BasePath}/{targetPublicId:D}/roles",
            new
            {
                roles = new[] { DoSelectRoles.FinanceManager },
                rowVersion,
                reasonCode = "job_change",
                confirmSuperAdmin = false,
            },
            DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var target = await manager.FindByIdAsync(targetId) ?? throw new InvalidOperationException();
            Assert.NotEqual(oldStamp, target.SecurityStamp);
            Assert.Equal([DoSelectRoles.FinanceManager], await manager.GetRolesAsync(target));
        }

        Guid actorPublicId;
        string actorRowVersion;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            var actor = await db.Users.SingleAsync(row => row.Id == actorId);
            actorPublicId = actor.PublicId;
            actorRowVersion = Convert.ToBase64String(actor.RowVersion);
        }
        using var rejected = await client.PutAsJsonWithAntiforgeryAsync(
            $"{BasePath}/{actorPublicId:D}/roles",
            new
            {
                roles = new[] { DoSelectRoles.OrderManager },
                rowVersion = actorRowVersion,
                reasonCode = "job_change",
                confirmSuperAdmin = false,
            },
            DoSelectClaimValues.Admin);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("self_demotion_forbidden", json.RootElement.GetProperty("code").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            TestAuthHandler.Configure(services);
        }));

    private static HttpClient CreateAdminClient(WebApplicationFactory<Program> factory, string actorId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, actorId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, DoSelectRoles.SuperAdmin);
        return client;
    }

    private static async Task<string> SeedActiveAdminAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyCollection<string> roles)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        foreach (var role in roles) await EnsureRoleAsync(roleManager, role);
        var marker = Guid.NewGuid().ToString("N");
        var user = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"actor-{marker}@example.invalid", DateTime.UtcNow);
        user.ConfirmEmail(DateTime.UtcNow);
        Assert.True((await userManager.CreateAsync(user, "Valid-Admin-Passw0rd!")).Succeeded);
        db.AdminProfiles.Add(new AdminProfile(user.Id, user.PublicId, $"ACT-{marker}", "測試管理員", DateTime.UtcNow));
        await db.SaveChangesAsync();
        Assert.True((await userManager.AddToRolesAsync(user, roles)).Succeeded);
        return user.Id;
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole> manager, string role)
    {
        if (!await manager.RoleExistsAsync(role))
            Assert.True((await manager.CreateAsync(new IdentityRole(role))).Succeeded);
    }
}

internal static class AdminAccountAntiforgeryExtensions
{
    public static async Task<HttpResponseMessage> PutAsJsonWithAntiforgeryAsync<T>(
        this HttpClient client,
        string requestUri,
        T value,
        string clientType)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, clientType);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, requestUri) { Content = JsonContent.Create(value) };
        request.Headers.Add("X-XSRF-TOKEN", tokenDocument.RootElement.GetProperty("requestToken").GetString());
        if (tokenResponse.Headers.TryGetValues("Set-Cookie", out var values))
        {
            request.Headers.Add("Cookie", values.Select(item => item.Split(';', 2)[0])
                .Single(item => item.StartsWith(".DoSelect.Antiforgery=", StringComparison.Ordinal)));
        }
        return await client.SendAsync(request);
    }
}
