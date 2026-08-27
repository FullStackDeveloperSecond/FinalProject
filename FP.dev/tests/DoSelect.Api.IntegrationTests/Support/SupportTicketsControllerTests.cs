using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// End-to-end tests against the real local DoSelectDb via the full ASP.NET Core pipeline
/// (Controller -> Application -> Infrastructure -> SQL Server). Two persistent test members
/// (recognizable by email, upserted idempotently) stand in for real accounts since
/// MemberUserId carries a real FK to AspNetUsers.
/// </summary>
public sealed class SupportTicketsControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string MemberAEmail = "kafen-test-member-a@doselect.local";
    private const string MemberBEmail = "kafen-test-member-b@doselect.local";

    private readonly WebApplicationFactory<Program> _factory;
    private string _memberAId = string.Empty;
    private string _memberBId = string.Empty;

    public SupportTicketsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                TestAuthHandler.Configure(services);
            });
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        _memberAId = await EnsureMemberAsync(dbContext, MemberAEmail);
        _memberBId = await EnsureMemberAsync(dbContext, MemberBEmail);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_WhenAnonymous_Returns401WithAuthenticationRequiredCode()
    {
        using var client = CreateClient(memberUserId: null);

        using var response = await client.GetAsync("/api/v1/support-tickets");
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AuthenticationRequired, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateThenGet_WhenAuthenticated_RoundTripsTicketWithComputedSla()
    {
        using var client = CreateClient(_memberAId);

        using var createResponse = await client.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "order",
            subject = "訂單延遲問題",
            message = "我的包裹已經超過預計送達時間三天了",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("open", created.RootElement.GetProperty("status").GetString());
        Assert.Equal("normal", created.RootElement.GetProperty("priority").GetString());
        var publicId = created.RootElement.GetProperty("publicId").GetString();

        using var getResponse = await client.GetAsync($"/api/v1/support-tickets/{publicId}");
        var fetched = await ReadJsonAsync(getResponse);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(publicId, fetched.RootElement.GetProperty("publicId").GetString());
        Assert.Single(fetched.RootElement.GetProperty("messages").EnumerateArray());
    }

    [Fact]
    public async Task GetDetail_WhenTicketBelongsToAnotherMember_Returns404NotForbidden()
    {
        using var ownerClient = CreateClient(_memberAId);
        using var createResponse = await ownerClient.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "account",
            subject = "帳號登入問題",
            message = "我無法登入我的帳號，一直顯示密碼錯誤",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetString();

        using var otherClient = CreateClient(_memberBId);
        using var response = await otherClient.GetAsync($"/api/v1/support-tickets/{publicId}");
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ResourceNotFound, document.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// DES-23 regression: an internal note added through the admin-only
    /// IAdminSupportTicketStore.AddInternalNoteAsync vertical slice must never reach the
    /// member-facing GetDetail response — neither the note's text nor any internal-only
    /// indicator. ListPublicMessagesAsync's `!m.IsInternal` filter is what enforces this; this
    /// test proves it end-to-end against the new action, not just the filter in isolation.
    /// </summary>
    [Fact]
    public async Task GetDetail_AsMember_NeverExposesAnInternalNoteAddedByAdmin()
    {
        using var client = CreateClient(_memberAId);
        using var createResponse = await client.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = $"Internal note regression {Guid.NewGuid():N}",
            message = "公開訊息內容",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetGuid();
        var rowVersion = Convert.FromBase64String(created.RootElement.GetProperty("rowVersion").GetString()!);

        const string secretNoteText = "INTERNAL-ONLY-SECRET-4f2ac9";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            var now = DateTime.UtcNow;
            var admin = ApplicationUser.CreateAdmin(Guid.NewGuid(), $"internal-note-admin-{Guid.NewGuid():N}@example.test", now);
            admin.ConfirmEmail(now.AddMilliseconds(1));
            db.Users.Add(admin);
            db.AdminProfiles.Add(new AdminProfile(admin.Id, Guid.NewGuid(), $"EMP-{Guid.NewGuid():N}", "Note Admin", now));
            await db.SaveChangesAsync();

            var store = scope.ServiceProvider.GetRequiredService<DoSelect.Application.Support.Admin.IAdminSupportTicketStore>();
            var result = await store.AddInternalNoteAsync(
                new DoSelect.Application.Support.Admin.SupportTicketAddInternalNoteCommand(
                    publicId, admin.Id, ["CustomerService"], CanSupervise: false, rowVersion, now,
                    "corr", "0123456789abcdef0123456789abcdef", null, secretNoteText),
                CancellationToken.None);
            Assert.Equal(DoSelect.Application.Support.Admin.SupportTicketMutationOutcome.Success, result.Outcome);
        }

        using var response = await client.GetAsync($"/api/v1/support-tickets/{publicId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(secretNoteText, body, StringComparison.Ordinal);
        Assert.DoesNotContain("isInternal", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        Assert.Single(document.RootElement.GetProperty("messages").EnumerateArray());
    }

    [Fact]
    public async Task List_WhenTicketBelongsToActorA_DoesNotExposeItToActorB()
    {
        using var actorAClient = CreateClient(_memberAId);
        using var createResponse = await actorAClient.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = $"Actor A list isolation {Guid.NewGuid():N}",
            message = "這個案件只能出現在建立者自己的列表中",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetGuid();

        using var actorAListResponse = await actorAClient.GetAsync("/api/v1/support-tickets?pageSize=100");
        using var actorAList = await ReadJsonAsync(actorAListResponse);
        using var actorBClient = CreateClient(_memberBId);
        using var actorBListResponse = await actorBClient.GetAsync("/api/v1/support-tickets?pageSize=100");
        using var actorBList = await ReadJsonAsync(actorBListResponse);

        Assert.Equal(HttpStatusCode.OK, actorAListResponse.StatusCode);
        Assert.Contains(actorAList.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("publicId").GetGuid() == publicId);
        Assert.Equal(HttpStatusCode.OK, actorBListResponse.StatusCode);
        Assert.DoesNotContain(actorBList.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("publicId").GetGuid() == publicId);
    }

    [Fact]
    public async Task AddMessage_WhenActorBUsesActorATicketId_Returns404WithoutSideEffects()
    {
        using var actorAClient = CreateClient(_memberAId);
        using var createResponse = await actorAClient.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = "Actor B 不可追加訊息",
            message = "原始訊息必須保持不變",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetGuid();
        var rowVersion = created.RootElement.GetProperty("rowVersion").GetString();

        using var actorBClient = CreateClient(_memberBId);
        using var response = await actorBClient.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/messages",
            new { body = "越權追加的訊息", rowVersion },
            DoSelectClaimValues.Member);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ResourceNotFound, document.RootElement.GetProperty("code").GetString());
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await dbContext.SupportTickets.AsNoTracking().SingleAsync(candidate => candidate.PublicId == publicId);
        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Equal(Convert.FromBase64String(rowVersion!), ticket.RowVersion);
        Assert.Equal(1, await dbContext.SupportMessages.CountAsync(message => message.SupportTicketId == ticket.Id));
    }

    [Fact]
    public async Task Cancel_WhenActorBUsesActorATicketId_Returns404WithoutSideEffects()
    {
        using var actorAClient = CreateClient(_memberAId);
        using var createResponse = await actorAClient.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = "Actor B 不可取消案件",
            message = "案件狀態與歷程必須保持不變",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetGuid();
        var rowVersion = created.RootElement.GetProperty("rowVersion").GetString();
        long ticketId;
        int statusHistoryCountBefore;
        using (var beforeScope = _factory.Services.CreateScope())
        {
            var beforeContext = beforeScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            ticketId = await beforeContext.SupportTickets
                .Where(candidate => candidate.PublicId == publicId)
                .Select(candidate => candidate.Id)
                .SingleAsync();
            statusHistoryCountBefore = await beforeContext.SupportStatusHistories
                .CountAsync(history => history.SupportTicketId == ticketId);
        }

        using var actorBClient = CreateClient(_memberBId);
        using var response = await actorBClient.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/actions/cancel",
            new { reasonCode = "unauthorized-attempt", rowVersion },
            DoSelectClaimValues.Member);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ResourceNotFound, document.RootElement.GetProperty("code").GetString());
        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await verifyContext.SupportTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == ticketId);
        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Equal(Convert.FromBase64String(rowVersion!), ticket.RowVersion);
        Assert.Equal(statusHistoryCountBefore,
            await verifyContext.SupportStatusHistories.CountAsync(history => history.SupportTicketId == ticketId));
    }
    [Fact]
    public async Task Cancel_WhenOpenWithNoReply_TransitionsToCancelled()
    {
        using var client = CreateClient(_memberAId);
        using var createResponse = await client.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = "測試取消案件",
            message = "這是一個要被取消的測試案件內容",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetString();
        var rowVersion = created.RootElement.GetProperty("rowVersion").GetString();

        using var cancelResponse = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/actions/cancel",
            new { reasonCode = "no-longer-needed", rowVersion },
            DoSelectClaimValues.Member);
        var cancelled = await ReadJsonAsync(cancelResponse);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal("cancelled", cancelled.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AddMessage_WhenRowVersionIsStale_Returns409ConcurrencyConflict()
    {
        using var client = CreateClient(_memberAId);
        using var createResponse = await client.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = "測試併發案件",
            message = "這是一個要測試併發衝突的測試案件內容",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetString();
        var staleRowVersion = created.RootElement.GetProperty("rowVersion").GetString();

        // First write with the real (current) RowVersion succeeds and advances it.
        using var firstResponse = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/messages",
            new { body = "第一則追加訊息", rowVersion = staleRowVersion },
            DoSelectClaimValues.Member);

        // Second write reuses the now-stale RowVersion captured before the first write.
        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/messages",
            new { body = "第二則追加訊息", rowVersion = staleRowVersion },
            DoSelectClaimValues.Member);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("concurrency_conflict", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddMessage_WhenRowVersionIsOmitted_Returns400ValidationFailedNotConflict()
    {
        using var client = CreateClient(_memberAId);
        using var createResponse = await client.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = "RowVersion 缺漏測試",
            message = "測試訊息缺少 RowVersion 欄位",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetString();

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/messages",
            new { body = "缺少 RowVersion 的訊息" },
            DoSelectClaimValues.Member);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cancel_WhenRowVersionIsWrongLength_Returns400ValidationFailedNotConflict()
    {
        using var client = CreateClient(_memberAId);
        using var createResponse = await client.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = "RowVersion 長度測試",
            message = "測試取消時 RowVersion 長度錯誤",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetString();

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/actions/cancel",
            new { reasonCode = "no-longer-needed", rowVersion = Convert.ToBase64String([1, 2, 3, 4]) },
            DoSelectClaimValues.Member);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddMessage_WhenRowVersionIsNotValidBase64_Returns400ValidationFailed()
    {
        using var client = CreateClient(_memberAId);
        using var createResponse = await client.PostAsJsonWithAntiforgeryAsync("/api/v1/support-tickets", new
        {
            category = "other",
            subject = "RowVersion Base64 測試",
            message = "測試 RowVersion 不是合法 Base64",
        }, DoSelectClaimValues.Member);
        var created = await ReadCreatedJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetString();

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/support-tickets/{publicId}/messages",
            new { body = "非法 Base64 的訊息", rowVersion = "not-valid-base64!!" },
            DoSelectClaimValues.Member);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, document.RootElement.GetProperty("code").GetString());
    }

    private HttpClient CreateClient(string? memberUserId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        if (memberUserId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, memberUserId);
        }

        return client;
    }

    private static async Task<string> EnsureMemberAsync(DoSelectDbContext dbContext, string email)
    {
        var existing = await dbContext.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            return existing.Id;
        }

        var user = ApplicationUser.CreateMember(Guid.NewGuid(), email, DateTime.UtcNow);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private static async Task<JsonDocument> ReadCreatedJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await ReadJsonAsync(response);
    }
}
