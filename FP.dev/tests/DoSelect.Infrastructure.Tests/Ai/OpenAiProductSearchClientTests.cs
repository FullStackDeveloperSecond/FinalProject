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
        Assert.Equal("none", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("low", body.RootElement.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.False(body.RootElement.TryGetProperty("service_tier", out _));
        var instructions = body.RootElement.GetProperty("instructions").GetString();
        Assert.Contains("Preserve every explicitly stated budget boundary", instructions, StringComparison.Ordinal);
        Assert.Contains("Add only purposes explicitly requested", instructions, StringComparison.Ordinal);
        Assert.Contains("ready-made, prebuilt, branded package", instructions, StringComparison.Ordinal);
        Assert.Contains("budget-based gaming 主機", instructions, StringComparison.Ordinal);
        Assert.Contains("generic 主機", instructions, StringComparison.Ordinal);
        Assert.Contains("遊戲美術", instructions, StringComparison.Ordinal);
        Assert.Contains("Do not ask about optional preferences", instructions, StringComparison.Ordinal);
        Assert.Contains("set minimum to null", instructions, StringComparison.Ordinal);
        Assert.Equal("product-search-v5", OpenAiProductSearchClient.PromptVersion);
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal(
            "json_schema",
            body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.DoesNotContain(
            "\"uniqueItems\"",
            body.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema").GetRawText(),
            StringComparison.Ordinal);
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
        Assert.Equal("untrusted_user_input", input.RootElement.GetProperty("userMessage").GetProperty("trust").GetString());
        Assert.Equal("untrusted_data", input.RootElement.GetProperty("allowedCatalog").GetProperty("trust").GetString());
    }

    [Fact]
    public async Task ParseIntentAsync_ConflictingBudgetUsesSafeMaximumAndClarification()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "PrebuiltComputer",
            purposes = Array.Empty<string>(),
            budget = new { minimum = (decimal?)null, maximum = 15_000m },
            keyword = "主機",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = new[] { "您同時指定至少兩萬元與最多一萬五，請確認可接受的預算範圍。" },
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "我要兩萬元以上的主機，但最多只能花一萬五。",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget?.Minimum);
        Assert.Equal(15_000m, result.Intent?.Budget?.Maximum);
        Assert.Single(result.Intent!.Clarifications);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_DuplicateBrandCode_FailsClosedWithoutSynchronousRetry()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "PrebuiltComputer",
            purposes = new[] { "VideoEditing" },
            budget = new { minimum = (decimal?)null, maximum = 50_000m },
            keyword = "剪輯",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = new[] { "DOSELECT", "DOSELECT" },
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal("INTENT_DUPLICATE_VALUE", result.ValidationFailureCode);
        Assert.Equal("preferredBrandCodes", result.ValidationFailureField);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_InvalidSchema_FailsClosedWithoutSynchronousRetry()
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
        Assert.Equal("RESPONSE_JSON_INVALID", result.ValidationFailureCode);
        Assert.Equal("output_text", result.ValidationFailureField);
        Assert.Equal(100, result.Usage?.InputTokens);
        Assert.Equal(20, result.Usage?.OutputTokens);
        Assert.Equal(1, handler.CallCount);
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
    public async Task ParseIntentAsync_TransientHttpFailure_DegradesWithoutSynchronousRetry()
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

        Assert.Equal(AiProductSearchModelStatus.Unavailable, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_Timeout_DegradesWithoutSynchronousRetry()
    {
        var handler = new CancellationAwareHandler();
        var subject = CreateSubject(handler, timeoutMilliseconds: 10);

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
    public async Task ExplainAsync_ApprovedCandidate_ReturnsGroundedReasonWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No explanation HTTP call expected."));
        var subject = CreateSubject(handler);

        var result = await subject.ExplainAsync(
            Intent(),
            [Product()],
            SupportedLocale.ZhTw,
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(Product().DefaultSkuPublicId, reason.SkuPublicId);
        Assert.Contains("創作者工作站", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("NT$49,000", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("最高預算 NT$50,000", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("GPU 預算優先", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("64GB RAM", reason.Reason, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task ExplainAsync_BrandPreferenceAndExclusion_ExplainsVerifiedCandidateScope()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No explanation HTTP call expected."));
        var subject = CreateSubject(handler);
        var intent = Intent() with
        {
            PreferredBrandCodes = ["NOVACORE"],
            ExcludedBrandCodes = ["PIXELFORGE"],
        };

        var result = await subject.ExplainAsync(
            intent,
            [Product()],
            SupportedLocale.ZhTw,
            default);

        var reason = Assert.Single(result.Reasons).Reason;
        Assert.Contains("偏好品牌 NOVACORE", reason, StringComparison.Ordinal);
        Assert.Contains("候選品牌為 DOSELECT", reason, StringComparison.Ordinal);
        Assert.Contains("未命中排除品牌 PIXELFORGE", reason, StringComparison.Ordinal);
        Assert.Contains("只套用於通過後端驗證的候選", reason, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsync_ExcludedBrandWithoutPreference_DoesNotRenderEmptyPreference()
    {
        var subject = CreateSubject(new RecordingHandler(_ =>
            throw new InvalidOperationException("No explanation HTTP call expected.")));
        var intent = Intent() with { ExcludedBrandCodes = ["PIXELFORGE"] };

        var result = await subject.ExplainAsync(
            intent,
            [Product()],
            SupportedLocale.ZhTw,
            default);

        var reason = Assert.Single(result.Reasons).Reason;
        Assert.DoesNotContain("偏好品牌 ，", reason, StringComparison.Ordinal);
        Assert.Contains("候選品牌為 DOSELECT", reason, StringComparison.Ordinal);
        Assert.Contains("未命中排除品牌 PIXELFORGE", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_TooManyCandidates_FailsClosedWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No explanation HTTP call expected."));
        var subject = CreateSubject(handler);

        var result = await subject.ExplainAsync(
            Intent(),
            Enumerable.Range(0, 7)
                .Select(index => Product() with
                {
                    DefaultSkuPublicId = Guid.Parse($"22222222-2222-2222-2222-{index + 1:000000000000}"),
                })
                .ToArray(),
            SupportedLocale.ZhTw,
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Empty(result.Reasons);
        Assert.Equal(0, handler.CallCount);
    }

    private static OpenAiProductSearchClient CreateSubject(
        HttpMessageHandler handler,
        int timeoutMilliseconds = 5_000) =>
        new(
            new HttpClient(handler),
            Options.Create(new OpenAiResponsesOptions
            {
                ApiKey = "synthetic-key",
                ProductSearchModel = "gpt-5.6-luna",
                ProductSearchTimeoutMilliseconds = timeoutMilliseconds,
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
            ["GPU 預算優先", "64GB RAM"]);

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

    private sealed class CancellationAwareHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The timeout token should cancel the request.");
        }
    }
}
