using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
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
        var created = await ReadJsonAsync(createResponse);

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
        var created = await ReadJsonAsync(createResponse);
        var publicId = created.RootElement.GetProperty("publicId").GetString();

        using var otherClient = CreateClient(_memberBId);
        using var response = await otherClient.GetAsync($"/api/v1/support-tickets/{publicId}");
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ResourceNotFound, document.RootElement.GetProperty("code").GetString());
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
        var created = await ReadJsonAsync(createResponse);
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
        var created = await ReadJsonAsync(createResponse);
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
        var created = await ReadJsonAsync(createResponse);
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
        var created = await ReadJsonAsync(createResponse);
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
        var created = await ReadJsonAsync(createResponse);
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

    private static async Task<JsonDocument> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await ReadJsonAsync(response);
    }
}
