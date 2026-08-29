using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class OpenAiProductSearchClientTests
{
    [Fact]
    public async Task ParseIntentAsync_CompletedStrictOutput_UsesStatelessWhitelistContract()
    {
        var handler = new RecordingHandler(_ => JsonResponse(IntentResponse()));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Equal(AiProductSearchIntentType.PrebuiltComputer, result.Intent?.Intent);
        Assert.Equal(50_000, result.Intent?.Budget?.Maximum);
        Assert.Equal("gpt-5.6-luna-snapshot", result.Usage?.Model);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal(
            "json_schema",
            body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
        Assert.Equal("untrusted_user_input", input.RootElement.GetProperty("userMessage").GetProperty("trust").GetString());
        Assert.Equal("untrusted_data", input.RootElement.GetProperty("allowedCatalog").GetProperty("trust").GetString());
    }

    [Fact]
    public async Task ParseIntentAsync_InvalidSchema_RetriesOnceThenFailsClosed()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{\"intent\":17}"));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_NonTransientHttpFailure_DoesNotRetry()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Unavailable, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_TransientHttpFailure_RetriesOnceThenCompletes()
    {
        var handler = new RecordingHandler(attempt =>
            attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : JsonResponse(IntentResponse()));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.NotNull(result.Intent);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_NaturalLanguageExistingPart_ReturnsUnconfirmedProposalOnly()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "SingleProduct",
            purposes = Array.Empty<string>(),
            budget = (object?)null,
            keyword = "主機板",
            categoryCode = "MOTHERBOARD",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = new[]
            {
                new
                {
                    categoryCode = "CPU",
                    displayName = "AM5 CPU",
                    quantity = 1,
                    specifications = new[]
                    {
                        new { semanticKey = "cpu_socket", @operator = "eq", value = "AM5", unit = (string?)null },
                    },
                },
            },
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "已有 AM5 CPU，想找主機板",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(
                ["CPU", "MOTHERBOARD"],
                ["DOSELECT"],
                ["cpu_socket"]),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        var proposal = Assert.Single(result.Intent!.ProposedExistingParts);
        Assert.Equal("CPU", proposal.CategoryCode);
        Assert.Equal("AM5", Assert.Single(proposal.Specifications).Value);
    }

    [Fact]
    public async Task ExplainAsync_UnknownCandidateId_FailsClosed()
    {
        var handler = new RecordingHandler(_ => JsonResponse(JsonSerializer.Serialize(new
        {
            recommendations = new[]
            {
                new { skuPublicId = "99999999-9999-9999-9999-999999999999", reason = "unsupported" },
            },
        })));
        var subject = CreateSubject(handler);

        var result = await subject.ExplainAsync(
            Intent(),
            [Product()],
            SupportedLocale.ZhTw,
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public async Task ExplainAsync_NonTransientHttpFailure_DoesNotRetry()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var subject = CreateSubject(handler);

        var result = await subject.ExplainAsync(
            Intent(),
            [Product()],
            SupportedLocale.ZhTw,
            default);

        Assert.Equal(AiProductSearchModelStatus.Unavailable, result.Status);
        Assert.Empty(result.Reasons);
        Assert.Equal(1, handler.CallCount);
    }

    private static OpenAiProductSearchClient CreateSubject(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new OpenAiResponsesOptions
            {
                ApiKey = "synthetic-key",
                ProductSearchModel = "gpt-5.6-luna",
                ProductSearchTimeoutMilliseconds = 8_000,
            }));

    private static AiProductSearchMetadata Metadata() =>
        new(["PREBUILT_COMPUTER"], ["DOSELECT"], ["memory_type"]);

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

    private static string IntentResponse() => JsonSerializer.Serialize(new
    {
        intent = "PrebuiltComputer",
        purposes = new[] { "VideoEditing" },
        budget = new { minimum = (decimal?)null, maximum = 50_000m },
        keyword = "剪輯",
        categoryCode = "PREBUILT_COMPUTER",
        preferredBrandCodes = Array.Empty<string>(),
        excludedBrandCodes = Array.Empty<string>(),
        requiredSpecs = Array.Empty<object>(),
        preferences = new[] { "安靜" },
        proposedExistingParts = Array.Empty<object>(),
        clarifications = Array.Empty<string>(),
    });

    private static HttpResponseMessage JsonResponse(string outputText)
    {
        var body = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = outputText,
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class RecordingHandler(
        Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization;
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responseFactory(CallCount);
        }
    }
}
