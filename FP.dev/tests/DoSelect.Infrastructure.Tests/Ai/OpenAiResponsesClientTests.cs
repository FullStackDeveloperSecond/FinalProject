using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Ai;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class OpenAiResponsesClientTests
{
    private const string ApiKey = "synthetic-openai-api-key";
    private const string OrderSourceId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task GenerateAsync_CompletedStructuredResponse_UsesStatelessContractAndTrustedCitation()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(JsonResponse(StructuredOutput(
                answer: "返品申請は注文ページから行えます。",
                sourceId: OrderSourceId))));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(SupportedLocale.JaJp), default);

        Assert.Equal(AiSupportModelAnswerStatus.Answered, result.Status);
        Assert.Equal("返品申請は注文ページから行えます。", result.Answer);
        var citation = Assert.Single(result.Citations);
        Assert.Equal("order", citation.SourceType);
        Assert.Equal(OrderSourceId, citation.SourceId);
        Assert.Equal("ORD-20260828-001", citation.Title);
        Assert.Equal("2026-08-28T02:30:00.0000000Z", citation.VersionOrUpdatedAt);
        Assert.Equal("gpt-5.6-terra-2026-08-01", result.Usage?.Model);
        Assert.Equal(128, result.Usage?.InputTokens);
        Assert.Equal(42, result.Usage?.OutputTokens);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(ApiKey, handler.Authorization?.Parameter);

        using var request = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var root = request.RootElement;
        Assert.Equal("gpt-5.6-terra", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.DoesNotContain("previous_response_id", root.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("tools", root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.True(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal(
            8,
            root.GetProperty("text")
                .GetProperty("format")
                .GetProperty("schema")
                .GetProperty("properties")
                .GetProperty("citations")
                .GetProperty("maxItems")
                .GetInt32());
        var sourceTypes = root.GetProperty("text")
            .GetProperty("format")
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("citations")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("sourceType")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("support_ticket", sourceTypes);

        using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!);
        Assert.Equal("ja-JP", input.RootElement.GetProperty("responseLocale").GetString());
        Assert.Equal(
            "untrusted_user_input",
            input.RootElement.GetProperty("userMessage").GetProperty("trust").GetString());
        Assert.Equal(
            "untrusted_data",
            input.RootElement.GetProperty("approvedData")[0].GetProperty("trust").GetString());
    }

    [Fact]
    public async Task GenerateAsync_TransientFailure_RetriesOnceThenReturnsAnswer()
    {
        var handler = new RecordingHandler((attempt, _, _) => Task.FromResult(
            attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : JsonResponse(StructuredOutput("已恢復服務。"))));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(), default);

        Assert.Equal(AiSupportModelAnswerStatus.Answered, result.Status);
        Assert.Equal("已恢復服務。", result.Answer);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_InvalidStructuredOutput_RetriesOnceThenFailsClosed()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(JsonResponse("{\"answer\":17,\"citations\":[]}")));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(), default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Null(result.Answer);
        Assert.Empty(result.Citations);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_CompletedResponseWithoutUsage_RetriesOnceThenFailsClosed()
    {
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-terra-2026-08-01",
            output_text = StructuredOutput("usage is required"),
        });
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            }));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(), default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Null(result.Usage);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_NonTransientHttpFailure_DoesNotRetry()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(), default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_CitationOutsideApprovedData_FailsClosed()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(JsonResponse(StructuredOutput(
                answer: "不應採用的回答",
                sourceId: "99999999-9999-9999-9999-999999999999"))));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(), default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Null(result.Answer);
        Assert.Empty(result.Citations);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_NullCitation_FailsClosedWithoutThrowing()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(JsonResponse(
                "{\"answer\":\"invalid citation\",\"citations\":[null],\"needsHumanSupport\":false}")));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(), default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Null(result.Answer);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_ModelRequestsHumanSupport_FailsClosed()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(JsonResponse(StructuredOutput(
                answer: "請改由人工客服協助。",
                needsHumanSupport: true))));
        using var subject = CreateSubject(handler);

        var result = await subject.GenerateAsync(CreateEnvelope(), default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Null(result.Answer);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_CallerCancellation_PropagatesCancellation()
    {
        var handler = new RecordingHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return JsonResponse(StructuredOutput("unused"));
        });
        using var subject = CreateSubject(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            subject.GenerateAsync(CreateEnvelope(), cancellation.Token));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_UnapprovedSourceType_DoesNotSendRequest()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(JsonResponse(StructuredOutput("unused"))));
        using var subject = CreateSubject(handler);
        var envelope = CreateEnvelope() with
        {
            DataItems =
            [
                new AiPromptContent(
                    "synthetic internal data",
                    AiContentTrust.UntrustedData,
                    SourceType: "member_secret",
                    SourceId: "secret-source",
                    Title: "should not leave the server",
                    VersionOrUpdatedAt: "v1"),
            ],
        };

        var result = await subject.GenerateAsync(envelope, default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_InvalidLocale_DoesNotSendRequest()
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(JsonResponse(StructuredOutput("unused"))));
        using var subject = CreateSubject(handler);
        var envelope = CreateEnvelope() with
        {
            ResponseLocale = (SupportedLocale)999,
        };

        var result = await subject.GenerateAsync(envelope, default);

        Assert.Equal(AiSupportModelAnswerStatus.Unavailable, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    private static OpenAiResponsesClient CreateSubject(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var options = Options.Create(new OpenAiResponsesOptions
        {
            ApiKey = ApiKey,
            SupportModel = "gpt-5.6-terra",
            SupportTimeoutMilliseconds = 12_000,
        });
        return new OpenAiResponsesClient(httpClient, options);
    }

    private static AiPromptEnvelope CreateEnvelope(
        SupportedLocale locale = SupportedLocale.ZhTw) =>
        new(
            locale,
            "Only answer from approved data.",
            new AiPromptContent(
                "請說明這張訂單如何退貨",
                AiContentTrust.UntrustedUserInput),
            [
                new AiPromptContent(
                    "{\"orderPublicId\":\"33333333-3333-3333-3333-333333333333\",\"status\":\"Completed\"}",
                    AiContentTrust.UntrustedData,
                    SourceType: "order",
                    SourceId: OrderSourceId,
                    Title: "ORD-20260828-001",
                    VersionOrUpdatedAt: "2026-08-28T02:30:00.0000000Z"),
            ],
            AllowedToolNames: []);

    private static HttpResponseMessage JsonResponse(string structuredOutput)
    {
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-terra-2026-08-01",
            usage = new
            {
                input_tokens = 128,
                output_tokens = 42,
            },
            output = new[]
            {
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new { type = "output_text", text = structuredOutput },
                    },
                },
            },
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }

    private static string StructuredOutput(
        string answer,
        string? sourceId = null,
        bool needsHumanSupport = false) =>
        JsonSerializer.Serialize(new
        {
            answer,
            citations = sourceId is null
                ? []
                : new[]
                {
                    new
                    {
                        sourceType = "order",
                        sourceId,
                        title = "model supplied title must not be trusted",
                        versionOrUpdatedAt = "model supplied version must not be trusted",
                    },
                },
            needsHumanSupport,
        });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public RecordingHandler(
            Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        public int CallCount { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization;
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return await _send(CallCount, request, cancellationToken);
        }
    }
}
