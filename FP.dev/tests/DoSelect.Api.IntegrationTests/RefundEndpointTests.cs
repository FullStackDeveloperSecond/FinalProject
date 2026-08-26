using System.Net;
using System.Net.Http.Json;
using DoSelect.Api.Refunds;
using DoSelect.Api.Security;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests;

public sealed class RefundEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid RefundPublicId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string ExecuteRoute =
        "/api/v1/admin/refunds/cccccccc-cccc-cccc-cccc-cccccccccccc/actions/execute";

    private readonly WebApplicationFactory<Program> _factory;

    public RefundEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task AnonymousCallerIsChallenged()
    {
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(400m)));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, signedIn: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAdminWithoutTheFinanceRoleIsForbidden()
    {
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(400m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.CatalogManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AFinanceManagerWithoutMultiFactorIsForbidden()
    {
        // 工程包要求退款執行需 TOTP 二次確認；該條件由 Policy 的 MFA 宣告保證。
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(400m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: false, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AMissingIdempotencyKeyIsRejectedBeforeAnyExecution()
    {
        var executor = new FakeRefundExecutor(Settled(400m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(executor.LastRequest);
    }

    [Fact]
    public async Task AnAuthorisedExecutionReturnsTheSettledAmount()
    {
        var executor = new FakeRefundExecutor(Settled(400m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefundExecutionResponse>();
        Assert.Equal(RefundPublicId, body!.RefundPublicId);
        Assert.Equal(400m, body.SettledAmount);
        Assert.False(body.Replayed);
        Assert.Equal(RefundPublicId, executor.LastRequest!.RefundPublicId);
        Assert.Equal("refund-1", executor.LastRequest.IdempotencyKey);
    }

    [Fact]
    public async Task AReplayReportsTheSameAmountAndIsFlagged()
    {
        using var factory = CreateFactory(new FakeRefundExecutor(ExecuteRefundResult.Replay(400m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefundExecutionResponse>();
        Assert.Equal(400m, body!.SettledAmount);
        Assert.True(body.Replayed);
    }

    [Theory]
    [InlineData(RefundErrorCodes.ResourceNotFound, HttpStatusCode.NotFound)]
    [InlineData(RefundErrorCodes.RefundStateConflict, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.RefundAmountExceeded, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.IdempotencyPayloadConflict, HttpStatusCode.Conflict)]
    public async Task DomainErrorCodesMapToTheDocumentedStatuses(
        string errorCode,
        HttpStatusCode expected)
    {
        using var factory = CreateFactory(
            new FakeRefundExecutor(ExecuteRefundResult.Failure(errorCode)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(expected, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains(errorCode, problem, StringComparison.Ordinal);
    }

    private static ExecuteRefundResult Settled(decimal amount) =>
        ExecuteRefundResult.Settled(
            amount,
            new RefundExecutionPlan(11L, amount, "finance-1", "refund-1"));

    private async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string? idempotencyKey = "refund-1",
        bool signedIn = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ExecuteRoute);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(RefundsController.IdempotencyKeyHeaderName, idempotencyKey);
        }

        if (signedIn)
        {
            request.Headers.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client, "admin"));
        }

        // Body 只帶理由與 RowVersion，沒有 allocations 也沒有金額（DEC-P287）。
        request.Content = JsonContent.Create(new
        {
            reasonCode = "customer_request",
            note = (string?)null,
            refundRowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        });

        return await client.SendAsync(request);
    }

    private WebApplicationFactory<Program> CreateFactory(IRefundExecutor executor) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.AddSingleton(executor);
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
            });
        });

    private static async Task SignInAsync(HttpClient client, bool includeMfa, params string[] roles)
    {
        var token = await GetAntiforgeryTokenAsync(client, "admin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa, roles }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string clientType)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, clientType);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("requestToken").GetString()!;
    }

    private sealed class FakeRefundExecutor(ExecuteRefundResult result) : IRefundExecutor
    {
        public ExecuteRefundRequest? LastRequest { get; private set; }

        public Task<ExecuteRefundResult> ExecuteAsync(
            ExecuteRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
