using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

public sealed class AiProductSearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/api/v1/ai/product-search/recommendations";
    private static readonly DateTimeOffset ResetAtUtc =
        new(2026, 8, 30, 16, 0, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> _factory;

    public AiProductSearchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ProductionRegistration_UsesOpenAiProductSearchClient()
    {
        using var scope = _factory.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IAiProductSearchModelClient>();

        Assert.IsType<OpenAiProductSearchClient>(client);
    }

    [Fact]
    public async Task AnonymousSafeRequest_ReturnsGroundedRecommendationAndBrowserCookie()
    {
        var product = Product();
        var model = new StubModel(
            Intent(),
            [new AiProductRecommendationReason(product.DefaultSkuPublicId, "符合剪輯用途與預算。")]);
        using var factory = CreateFactory(
            new StubAdmission(30),
            model,
            new StubCatalog([new AiProductSearchCandidate(product, AiCompatibilityStatus.NotRequired, [])]));
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("recommendations", json.RootElement.GetProperty("resultType").GetString());
        Assert.Equal(
            product.DefaultSkuPublicId,
            json.RootElement.GetProperty("recommendations")[0]
                .GetProperty("product").GetProperty("defaultSkuPublicId").GetGuid());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.Contains(".DoSelect.AiBrowser=", StringComparison.Ordinal));
        Assert.Equal(1, model.ParseCount);
        Assert.Equal(1, model.ExplainCount);
    }

    [Fact]
    public async Task CustomBuild_ReturnsAdditiveCompleteBuildContractAndNoStandaloneRecommendations()
    {
        var cpu = Product() with
        {
            Name = "懂選處理器",
            Category = new ProductCategoryRef("CPU", "處理器"),
            Price = new ProductPrice(10_000, null, "TWD"),
        };
        var intent = Intent() with
        {
            Intent = AiProductSearchIntentType.CustomBuild,
            CategoryCode = null,
        };
        var customBuild = new AiCustomBuildCandidate(
            [
                new AiCustomBuildComponentCandidate(
                    cpu,
                    cpu.DefaultSkuPublicId,
                    "catalogSku",
                    "CPU",
                    cpu.Name,
                    1,
                    IsExistingPart: false),
            ],
            PurchaseSubtotal: 10_000,
            AssemblyFee: 300,
            PurchaseTotal: 10_300,
            Currency: "TWD",
            AiCompatibilityStatus.Compatible,
            CompatibilityMessageKeys: []);
        using var factory = CreateFactory(
            new StubAdmission(30),
            new StubModel(
                intent,
                [new AiProductRecommendationReason(cpu.DefaultSkuPublicId, "符合完整組裝預算。")]),
            new StubCatalog([], customBuild: customBuild));
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, json.RootElement.GetProperty("recommendations").GetArrayLength());
        var build = json.RootElement.GetProperty("customBuild");
        Assert.Equal(10_300, build.GetProperty("purchaseTotal").GetDecimal());
        Assert.Equal(300, build.GetProperty("assemblyFee").GetDecimal());
        Assert.Equal("Compatible", build.GetProperty("compatibilityStatus").GetString());
        Assert.Equal("CPU", build.GetProperty("components")[0].GetProperty("categoryCode").GetString());
        Assert.Equal(
            "符合完整組裝預算。",
            build.GetProperty("components")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ExhaustedAnonymousQuota_Returns429WithoutCallingModel()
    {
        var model = new StubModel(Intent(), []);
        using var factory = CreateFactory(
            new StubAdmission(0),
            model,
            new StubCatalog([]));
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AiUsageLimitExceeded, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, model.ParseCount);
    }

    [Fact]
    public async Task DisabledFeature_ReturnsExplicitKeywordFallback()
    {
        var fallback = Product();
        var model = new StubModel(Intent(), []);
        using var factory = CreateFactory(
            new StubAdmission(10),
            model,
            new StubCatalog([], [fallback]),
            aiEnabled: false);
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, ValidRequest(), token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("degraded", json.RootElement.GetProperty("resultType").GetString());
        Assert.Equal("keywordSearch", json.RootElement.GetProperty("degradationMode").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("fallbackProducts").GetArrayLength());
        Assert.Equal(0, model.ParseCount);
    }

    [Fact]
    public async Task StructuredManualExistingPart_MapsConfirmedUnionToApplication()
    {
        var product = Product();
        var catalog = new StubCatalog(
            [new AiProductSearchCandidate(product, AiCompatibilityStatus.Compatible, [])]);
        using var factory = CreateFactory(
            new StubAdmission(10),
            new StubModel(
                Intent(),
                [new AiProductRecommendationReason(product.DefaultSkuPublicId, "相容性已驗證。")]),
            catalog);
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, new
        {
            message = "找能搭配既有 CPU 的主機板",
            locale = "zh-TW",
            existingParts = new[]
            {
                new
                {
                    sourceType = "structuredManual",
                    skuPublicId = (Guid?)null,
                    categoryCode = "CPU",
                    displayName = "既有處理器",
                    quantity = 1,
                    confirmedByUser = true,
                    specifications = new[]
                    {
                        new { semanticKey = "cpu_socket", @operator = "eq", value = "AM5", unit = (string?)null },
                    },
                },
            },
        }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var part = Assert.Single(catalog.LastExistingParts!);
        Assert.Equal("structuredManual", part.SourceType);
        Assert.Equal("CPU", part.CategoryCode);
        Assert.Equal("既有處理器", part.DisplayName);
    }

    [Fact]
    public async Task NaturalLanguageExistingPart_ReturnsProposalWithoutRunningCandidateQuery()
    {
        var intent = Intent() with
        {
            ProposedExistingParts =
            [
                new AiProductSearchProposedPart(
                    "CPU",
                    "AM5 CPU",
                    [new AiRequiredSpec("cpu_socket", "eq", "AM5", null)],
                    1),
            ],
        };
        var catalog = new StubCatalog([]);
        using var factory = CreateFactory(
            new StubAdmission(10),
            new StubModel(intent, []),
            catalog);
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, new
        {
            message = "已有 AM5 CPU，想找主機板",
            locale = "zh-TW",
            existingParts = Array.Empty<object>(),
        }, token);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("clarification", json.RootElement.GetProperty("resultType").GetString());
        var proposal = json.RootElement.GetProperty("intent").GetProperty("proposedExistingParts")[0];
        Assert.Equal("CPU", proposal.GetProperty("categoryCode").GetString());
        Assert.Equal("AM5", proposal.GetProperty("specifications")[0].GetProperty("value").GetString());
        Assert.Equal(0, catalog.CandidateReadCount);
    }

    private WebApplicationFactory<Program> CreateFactory(
        StubAdmission admission,
        StubModel model,
        StubCatalog catalog,
        bool aiEnabled = true) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Features:AiEnabled"] = aiEnabled.ToString(),
                    ["OpenAI:ApiKey"] = aiEnabled ? "integration-test-placeholder" : null,
                    ["OpenAI:SupportModel"] = "integration-test-support-model",
                    ["OpenAI:SupportInputCostPerMillionTokens"] = "0",
                    ["OpenAI:SupportOutputCostPerMillionTokens"] = "0",
                    ["OpenAI:ProductSearchModel"] = "integration-test-search-model",
                    ["OpenAI:ProductSearchTimeoutMilliseconds"] = "5000",
                    ["OpenAI:ProductSearchInputCostPerMillionTokens"] = "0",
                    ["OpenAI:ProductSearchOutputCostPerMillionTokens"] = "0",
                    ["OpenAI:AnonymousIdentityPepper"] = "integration-test-ai-anonymous-pepper-32-bytes",
                    ["OpenAI:BudgetAlertRecipientAdminPublicId"] = "0f269121-89a5-43a4-97f5-b95278bc0cf6",
                }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.RemoveAll<IAiProductSearchAdmissionGate>();
                services.RemoveAll<IAiProductSearchModelClient>();
                services.RemoveAll<IAiProductSearchCatalog>();
                services.RemoveAll<IAiProductSearchInteractionStore>();
                services.AddSingleton<IAiProductSearchAdmissionGate>(admission);
                services.AddSingleton<IAiProductSearchModelClient>(model);
                services.AddSingleton<IAiProductSearchCatalog>(catalog);
                services.AddSingleton<IAiProductSearchInteractionStore, StubStore>();
            });
        });

    private static object ValidRequest() => new
    {
        message = "五萬元剪輯 4K 影片",
        locale = "zh-TW",
        existingParts = Array.Empty<object>(),
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

    private static AiProductSearchIntent Intent() =>
        new(
            AiProductSearchIntentType.PrebuiltComputer,
            ["VideoEditing"],
            new AiBudgetRange(null, 50_000),
            "剪輯",
            "PREBUILT_COMPUTER",
            [],
            [],
            [],
            ["安靜"],
            [],
            []);

    private static ProductCardDto Product() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "PC-CREATOR",
            "PC-CREATOR-01",
            "創作者工作站",
            new ProductBrandRef("DOSELECT", "懂選"),
            new ProductCategoryRef("PREBUILT_COMPUTER", "套裝電腦"),
            new ProductPrice(49_000, null, "TWD"),
            ProductAvailabilityCodes.InStock,
            null,
            []);

    private sealed class StubAdmission(int remaining) : IAiProductSearchAdmissionGate
    {
        public Task<AiProductSearchAccessState> ReadAsync(
            AiProductSearchActor actor,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchAccessState(
                remaining,
                ResetAtUtc,
                BudgetProtectionActive: false,
                IsDemoAllowlisted: false));

        public Task<AiProductSearchReservationResult> TryReserveAsync(
            AiProductSearchActor actor,
            Guid requestPublicId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchReservationResult(
                remaining > 0,
                new AiProductSearchAccessState(
                    Math.Max(0, remaining - 1),
                    ResetAtUtc,
                    BudgetProtectionActive: false,
                    IsDemoAllowlisted: false)));
    }

    private sealed class StubModel(
        AiProductSearchIntent intent,
        IReadOnlyList<AiProductRecommendationReason> reasons) : IAiProductSearchModelClient
    {
        public int ParseCount { get; private set; }
        public int ExplainCount { get; private set; }

        public Task<AiProductSearchIntentResult> ParseIntentAsync(
            string message,
            SupportedLocale locale,
            AiProductSearchMetadata metadata,
            CancellationToken cancellationToken)
        {
            ParseCount++;
            return Task.FromResult(new AiProductSearchIntentResult(
                AiProductSearchModelStatus.Completed,
                intent,
                new AiSupportModelUsage("search-model", 10, 5)));
        }

        public Task<AiProductSearchExplanationResult> ExplainAsync(
            AiProductSearchIntent parsedIntent,
            IReadOnlyList<ProductCardDto> approvedCandidates,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            ExplainCount++;
            return Task.FromResult(new AiProductSearchExplanationResult(
                AiProductSearchModelStatus.Completed,
                reasons,
                new AiSupportModelUsage("search-model", 10, 5)));
        }
    }

    private sealed class StubCatalog(
        IReadOnlyList<AiProductSearchCandidate> candidates,
        IReadOnlyList<ProductCardDto>? fallback = null,
        AiCustomBuildCandidate? customBuild = null) : IAiProductSearchCatalog
    {
        public IReadOnlyList<AiProductSearchExistingPart>? LastExistingParts { get; private set; }
        public int CandidateReadCount { get; private set; }

        public Task<AiProductSearchMetadata> ReadMetadataAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchMetadata(
                ["PREBUILT_COMPUTER"],
                ["DOSELECT"],
                ["memory_type"]));

        public Task<AiProductSearchCandidateResult> FindCandidatesAsync(
            AiProductSearchIntent intent,
            IReadOnlyList<AiProductSearchExistingPart> existingParts,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            CandidateReadCount++;
            LastExistingParts = existingParts;
            return Task.FromResult(new AiProductSearchCandidateResult(
                true,
                AiSafetyReason.None,
                candidates,
                [],
                customBuild));
        }

        public Task<IReadOnlyList<ProductCardDto>> KeywordFallbackAsync(
            string message,
            SupportedLocale locale,
            CancellationToken cancellationToken) =>
            Task.FromResult(fallback ?? []);
    }

    private sealed class StubStore : IAiProductSearchInteractionStore
    {
        public Task<bool> SaveAsync(
            AiProductSearchInteractionWrite interaction,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
