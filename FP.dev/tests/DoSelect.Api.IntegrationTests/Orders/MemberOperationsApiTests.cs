using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Orders;

[Collection(nameof(AdminOrdersApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class MemberOperationsApiTests(AdminOrdersApiFixture fixture)
{
    [Fact]
    public async Task Members_RequireAuthorizedRole_AndMaskEmail()
    {
        await using var db = fixture.CreateScopedContext();
        var (user, profile) = await SeedMemberAsync(db);
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/members")).StatusCode);
        using var wrongRole = await fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongRole.GetAsync("/api/v1/admin/members")).StatusCode);
        using var allowed = await fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.PrivacyAdmin);
        using var response = await allowed.GetAsync($"/api/v1/admin/members/{profile.PublicId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(user.Email!, body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("emailMasked", body);
    }

    [Fact]
    public async Task Suspend_RotatesCredentials_RecordsAudit_RejectsStaleWrite()
    {
        await using var db = fixture.CreateScopedContext();
        var (user, profile) = await SeedMemberAsync(db);
        var oldStamp = user.SecurityStamp;
        var oldVersion = user.RowVersion.ToArray();
        var admin = await AdminOrdersApiSeeding.SeedAdminUserAsync(db);
        using var client = await fixture.CreateAuthenticatedAdminClientForUserAsync(admin, DoSelectRoles.SuperAdmin);
        async Task<HttpResponseMessage> Send(bool active, byte[] version) => await AdminOrdersApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/members/{profile.PublicId}/status")
            { Content = JsonContent.Create(new { active, rowVersion = version, reasonCode = "user_request" }) });
        using var suspended = await Send(false, oldVersion);
        Assert.Equal(HttpStatusCode.NoContent, suspended.StatusCode);
        await db.Entry(user).ReloadAsync();
        Assert.Equal(AccountStatus.Suspended, user.AccountStatus);
        Assert.NotEqual(oldStamp, user.SecurityStamp);
        Assert.True(await db.AuditLogs.AnyAsync(row => row.ResourcePublicId == profile.PublicId && row.Action == AuditActions.MemberSuspend));
        using var stale = await Send(true, oldVersion);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await db.Entry(user).ReloadAsync();
        Assert.Equal(AccountStatus.Suspended, user.AccountStatus);
        using var restored = await Send(true, user.RowVersion);
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
    }

    [Fact]
    public async Task MemberStatus_RejectsPrivacyAdmin_NoCsrf_AndUnverifiedActivation()
    {
        await using var db = fixture.CreateScopedContext();
        var (user, profile) = await SeedMemberAsync(db, verified: false);
        var admin = await AdminOrdersApiSeeding.SeedAdminUserAsync(db);
        using var viewer = await fixture.CreateAuthenticatedAdminClientForUserAsync(admin, DoSelectRoles.PrivacyAdmin);
        var path = $"/api/v1/admin/members/{profile.PublicId}/status";
        object body = new { active = true, rowVersion = user.RowVersion, reasonCode = "resolved" };
        using var denied = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(viewer, new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var manager = await fixture.CreateAuthenticatedAdminClientForUserAsync(admin, DoSelectRoles.SuperAdmin);
        using var noCsrf = await manager.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var unverified = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(manager, new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) });
        Assert.Equal(HttpStatusCode.Conflict, unverified.StatusCode);
        await db.Entry(user).ReloadAsync();
        Assert.Equal(AccountStatus.PendingEmailVerification, user.AccountStatus);
    }

    [Fact]
    public async Task Assembly_RequiresEachComputerToPass_RejectsSkippingAndStaleVersion()
    {
        await using var db = fixture.CreateScopedContext();
        var shippingProfile = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(db);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(db, shippingProfile, assemblyStatus: AssemblyStatus.Pending);
        var first = await AdminOrdersApiSeeding.SeedAssemblyJobAsync(db, order.Id);
        var second = await AdminOrdersApiSeeding.SeedAssemblyJobAsync(db, order.Id);
        var admin = await AdminOrdersApiSeeding.SeedAdminUserAsync(db);
        using var client = await fixture.CreateAuthenticatedAdminClientForUserAsync(admin);
        using var start = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/startProcessing") { Content = JsonContent.Create(new { rowVersion = order.RowVersion }) });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        async Task<HttpResponseMessage> Advance(string action, DoSelect.Domain.Orders.AssemblyJob job, byte[]? orderVersion = null)
        {
            await db.Entry(order).ReloadAsync(); await db.Entry(job).ReloadAsync();
            return await AdminOrdersApiFixture.SendWithAntiforgeryAsync(client,
                new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/{action}")
                { Content = JsonContent.Create(new { rowVersion = orderVersion ?? order.RowVersion, assemblyJobPublicId = job.PublicId, assemblyJobRowVersion = job.RowVersion }) });
        }
        using var skipped = await Advance("assemblyReady", first);
        Assert.Equal(HttpStatusCode.Conflict, skipped.StatusCode);
        var previousVersion = order.RowVersion.ToArray();
        using var testing = await Advance("assemblyTesting", first);
        Assert.Equal(HttpStatusCode.OK, testing.StatusCode);
        using var stale = await Advance("assemblyReady", first, previousVersion);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var readyOne = await Advance("assemblyReady", first);
        Assert.Equal(HttpStatusCode.OK, readyOne.StatusCode);
        Assert.NotEqual("ReadyToShip", (await readyOne.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assemblyStatus").GetString());
        using var testingTwo = await Advance("assemblyTesting", second);
        Assert.Equal(HttpStatusCode.OK, testingTwo.StatusCode);
        using var readyTwo = await Advance("assemblyReady", second);
        Assert.Equal(HttpStatusCode.OK, readyTwo.StatusCode);
        Assert.Equal("ReadyToShip", (await readyTwo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assemblyStatus").GetString());
        Assert.Equal(4, await db.AuditLogs.CountAsync(row => row.ResourcePublicId == order.PublicId && row.Action == AuditActions.OrderAssemblyProgress));
    }

    private static async Task<(ApplicationUser, MemberProfile)> SeedMemberAsync(DoSelect.Infrastructure.Persistence.DoSelectDbContext db, bool verified = true)
    {
        var now = DateTime.UtcNow;
        var user = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"member-{Guid.NewGuid():N}@example.invalid", now);
        if (verified) user.ConfirmEmail(now);
        db.Users.Add(user);
        var profile = new MemberProfile(user.Id, Guid.CreateVersion7(), "測試會員", null, now);
        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();
        return (user, profile);
    }
}
