using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

public sealed class AiSupportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/api/v1/ai/support/messages";
    private const string MemberId = "11111111-1111-1111-1111-111111111111";
    private static readonly DateTimeOffset ResetAtUtc =
        new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> _factory;

    public AiSupportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousRequest_Returns401WithoutCallingModel()
    {
        var model = new RecordingModelClient();
        using var factory = CreateFactory(GrantedAccess(), model);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task GuestOrderScope_Returns403WithoutCallingModel()
    {
        var model = new RecordingModelClient();
        using var factory = CreateFactory(GrantedAccess(), model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "guest-order");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task MemberWithoutConsent_Returns409ConsentRequiredWithoutCallingModel()
    {
        var model = new RecordingModelClient();
        using var factory = CreateFactory(
            new AiSupportAccessState(AiConsentState.Missing, 20, ResetAtUtc),
            model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ai_consent_required", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task MemberWithExhaustedQuota_Returns429WithoutCallingModel()
    {
        var model = new RecordingModelClient();
        using var factory = CreateFactory(
            new AiSupportAccessState(AiConsentState.Granted, 0, ResetAtUtc),
            model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("ai_usage_limit_exceeded", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task SensitiveMessage_ReturnsSafeValidationProblemWithoutCallingModelOrEchoingContent()
    {
        const string secret = "access_token: [[SYNTHETIC_ACCESS_TOKEN]]";
        var model = new RecordingModelClient();
        using var factory = CreateFactory(GrantedAccess(), model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(secret), token);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task WhitespaceOnlyMessage_ReturnsValidationProblemWithoutCallingModel()
    {
        var model = new RecordingModelClient();
        using var factory = CreateFactory(GrantedAccess(), model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest("   "), token);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task AuthorizedSafeRequest_ReturnsContractAndCallsModelOnce()
    {
        var model = new RecordingModelClient("請至訂單頁提出退貨申請。");
        using var factory = CreateFactory(GrantedAccess(), model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, model.CallCount);
        Assert.Equal("answered", document.RootElement.GetProperty("resultCode").GetString());
        Assert.Equal("none", document.RootElement.GetProperty("degradationMode").GetString());
        Assert.Equal(19, document.RootElement.GetProperty("usage").GetProperty("remainingRequests").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("citations").GetArrayLength());
    }

    private WebApplicationFactory<Program> CreateFactory(
        AiSupportAccessState accessState,
        RecordingModelClient model) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(AiSupportTestSignInController).Assembly);
                services.RemoveAll<IAiSupportAccessReader>();
                services.RemoveAll<IAiSupportModelClient>();
                services.AddSingleton<IAiSupportAccessReader>(new StubAccessReader(accessState));
                services.AddSingleton<IAiSupportModelClient>(model);
            });
        });

    private static AiSupportAccessState GrantedAccess() =>
        new(AiConsentState.Granted, 20, ResetAtUtc);

    private static object ValidRequest(string message = "請說明退貨流程") => new
    {
        conversationPublicId = (Guid?)null,
        message,
        referencedOrderPublicIds = Array.Empty<Guid>(),
        locale = "zh-TW",
    };

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        object body,
        string antiforgeryToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        return await client.SendAsync(request);
    }

    private static async Task SignInAsync(HttpClient client, string scope)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/__tests/ai/sign-in/{scope}")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, "member");
        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);
        response.EnsureSuccessStatusCode();
        return document.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class StubAccessReader : IAiSupportAccessReader
    {
        private readonly AiSupportAccessState _state;

        public StubAccessReader(AiSupportAccessState state)
        {
            _state = state;
        }

        public Task<AiSupportAccessState> ReadAsync(
            Guid memberId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_state);
    }

    private sealed class RecordingModelClient : IAiSupportModelClient
    {
        private readonly string _answer;

        public RecordingModelClient(string answer = "unused")
        {
            _answer = answer;
        }

        public int CallCount { get; private set; }

        public Task<AiSupportModelAnswer> GenerateAsync(
            AiPromptEnvelope envelope,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new AiSupportModelAnswer(_answer));
        }
    }
}

[ApiController]
[Route("__tests/ai")]
public sealed class AiSupportTestSignInController : ControllerBase
{
    [HttpPost("sign-in/{scope}")]
    public async Task<IActionResult> SignIn(string scope)
    {
        var accountType = scope == "member" ? DoSelectClaimValues.Member : "guest_order";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, AiSupportEndpointTestsMemberId.Value),
            new Claim(DoSelectClaimTypes.AccountType, accountType),
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, DoSelectAuthenticationSchemes.Member));

        await HttpContext.SignInAsync(DoSelectAuthenticationSchemes.Member, principal);
        return NoContent();
    }
}

internal static class AiSupportEndpointTestsMemberId
{
    public const string Value = "11111111-1111-1111-1111-111111111111";
}
