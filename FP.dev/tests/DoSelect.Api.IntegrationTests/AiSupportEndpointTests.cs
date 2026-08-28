using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.IntegrationTests;

public sealed class AiSupportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/api/v1/ai/support/messages";
    private static readonly Guid OrderPublicId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
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
    public async Task WrongAccountTypeInMemberScheme_Returns403WithoutCallingModel()
    {
        var model = new RecordingModelClient();
        using var factory = CreateFactory(GrantedAccess(), model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "wrong-account-type");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public void ProductionRegistration_UsesOpenAiResponsesClient()
    {
        using var scope = _factory.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IAiSupportModelClient>();

        Assert.IsType<OpenAiResponsesClient>(client);
    }

    [Fact]
    public async Task GuestOrderAccessCookie_Returns403WithoutReadingAccessOrCallingModel()
    {
        var model = new RecordingModelClient();
        var admission = new StubAdmissionGate(GrantedAccess());
        using var factory = CreateFactory(admission, model);
        using var client = factory.CreateClient();
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(DoSelectAuthenticationSchemes.GuestOrderAccess);
        var identity = new ClaimsIdentity(DoSelectAuthenticationSchemes.GuestOrderAccess);
        identity.AddClaim(new Claim(
            DoSelect.Application.Orders.GuestOrderAccessClaimTypes.TokenValue,
            "synthetic-guest-token"));
        var protectedTicket = cookieOptions.TicketDataFormat.Protect(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            DoSelectAuthenticationSchemes.GuestOrderAccess));
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, admission.ReadCount);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task DisabledFeature_Returns503WithoutReadingAccessOrCallingModel()
    {
        var model = new RecordingModelClient();
        var admission = new StubAdmissionGate(GrantedAccess());
        using var factory = CreateFactory(admission, model, aiEnabled: false);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AiServiceUnavailable, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, admission.ReadCount);
        Assert.Equal(0, admission.ReservationCount);
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
        Assert.Equal(ApiErrorCodes.AiConsentRequired, problem.RootElement.GetProperty("code").GetString());
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
        Assert.Equal(ApiErrorCodes.AiUsageLimitExceeded, problem.RootElement.GetProperty("code").GetString());
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
    public async Task UnownedReferencedOrder_Returns404WithoutReservingOrCallingModel()
    {
        var model = new RecordingModelClient();
        var admission = new StubAdmissionGate(GrantedAccess());
        var context = new StubContextReader(
            new AiSupportContextReadResult(
                AiSupportContextStatus.ResourceNotFound,
                DataItems: []));
        using var factory = CreateFactory(admission, model, context);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            ValidRequest(referencedOrderPublicIds: [OrderPublicId]),
            token);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AiOrderAccessDenied, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task AuthorizedSafeRequest_PropagatesLocaleAndOrderContextAndReservesOnce()
    {
        var model = new RecordingModelClient(
            "返品申請は注文ページから行えます。",
            citations:
            [
                new AiSupportCitation(
                    "order",
                    OrderPublicId.ToString("D"),
                    "ORD-TEST",
                    "2026-08-28T00:00:00.0000000Z"),
            ]);
        var admission = new StubAdmissionGate(GrantedAccess());
        var context = new StubContextReader(
            new AiSupportContextReadResult(
                AiSupportContextStatus.Allowed,
                [
                    new AiSupportContextItem(
                        "order",
                        OrderPublicId.ToString("D"),
                        "ORD-TEST",
                        "2026-08-28T00:00:00.0000000Z",
                        "owner-verified de-identified order context"),
                ]));
        using var factory = CreateFactory(admission, model, context);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            ValidRequest(locale: "ja-JP", referencedOrderPublicIds: [OrderPublicId]),
            token);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, admission.ReservationCount);
        Assert.Equal(1, model.CallCount);
        Assert.Equal(SupportedLocale.JaJp, model.LastEnvelope?.ResponseLocale);
        Assert.Equal([OrderPublicId], context.LastReferencedOrderPublicIds);
        Assert.Equal(
            "owner-verified de-identified order context",
            Assert.Single(model.LastEnvelope!.DataItems).Content);
        Assert.Equal("answered", document.RootElement.GetProperty("resultCode").GetString());
        var citation = Assert.Single(document.RootElement.GetProperty("citations").EnumerateArray());
        Assert.Equal("order", citation.GetProperty("type").GetString());
        Assert.Equal("ORD-TEST", citation.GetProperty("label").GetString());
        Assert.Equal(OrderPublicId, citation.GetProperty("resourcePublicId").GetGuid());
        Assert.Equal(JsonValueKind.Null, citation.GetProperty("url").ValueKind);
        Assert.Equal(19, document.RootElement.GetProperty("usage").GetProperty("remainingRequests").GetInt32());
    }

    private WebApplicationFactory<Program> CreateFactory(
        AiSupportAccessState accessState,
        RecordingModelClient model,
        StubContextReader? context = null,
        bool aiEnabled = true) =>
        CreateFactory(new StubAdmissionGate(accessState), model, context, aiEnabled);

    private WebApplicationFactory<Program> CreateFactory(
        StubAdmissionGate admission,
        RecordingModelClient model,
        StubContextReader? context = null,
        bool aiEnabled = true) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Features:AiEnabled"] = aiEnabled.ToString(),
                    ["OpenAI:ApiKey"] = aiEnabled ? "integration-test-placeholder" : null,
                    ["OpenAI:SupportModel"] = "integration-test-model",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(AiSupportTestSignInController).Assembly);
                services.RemoveAll<IAiSupportAdmissionGate>();
                services.RemoveAll<IAiSupportContextReader>();
                services.RemoveAll<IAiSupportModelClient>();
                services.AddSingleton<IAiSupportAdmissionGate>(admission);
                services.AddSingleton<IAiSupportContextReader>(
                    context ?? new StubContextReader(
                        new AiSupportContextReadResult(
                            AiSupportContextStatus.Allowed,
                            DataItems: [])));
                services.AddSingleton<IAiSupportModelClient>(model);
            });
        });

    private static AiSupportAccessState GrantedAccess() =>
        new(AiConsentState.Granted, 20, ResetAtUtc);

    private static object ValidRequest(
        string message = "請說明退貨流程",
        string locale = "zh-TW",
        Guid[]? referencedOrderPublicIds = null) => new
        {
            conversationPublicId = (Guid?)null,
            message,
            referencedOrderPublicIds = referencedOrderPublicIds ?? [],
            locale,
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

    private sealed class StubAdmissionGate : IAiSupportAdmissionGate
    {
        private readonly AiSupportAccessState _state;

        public StubAdmissionGate(AiSupportAccessState state)
        {
            _state = state;
        }

        public int ReadCount { get; private set; }

        public int ReservationCount { get; private set; }

        public Task<AiSupportAccessState> ReadAsync(
            Guid memberId,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(_state);
        }

        public Task<AiSupportReservationResult> TryReserveAsync(
            Guid memberId,
            Guid requestPublicId,
            CancellationToken cancellationToken)
        {
            ReservationCount++;
            var reserved = _state.ConsentState == AiConsentState.Granted &&
                _state.RemainingDailyMessages > 0;
            var state = reserved
                ? _state with { RemainingDailyMessages = _state.RemainingDailyMessages - 1 }
                : _state;
            return Task.FromResult(new AiSupportReservationResult(reserved, state));
        }
    }

    private sealed class StubContextReader : IAiSupportContextReader
    {
        private readonly AiSupportContextReadResult _result;

        public StubContextReader(AiSupportContextReadResult result)
        {
            _result = result;
        }

        public IReadOnlyList<Guid>? LastReferencedOrderPublicIds { get; private set; }

        public Task<AiSupportContextReadResult> ReadAsync(
            Guid memberId,
            IReadOnlyList<Guid> referencedOrderPublicIds,
            CancellationToken cancellationToken)
        {
            LastReferencedOrderPublicIds = referencedOrderPublicIds;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingModelClient : IAiSupportModelClient
    {
        private readonly string? _answer;
        private readonly AiSupportModelAnswerStatus _status;
        private readonly IReadOnlyList<AiSupportCitation> _citations;

        public RecordingModelClient(
            string? answer = "unused",
            AiSupportModelAnswerStatus status = AiSupportModelAnswerStatus.Answered,
            IReadOnlyList<AiSupportCitation>? citations = null)
        {
            _answer = answer;
            _status = status;
            _citations = citations ?? [];
        }

        public int CallCount { get; private set; }

        public AiPromptEnvelope? LastEnvelope { get; private set; }

        public Task<AiSupportModelAnswer> GenerateAsync(
            AiPromptEnvelope envelope,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastEnvelope = envelope;
            return Task.FromResult(new AiSupportModelAnswer(_answer, _status, _citations));
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
        var accountType = scope == "member" ? DoSelectClaimValues.Member : "wrong_account_type";
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
