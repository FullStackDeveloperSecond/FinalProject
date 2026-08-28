using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.IntegrationTests.Catalog;
using DoSelect.Api.Security;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Outbox;

[Collection(nameof(CatalogAdminApiCollection))]
public sealed class AdminOutboxMessagesApiTests(CatalogAdminApiFixture fixture)
{
    [Fact]
    public async Task Retry_WhenFailedAndSuperAdmin_RequeuesAndWritesAuditAtomically()
    {
        var (adminId, messagePublicId, payload, attemptCount) = await SeedFailedMessageAsync();
        using var client = await SignInAsync(adminId, DoSelectRoles.SuperAdmin);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/outbox-messages/{messagePublicId:D}/actions/retry")
        {
            Content = JsonContent.Create(new { reasonCode = "operator_verified_delivery_restored" }),
        };

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        await using var context = fixture.CreateScopedContext();
        var message = await context.OutboxMessages.SingleAsync(item => item.PublicId == messagePublicId);
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(payload, message.PayloadJson);
        Assert.Equal(attemptCount, message.AttemptCount);
        Assert.Null(message.LastErrorCode);
        var audit = await context.AuditLogs.SingleAsync(item =>
            item.Action == "outbox.retry" && item.ResourcePublicId == messagePublicId);
        Assert.Equal(AuditResult.Success, audit.Result);
        Assert.Equal("operator_verified_delivery_restored", audit.Reason);
    }

    [Fact]
    public async Task Retry_RequiresMfaSuperAdminAndFailedState()
    {
        var (adminId, messagePublicId, _, _) = await SeedFailedMessageAsync();
        using var nonSuperAdmin = await SignInAsync(adminId, DoSelectRoles.OrderManager);
        using var forbiddenRequest = CreateRetryRequest(messagePublicId);
        using var forbidden = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            nonSuperAdmin,
            forbiddenRequest);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var noMfaSuperAdmin = await SignInAsync(
            adminId,
            DoSelectRoles.SuperAdmin,
            includeMfa: false);
        using var noMfaRequest = CreateRetryRequest(messagePublicId);
        using var noMfa = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            noMfaSuperAdmin,
            noMfaRequest);
        Assert.Equal(HttpStatusCode.Forbidden, noMfa.StatusCode);

        using var superAdmin = await SignInAsync(adminId, DoSelectRoles.SuperAdmin);
        using var firstRequest = CreateRetryRequest(messagePublicId);
        using var first = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(superAdmin, firstRequest);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var secondRequest = CreateRetryRequest(messagePublicId);
        using var second = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(superAdmin, secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("outbox_message_not_retryable", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Retry_WhenReasonViolatesAuditPolicy_ReturnsValidationWithoutMutation()
    {
        var (adminId, messagePublicId, _, _) = await SeedFailedMessageAsync();
        using var superAdmin = await SignInAsync(adminId, DoSelectRoles.SuperAdmin);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/outbox-messages/{messagePublicId:D}/actions/retry")
        {
            Content = JsonContent.Create(new { reasonCode = "operator_recovery_code" }),
        };

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(
            superAdmin,
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        await using var context = fixture.CreateScopedContext();
        Assert.Equal(
            OutboxMessageStatus.Failed,
            (await context.OutboxMessages.SingleAsync(item =>
                item.PublicId == messagePublicId)).Status);
        Assert.False(await context.AuditLogs.AnyAsync(item =>
            item.Action == "outbox.retry" && item.ResourcePublicId == messagePublicId));
    }

    private async Task<(string AdminId, Guid MessagePublicId, string Payload, int AttemptCount)>
        SeedFailedMessageAsync()
    {
        await using var context = fixture.CreateScopedContext();
        var now = DateTime.UtcNow;
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(),
            $"outbox-{Guid.NewGuid():N}@example.test",
            now);
        context.Users.Add(admin);
        var writer = new EfOutboxWriter(context, TimeProvider.System);
        var message = writer.Add(OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            "Order",
            Guid.CreateVersion7(),
            new EmailNotificationRequestedV1(
                Guid.CreateVersion7(),
                "order.created",
                "order.customer",
                "Order",
                Guid.CreateVersion7(),
                "zh-TW",
                1),
            now,
            now,
            $"outbox-api-{Guid.NewGuid():N}"[..40]));
        message.Claim(now, now.AddMinutes(1));
        message.Fail("provider_permanent_failure");
        await context.SaveChangesAsync();
        return (admin.Id, message.PublicId, message.PayloadJson, message.AttemptCount);
    }

    private async Task<HttpClient> SignInAsync(
        string adminId,
        string role,
        bool includeMfa = true)
    {
        var client = fixture.CreateClient();
        var token = await CatalogAdminApiFixture.GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new
            {
                includeMfa,
                roles = new[] { role },
                userId = adminId,
            }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static HttpRequestMessage CreateRetryRequest(Guid publicId) => new(
        HttpMethod.Post,
        $"/api/v1/admin/outbox-messages/{publicId:D}/actions/retry")
    {
        Content = JsonContent.Create(new { reasonCode = "operator_verified_delivery_restored" }),
    };
}
