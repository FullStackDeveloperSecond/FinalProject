using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoSelect.Application.Ai;
using DoSelect.Domain.Members;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Ai;

public sealed class OpenAiResponsesOptions
{
    public const string SectionName = "OpenAI";

    public string? ApiKey { get; set; }

    public string SupportModel { get; set; } = "gpt-5.6-terra";

    public string ProductSearchModel { get; set; } = "gpt-5.6-luna";

    public int SupportTimeoutMilliseconds { get; set; } = 12_000;

    public int ProductSearchTimeoutMilliseconds { get; set; } = 8_000;

    public decimal SupportInputCostPerMillionTokens { get; set; } = -1m;

    public decimal SupportOutputCostPerMillionTokens { get; set; } = -1m;

    public decimal ProductSearchInputCostPerMillionTokens { get; set; } = -1m;

    public decimal ProductSearchOutputCostPerMillionTokens { get; set; } = -1m;

    public string AnonymousIdentityPepper { get; set; } = string.Empty;

    public Guid? BudgetAlertRecipientAdminPublicId { get; set; }

    public Guid[] DemoMemberPublicIds { get; set; } = [];

    public Guid[] DemoBrowserIds { get; set; } = [];
}

public sealed class OpenAiResponsesClient : IAiSupportModelClient, IDisposable
{
    private const int MaximumAttempts = 2;
    private const int MaximumAnswerLength = 4_000;
    private const int MaximumCitations = 8;
    private static readonly Uri ResponsesEndpoint =
        new("https://api.openai.com/v1/responses", UriKind.Absolute);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly JsonElement SupportOutputSchema = CreateSupportOutputSchema();

    private readonly HttpClient _httpClient;
    private readonly OpenAiResponsesOptions _options;

    public OpenAiResponsesClient(
        HttpClient httpClient,
        IOptions<OpenAiResponsesOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AiSupportModelAnswer> GenerateAsync(
        AiPromptEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.SupportModel) ||
            _options.SupportTimeoutMilliseconds <= 0 ||
            !IsValidEnvelope(envelope))
        {
            return Unavailable();
        }

        var payload = CreateRequestPayload(envelope);
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await SendAttemptAsync(payload, envelope, cancellationToken);
            if (outcome.Answer is not null)
            {
                return outcome.Answer;
            }

