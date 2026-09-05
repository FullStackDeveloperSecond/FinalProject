using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using DoSelect.Domain.Ai;
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
    private static readonly Guid SupportTicketPublicId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
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
    public async Task NonDemoMemberWhenBudgetProtectionIsActive_Returns503WithoutCallingModel()
    {
        var model = new RecordingModelClient();
        using var factory = CreateFactory(
            new AiSupportAccessState(
                AiConsentState.Granted,
                20,
                ResetAtUtc,
                BudgetProtectionActive: true,
                IsDemoAllowlisted: false),
            model);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.AiBudgetProtectionActive,
            problem.RootElement.GetProperty("code").GetString());
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

    [Fact]
    public async Task AuthorizedRequest_PropagatesConversationAndSupportTicketReferences()
    {
        var conversationPublicId = Guid.NewGuid();
        var context = new StubContextReader(
            new AiSupportContextReadResult(AiSupportContextStatus.Allowed, DataItems: []));
        using var factory = CreateFactory(GrantedAccess(), new RecordingModelClient("回答"), context);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(
            client,
            ValidRequest(
                conversationPublicId: conversationPublicId,
                referencedSupportTicketPublicIds: [SupportTicketPublicId]),
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(conversationPublicId, context.LastConversationPublicId);
        Assert.Equal([SupportTicketPublicId], context.LastReferencedSupportTicketPublicIds);
    }

    [Fact]
    public async Task MemberCanReadGrantAndWithdrawCurrentConsent()
    {
        var consent = new StubConsentManager(new AiConsentSnapshot(
            AiConsentState.Missing,
            AiConsentPolicy.CurrentVersion,
            Locale: null,
            DecidedAtUtc: null));
        using var factory = CreateFactory(
            GrantedAccess(),
            new RecordingModelClient(),
            consent: consent);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");

        using var initial = await client.GetAsync("/api/v1/ai/consents/current");
        using var initialJson = await ReadJsonAsync(initial);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal("missing", initialJson.RootElement.GetProperty("state").GetString());

        var token = await GetAntiforgeryTokenAsync(client);
        using var grantRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/consents")
        {
            Content = JsonContent.Create(new
            {
                policyVersion = AiConsentPolicy.CurrentVersion,
                locale = "zh-TW",
                accepted = true,
            }),
        };
        grantRequest.Headers.Add("X-XSRF-TOKEN", token);
        using var granted = await client.SendAsync(grantRequest);
        using var grantedJson = await ReadJsonAsync(granted);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal("granted", grantedJson.RootElement.GetProperty("state").GetString());

        token = await GetAntiforgeryTokenAsync(client);
        using var withdrawRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/ai/consents/current");
        withdrawRequest.Headers.Add("X-XSRF-TOKEN", token);
        using var withdrawn = await client.SendAsync(withdrawRequest);
        using var withdrawnJson = await ReadJsonAsync(withdrawn);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal("denied", withdrawnJson.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task MemberCanReadOwnSupportUsageWithoutRawConversationContent()
    {
        var usage = new StubUsageReader(new AiMemberUsageSnapshot(
            3,
            20,
            ResetAtUtc.AddDays(-1),
            ResetAtUtc));
        using var factory = CreateFactory(
            GrantedAccess(),
            new RecordingModelClient(),
            usage: usage);
        using var client = factory.CreateClient();
        await SignInAsync(client, "member");

        using var response = await client.GetAsync("/api/v1/ai/usage/me");
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, document.RootElement.GetProperty("usedRequests").GetInt32());
        Assert.Equal(20, document.RootElement.GetProperty("requestLimit").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("inputTokens", out _));
        Assert.False(document.RootElement.TryGetProperty("outputTokens", out _));
        Assert.False(document.RootElement.TryGetProperty("estimatedCostUsd", out _));
        Assert.False(document.RootElement.TryGetProperty("budgetWarningActive", out _));
        Assert.False(document.RootElement.TryGetProperty("budgetProtectionActive", out _));
        Assert.False(document.RootElement.TryGetProperty("answer", out _));
    }

    [Theory]
    [InlineData(DoSelectRoles.MarketingAnalyst, false)]
    [InlineData(DoSelectRoles.FinanceManager, true)]
    public async Task AdminUsageReport_MasksCostUnlessRoleMayViewIt(
        string role,
        bool expectsCost)
    {
        using var factory = CreateFactory(GrantedAccess(), new RecordingModelClient());
        using var client = factory.CreateClient();
        await SignInAdminAsync(client, role);

        using var response = await client.GetAsync(
            "/api/v1/admin/ai/usage?fromUtc=2026-08-01T00:00:00Z&toUtc=2026-08-29T00:00:00Z");
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cumulativeCost = document.RootElement.GetProperty("cumulativeCostUsd");
        var rowCost = Assert.Single(document.RootElement.GetProperty("rows").EnumerateArray())
            .GetProperty("estimatedCostUsd");
        Assert.Equal(expectsCost ? JsonValueKind.Number : JsonValueKind.Null, cumulativeCost.ValueKind);
        Assert.Equal(expectsCost ? JsonValueKind.Number : JsonValueKind.Null, rowCost.ValueKind);
        Assert.True(document.RootElement.GetProperty("budgetWarningActive").GetBoolean());
    }

    private WebApplicationFactory<Program> CreateFactory(
        AiSupportAccessState accessState,
        RecordingModelClient model,
        StubContextReader? context = null,
        bool aiEnabled = true,
        StubConsentManager? consent = null,
        StubUsageReader? usage = null) =>
        CreateFactory(new StubAdmissionGate(accessState), model, context, aiEnabled, consent, usage);

    private WebApplicationFactory<Program> CreateFactory(
        StubAdmissionGate admission,
        RecordingModelClient model,
        StubContextReader? context = null,
        bool aiEnabled = true,
        StubConsentManager? consent = null,
        StubUsageReader? usage = null) =>
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
                    ["OpenAI:SupportInputCostPerMillionTokens"] = "0",
                    ["OpenAI:SupportOutputCostPerMillionTokens"] = "0",
                    ["OpenAI:ProductSearchModel"] = "integration-test-search-model",
                    ["OpenAI:ProductSearchTimeoutMilliseconds"] = "5000",
                    ["OpenAI:ProductSearchInputCostPerMillionTokens"] = "0",
                    ["OpenAI:ProductSearchOutputCostPerMillionTokens"] = "0",
                    ["OpenAI:AnonymousIdentityPepper"] = "integration-test-ai-anonymous-pepper-32-bytes",
                    ["OpenAI:BudgetAlertRecipientAdminPublicId"] = "0f269121-89a5-43a4-97f5-b95278bc0cf6",
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
                services.RemoveAll<IAiSupportInteractionStore>();
                services.RemoveAll<IAiConsentManager>();
                services.RemoveAll<IAiMemberUsageReader>();
                services.RemoveAll<IAiAdminUsageReader>();
                services.AddSingleton<IAiSupportAdmissionGate>(admission);
                services.AddSingleton<IAiSupportContextReader>(
                    context ?? new StubContextReader(
                        new AiSupportContextReadResult(
                            AiSupportContextStatus.Allowed,
                            DataItems: [])));
                services.AddSingleton<IAiSupportModelClient>(model);
                services.AddSingleton<IAiSupportInteractionStore, StubInteractionStore>();
                services.AddSingleton<IAiConsentManager>(
                    consent ?? new StubConsentManager(new AiConsentSnapshot(
                        AiConsentState.Missing,
                        AiConsentPolicy.CurrentVersion,
                        Locale: null,
                        DecidedAtUtc: null)));
                services.AddSingleton<IAiMemberUsageReader>(
                    usage ?? new StubUsageReader(new AiMemberUsageSnapshot(
                        0, 20, ResetAtUtc.AddDays(-1), ResetAtUtc)));
                services.AddSingleton<IAiAdminUsageReader>(new StubAdminUsageReader());
            });
        });

    private static AiSupportAccessState GrantedAccess() =>
        new(AiConsentState.Granted, 20, ResetAtUtc);

    private static object ValidRequest(
        string message = "請說明退貨流程",
        string locale = "zh-TW",
        Guid[]? referencedOrderPublicIds = null,
        Guid? conversationPublicId = null,
        Guid[]? referencedSupportTicketPublicIds = null) => new
        {
            conversationPublicId,
            message,
            referencedOrderPublicIds = referencedOrderPublicIds ?? [],
            referencedSupportTicketPublicIds = referencedSupportTicketPublicIds ?? [],
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

    private static async Task SignInAdminAsync(HttpClient client, string role)
    {
        var token = await GetAntiforgeryTokenAsync(client, "admin");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/__tests/ai/admin-sign-in/{role}")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string clientName = "member")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, clientName);
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

        public Guid? LastConversationPublicId { get; private set; }

        public IReadOnlyList<Guid>? LastReferencedSupportTicketPublicIds { get; private set; }

        public Task<AiSupportContextReadResult> ReadAsync(
            Guid memberId,
            Guid? conversationPublicId,
            IReadOnlyList<Guid> referencedOrderPublicIds,
            IReadOnlyList<Guid> referencedSupportTicketPublicIds,
            CancellationToken cancellationToken)
        {
            LastConversationPublicId = conversationPublicId;
            LastReferencedOrderPublicIds = referencedOrderPublicIds;
            LastReferencedSupportTicketPublicIds = referencedSupportTicketPublicIds;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubConsentManager(AiConsentSnapshot snapshot) : IAiConsentManager
    {
        private AiConsentSnapshot _snapshot = snapshot;

        public Task<AiConsentSnapshot> ReadCurrentAsync(Guid memberId, CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);

        public Task<AiConsentSnapshot> GrantAsync(
            Guid memberId,
            int policyVersion,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            _snapshot = new AiConsentSnapshot(
                AiConsentState.Granted,
                policyVersion,
                locale,
                ResetAtUtc.AddDays(-1));
            return Task.FromResult(_snapshot);
        }

        public Task<AiConsentSnapshot> WithdrawAsync(Guid memberId, CancellationToken cancellationToken)
        {
            _snapshot = _snapshot with { State = AiConsentState.Denied };
            return Task.FromResult(_snapshot);
        }
    }

    private sealed class StubUsageReader(AiMemberUsageSnapshot snapshot) : IAiMemberUsageReader
    {
        public Task<AiMemberUsageSnapshot?> ReadSupportUsageAsync(
            Guid memberId,
            CancellationToken cancellationToken) => Task.FromResult<AiMemberUsageSnapshot?>(snapshot);
    }

    private sealed class StubAdminUsageReader : IAiAdminUsageReader
    {
        public Task<AiAdminUsageSnapshot?> ReadAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<AiAdminUsageSnapshot?>(new AiAdminUsageSnapshot(
                fromUtc,
                toUtc,
                [new AiAdminUsageRow("support", "integration-model", "answered", 2, 100, 20, 1.25m)],
                71.25m,
                BudgetWarningActive: true,
                BudgetProtectionActive: false,
                ResetAtUtc));
    }


    private sealed class StubInteractionStore : IAiSupportInteractionStore
    {
        public Task<AiSupportInteractionWriteResult> SaveAsync(
            AiSupportInteractionWrite interaction,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiSupportInteractionWriteResult(
                true,
                interaction.ConversationPublicId ?? Guid.Parse("44444444-4444-4444-4444-444444444444")));
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

    [HttpPost("admin-sign-in/{role}")]
    public async Task<IActionResult> SignInAdmin(string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            new Claim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin),
            new Claim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor),
            new Claim(ClaimTypes.Role, role),
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, DoSelectAuthenticationSchemes.Admin));

        await HttpContext.SignInAsync(DoSelectAuthenticationSchemes.Admin, principal);
        return NoContent();
    }
}

internal static class AiSupportEndpointTestsMemberId
{
    public const string Value = "11111111-1111-1111-1111-111111111111";
}