            if (!outcome.MayRetry)
            {
                break;
            }
        }

        return Unavailable();
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<AttemptOutcome> SendAttemptAsync(
        object payload,
        AiPromptEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(_options.SupportTimeoutMilliseconds));
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AttemptOutcome.RetryableFailure;
        }
        catch (HttpRequestException)
        {
            return AttemptOutcome.RetryableFailure;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return IsTransient(response.StatusCode)
                    ? AttemptOutcome.RetryableFailure
                    : AttemptOutcome.TerminalFailure;
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: timeout.Token);
                return TryMapCompletedResponse(document.RootElement, envelope);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AttemptOutcome.RetryableFailure;
            }
            catch (JsonException)
            {
                return AttemptOutcome.RetryableFailure;
            }
            catch (HttpRequestException)
            {
                return AttemptOutcome.RetryableFailure;
            }
        }
    }

    private object CreateRequestPayload(AiPromptEnvelope envelope)
    {
        var input = JsonSerializer.Serialize(
            new
            {
                responseLocale = ToLocaleCode(envelope.ResponseLocale),
                userMessage = new
                {
                    trust = "untrusted_user_input",
                    content = envelope.UserMessage.Content,
                },
                approvedData = envelope.DataItems.Select(item => new
                {
                    trust = "untrusted_data",
                    sourceType = item.SourceType,
                    sourceId = item.SourceId,
                    title = item.Title,
                    versionOrUpdatedAt = item.VersionOrUpdatedAt,
                    content = item.Content,
                }),
            },
            JsonOptions);

        return new
        {
            model = _options.SupportModel,
            instructions = envelope.SystemInstructions,
            input,
            store = false,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "do_select_ai_support_answer",
                    strict = true,
                    schema = SupportOutputSchema,
                },
            },
        };
    }

    private static AttemptOutcome TryMapCompletedResponse(
        JsonElement root,
        AiPromptEnvelope envelope)
    {
        if (!root.TryGetProperty("status", out var status) ||
            status.GetString() != "completed" ||
            !TryReadUsage(root, out var usage))
        {
            return AttemptOutcome.RetryableFailure;
        }

        var outputText = TryReadOutputText(root);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return AttemptOutcome.RetryableFailure;
        }

        OpenAiSupportOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<OpenAiSupportOutput>(outputText, JsonOptions);
        }
        catch (JsonException)
        {
            return AttemptOutcome.RetryableFailure;
        }

        if (output is null ||
            output.NeedsHumanSupport ||
            string.IsNullOrWhiteSpace(output.Answer) ||
            output.Answer.Length > MaximumAnswerLength ||
            output.Citations is null ||
            output.Citations.Count > MaximumCitations)
        {
            return output?.NeedsHumanSupport == true
                ? AttemptOutcome.TerminalFailure
                : AttemptOutcome.RetryableFailure;
        }

        var approvedSources = new Dictionary<(string SourceType, string SourceId), AiPromptContent>(
            SourceIdentityComparer.Instance);
        foreach (var item in envelope.DataItems)
        {
            approvedSources.Add((item.SourceType!, item.SourceId!), item);
        }

        var citations = new List<AiSupportCitation>(output.Citations.Count);
        var seen = new HashSet<(string SourceType, string SourceId)>(SourceIdentityComparer.Instance);
        foreach (var citation in output.Citations)
        {
            if (citation is null ||
                string.IsNullOrWhiteSpace(citation.SourceType) ||
                string.IsNullOrWhiteSpace(citation.SourceId))
            {
                return AttemptOutcome.RetryableFailure;
            }

            var key = (citation.SourceType, citation.SourceId);
            if (!approvedSources.TryGetValue(key, out var approved))
            {
                return AttemptOutcome.RetryableFailure;
            }

            if (seen.Add(key))
            {
                citations.Add(new AiSupportCitation(
                    approved.SourceType!,
                    approved.SourceId!,
                    approved.Title!,
                    approved.VersionOrUpdatedAt!));
            }
        }

        return AttemptOutcome.Success(new AiSupportModelAnswer(
            output.Answer,
            AiSupportModelAnswerStatus.Answered,
            citations,
            usage));
    }

    private static bool TryReadUsage(
        JsonElement root,
        out AiSupportModelUsage? usage)
    {
        usage = null;
        if (!root.TryGetProperty("model", out var modelElement) ||
            modelElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(modelElement.GetString()) ||
            !root.TryGetProperty("usage", out var usageElement) ||
            usageElement.ValueKind != JsonValueKind.Object ||
            !usageElement.TryGetProperty("input_tokens", out var inputTokensElement) ||
            !inputTokensElement.TryGetInt32(out var inputTokens) ||
            inputTokens < 0 ||
            !usageElement.TryGetProperty("output_tokens", out var outputTokensElement) ||
            !outputTokensElement.TryGetInt32(out var outputTokens) ||
            outputTokens < 0)
        {
            return false;
        }

        usage = new AiSupportModelUsage(
            modelElement.GetString()!,
            inputTokens,
            outputTokens);
        return true;
    }

    private static string? TryReadOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) &&
            direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString();
        }

        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType) ||
                itemType.GetString() != "message" ||
                !item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var partType) &&
                    partType.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static bool IsValidEnvelope(AiPromptEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.SystemInstructions) ||
            !Enum.IsDefined(envelope.ResponseLocale) ||
            envelope.UserMessage.Trust != AiContentTrust.UntrustedUserInput ||
            string.IsNullOrWhiteSpace(envelope.UserMessage.Content) ||
            envelope.DataItems.Count > MaximumCitations)
        {
            return false;
        }

        var sources = new HashSet<(string SourceType, string SourceId)>(
            SourceIdentityComparer.Instance);
        foreach (var item in envelope.DataItems)
        {
            if (item.Trust != AiContentTrust.UntrustedData ||
                string.IsNullOrWhiteSpace(item.Content) ||
                string.IsNullOrWhiteSpace(item.SourceType) ||
                !IsAllowedSourceType(item.SourceType) ||
                string.IsNullOrWhiteSpace(item.SourceId) ||
                string.IsNullOrWhiteSpace(item.Title) ||
                string.IsNullOrWhiteSpace(item.VersionOrUpdatedAt) ||
                !sources.Add((item.SourceType!, item.SourceId!)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedSourceType(string sourceType) => sourceType is
        "order" or "faq" or "return_policy" or "product" or "support_ticket";

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode is >= 500 and <= 599;

    private static string ToLocaleCode(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "zh-TW",
        SupportedLocale.JaJp => "ja-JP",
        SupportedLocale.KoKr => "ko-KR",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static AiSupportModelAnswer Unavailable() =>
        new(
            Answer: null,
            AiSupportModelAnswerStatus.Unavailable,
            ModelCitations: [],
            Usage: null);

    private static JsonElement CreateSupportOutputSchema() =>
        JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "answer": { "type": "string", "maxLength": 4000 },
                "citations": {
                  "type": "array",
                  "maxItems": 8,
                  "items": {
                    "type": "object",
                    "properties": {
                      "sourceType": {
                        "type": "string",
                        "enum": ["order", "faq", "return_policy", "product", "support_ticket"]
                      },
                      "sourceId": { "type": "string" },
                      "title": { "type": "string" },
                      "versionOrUpdatedAt": { "type": "string" }
                    },
                    "required": ["sourceType", "sourceId", "title", "versionOrUpdatedAt"],
                    "additionalProperties": false
                  }
                },
                "needsHumanSupport": { "type": "boolean" }
              },
              "required": ["answer", "citations", "needsHumanSupport"],
              "additionalProperties": false
            }
            """).RootElement.Clone();

    private sealed record OpenAiSupportOutput(
        string? Answer,
        IReadOnlyList<OpenAiSupportCitation>? Citations,
        bool NeedsHumanSupport);

    private sealed record OpenAiSupportCitation(
        string? SourceType,
        string? SourceId,
        string? Title,
        string? VersionOrUpdatedAt);

    private sealed record AttemptOutcome(AiSupportModelAnswer? Answer, bool MayRetry)
    {
        public static AttemptOutcome RetryableFailure { get; } = new(null, MayRetry: true);

        public static AttemptOutcome TerminalFailure { get; } = new(null, MayRetry: false);

        public static AttemptOutcome Success(AiSupportModelAnswer answer) =>
            new(answer, MayRetry: false);
    }

    private sealed class SourceIdentityComparer : IEqualityComparer<(string SourceType, string SourceId)>
    {
        public static SourceIdentityComparer Instance { get; } = new();

        public bool Equals(
            (string SourceType, string SourceId) x,
            (string SourceType, string SourceId) y) =>
            string.Equals(x.SourceType, y.SourceType, StringComparison.Ordinal) &&
            string.Equals(x.SourceId, y.SourceId, StringComparison.Ordinal);

        public int GetHashCode((string SourceType, string SourceId) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.SourceType),
                StringComparer.Ordinal.GetHashCode(value.SourceId));
    }
}
