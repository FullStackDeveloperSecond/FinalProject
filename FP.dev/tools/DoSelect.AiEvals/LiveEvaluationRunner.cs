using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace DoSelect.AiEvals;

public sealed record LiveEvaluationRunOptions(
    string ProjectRoot,
    string OutputDirectory,
    decimal StopAfterCostUsd);

public sealed record LiveEvaluationCaseResult(
    string CaseId,
    int Trial,
    string Feature,
    string ExpectedOutcome,
    string ActualStatus,
    bool SchemaValid,
    bool IntentFieldsMatch,
    bool ClarificationShapeMatch,
    bool CitationGrounded,
    bool DeterministicPass,
    bool HumanReviewRequired,
    long LatencyMilliseconds,
    string? Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    string? Answer,
    JsonElement? StructuredOutput,
    IReadOnlyList<string> Citations,
    string? ErrorCode);

public sealed record LiveEvaluationSummary(
    string RunId,
    string DatasetVersion,
    string CommitSha,
    string Split,
    int Trials,
    int PlannedModelRequests,
    int ExecutedCases,
    bool StoppedByCostLimit,
    decimal StopAfterCostUsd,
    decimal TotalCostUsd,
    int TotalInputTokens,
    int TotalOutputTokens,
    long ProductSearchP95LatencyMilliseconds,
    long AiSupportP95LatencyMilliseconds,
    decimal SchemaValidRate,
    decimal IntentFieldAccuracy,
    decimal CitationGroundingRate,
    decimal DeterministicPassRate,
    string Verdict,
    string OutputDirectory);

public sealed class LiveEvaluationRunner : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly OpenAiResponsesOptions _openAiOptions;
    private readonly HttpClient _productSearchHttpClient;
    private readonly HttpClient _supportHttpClient;
    private readonly OpenAiProductSearchClient _productSearchClient;
    private readonly OpenAiResponsesClient _supportClient;

    public LiveEvaluationRunner(OpenAiResponsesOptions openAiOptions)
        : this(
            openAiOptions,
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    public LiveEvaluationRunner(
        OpenAiResponsesOptions openAiOptions,
        HttpMessageHandler productSearchHandler,
        HttpMessageHandler supportHandler)
        : this(
            openAiOptions,
            new HttpClient(productSearchHandler) { Timeout = Timeout.InfiniteTimeSpan },
            new HttpClient(supportHandler) { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    private LiveEvaluationRunner(
        OpenAiResponsesOptions openAiOptions,
        HttpClient productSearchHttpClient,
        HttpClient supportHttpClient)
    {
        _openAiOptions = openAiOptions;
        _productSearchHttpClient = productSearchHttpClient;
        _supportHttpClient = supportHttpClient;
        _productSearchClient = new OpenAiProductSearchClient(
            _productSearchHttpClient,
            Options.Create(openAiOptions));
        _supportClient = new OpenAiResponsesClient(
            _supportHttpClient,
            Options.Create(openAiOptions));
    }

    public async Task<LiveEvaluationSummary> RunAsync(
        EvaluationPlan plan,
        LiveEvaluationRunOptions runOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runOptions);
        if (!plan.IsLiveReady)
        {
            throw new InvalidOperationException("The evaluation plan has unresolved preflight blockers.");
        }

        if (runOptions.StopAfterCostUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runOptions),
                "A positive stop-after cost must be supplied for every live run.");
        }

        using var fixtureStore = EvaluationFixtureStore.Load(Path.Combine(
            runOptions.ProjectRoot,
            "evals",
            "ai",
            "v1",
            "context-fixtures.v1.json"));
        var results = new List<LiveEvaluationCaseResult>();
        var totalCost = 0m;
        var stoppedByCost = false;

        foreach (var item in plan.LiveEligibleCases)
        {
            for (var trial = 1; trial <= plan.Trials; trial++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (totalCost >= runOptions.StopAfterCostUsd)
                {
                    stoppedByCost = true;
                    break;
                }

                LiveEvaluationCaseResult result;
                try
                {
                    result = item.Feature switch
                    {
                        "product_search" => await RunProductSearchAsync(
                            item,
                            trial,
                            fixtureStore,
                            cancellationToken),
                        "ai_support" => await RunSupportAsync(
                            item,
                            trial,
                            fixtureStore,
                            cancellationToken),
                        _ => throw new InvalidOperationException($"Unsupported feature '{item.Feature}'."),
                    };
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = Failed(item, trial, "TIMEOUT");
                }
                catch (HttpRequestException)
                {
                    result = Failed(item, trial, "HTTP_REQUEST_FAILED");
                }
                catch (JsonException)
                {
                    result = Failed(item, trial, "EVALUATION_DATA_INVALID");
                }

                results.Add(result);
                totalCost += result.CostUsd;
                if (totalCost >= runOptions.StopAfterCostUsd)
                {
                    stoppedByCost = true;
                    break;
                }
            }

            if (stoppedByCost)
            {
                break;
            }
        }

        return await WriteResultsAsync(plan, runOptions, results, stoppedByCost, cancellationToken);
    }

    public void Dispose()
    {
        _supportClient.Dispose();
        _productSearchHttpClient.Dispose();
        _supportHttpClient.Dispose();
    }

    private async Task<LiveEvaluationCaseResult> RunProductSearchAsync(
        EvaluationCasePlan item,
        int trial,
        EvaluationFixtureStore fixtures,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var metadata = fixtures.CreateProductSearchMetadata();
        var intentResult = await _productSearchClient.ParseIntentAsync(
            item.Message,
            SupportedLocale.ZhTw,
            metadata,
            cancellationToken);
        var usage = intentResult.Usage;
        AiProductSearchExplanationResult? explanation = null;
        var approvedCandidateIds = ReadStrings(item.Expected, "allowedCandidateIds");
        if (intentResult.Status == AiProductSearchModelStatus.Completed &&
            intentResult.Intent is not null &&
            approvedCandidateIds.Count > 0)
        {
            explanation = await _productSearchClient.ExplainAsync(
                intentResult.Intent,
                fixtures.CreateProductCards(approvedCandidateIds),
                SupportedLocale.ZhTw,
                cancellationToken);
            usage = AddUsage(usage, explanation.Usage);
        }

        stopwatch.Stop();
        var schemaValid = intentResult.Status == AiProductSearchModelStatus.Completed &&
            intentResult.Intent is not null &&
            (approvedCandidateIds.Count == 0 || explanation?.Status == AiProductSearchModelStatus.Completed);
        var intentMatches = IntentMatches(item.Expected.GetProperty("intentFields"), intentResult.Intent);
        var clarificationMatches = ClarificationMatches(
            item.Expected.GetProperty("clarification"),
            intentResult.Intent);
        var explanationValid = approvedCandidateIds.Count == 0 ||
            explanation?.Reasons.Count == approvedCandidateIds.Count;
        var answer = explanation is null
            ? null
            : string.Join("\n", explanation.Reasons.Select(reason => reason.Reason));
        var deterministicPass = schemaValid && intentMatches && clarificationMatches && explanationValid;

        return new LiveEvaluationCaseResult(
            item.CaseId,
            trial,
            item.Feature,
            item.ExpectedOutcome,
            intentResult.Status.ToString(),
            schemaValid,
            intentMatches,
            clarificationMatches,
            CitationGrounded: true,
            deterministicPass,
            HumanReviewRequired: HasRequiredAnswerPoints(item.Expected),
            stopwatch.ElapsedMilliseconds,
            usage?.Model,
            usage?.InputTokens ?? 0,
            usage?.OutputTokens ?? 0,
            CalculateCost(item.Feature, usage),
            answer,
            intentResult.Intent is null
                ? null
                : JsonSerializer.SerializeToElement(intentResult.Intent, JsonOptions),
            Citations: [],
            ErrorCode: schemaValid ? null : "MODEL_OUTPUT_INVALID");
    }

    private async Task<LiveEvaluationCaseResult> RunSupportAsync(
        EvaluationCasePlan item,
        int trial,
        EvaluationFixtureStore fixtures,
        CancellationToken cancellationToken)
    {
        var dataItems = fixtures.CreateSupportContext(item.FixtureIds, item.Message);
        var preparation = AiPromptEnvelopeFactory.TryCreateSupport(
            SupportedLocale.ZhTw,
            item.Message,
            dataItems);
        if (preparation.Envelope is null)
        {
            return Failed(item, trial, $"OUTBOUND_REJECTED_{preparation.Reason}");
        }

        var stopwatch = Stopwatch.StartNew();
        var answer = await _supportClient.GenerateAsync(preparation.Envelope, cancellationToken);
        stopwatch.Stop();
        var expectsAnswer = item.ExpectedOutcome == "answer_with_citations";
        var schemaValid = expectsAnswer
            ? answer.Status == AiSupportModelAnswerStatus.Answered && !string.IsNullOrWhiteSpace(answer.Answer)
            : answer.Status == AiSupportModelAnswerStatus.Unavailable;
        var expectedCitations = ReadStrings(item.Expected.GetProperty("citations"), "sourceIds");
        var actualCitations = answer.Citations.Select(citation => citation.SourceId).ToArray();
        var citationGrounded = expectedCitations.Count == 0
            ? actualCitations.Length == 0
            : expectedCitations.All(expected => actualCitations.Contains(expected, StringComparer.Ordinal));

        return new LiveEvaluationCaseResult(
            item.CaseId,
            trial,
            item.Feature,
            item.ExpectedOutcome,
            answer.Status.ToString(),
            schemaValid,
            IntentFieldsMatch: true,
            ClarificationShapeMatch: true,
            citationGrounded,
            DeterministicPass: schemaValid && citationGrounded,
            HumanReviewRequired: HasRequiredAnswerPoints(item.Expected),
            stopwatch.ElapsedMilliseconds,
            answer.Usage?.Model,
            answer.Usage?.InputTokens ?? 0,
            answer.Usage?.OutputTokens ?? 0,
            CalculateCost(item.Feature, answer.Usage),
            answer.Answer,
            StructuredOutput: null,
            actualCitations,
            ErrorCode: schemaValid ? null : "MODEL_OUTCOME_MISMATCH");
    }

    private async Task<LiveEvaluationSummary> WriteResultsAsync(
        EvaluationPlan plan,
        LiveEvaluationRunOptions runOptions,
        IReadOnlyList<LiveEvaluationCaseResult> results,
        bool stoppedByCost,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(runOptions.OutputDirectory);
        var runId = Path.GetFileName(Path.TrimEndingDirectorySeparator(runOptions.OutputDirectory));
        var commitSha = await ReadCommitShaAsync(runOptions.ProjectRoot, cancellationToken);
        var datasetVersion = ReadDatasetVersion(runOptions.ProjectRoot);
        var totalCost = results.Sum(result => result.CostUsd);
        var summary = new LiveEvaluationSummary(
            runId,
            datasetVersion,
            commitSha,
            plan.Split,
            plan.Trials,
            plan.PlannedModelRequests,
            results.Count,
            stoppedByCost,
            runOptions.StopAfterCostUsd,
            totalCost,
            results.Sum(result => result.InputTokens),
            results.Sum(result => result.OutputTokens),
            P95(results.Where(result => result.Feature == "product_search").Select(result => result.LatencyMilliseconds)),
            P95(results.Where(result => result.Feature == "ai_support").Select(result => result.LatencyMilliseconds)),
            Rate(results, result => result.SchemaValid),
            Rate(results.Where(result => result.Feature == "product_search"), result => result.IntentFieldsMatch),
            Rate(results.Where(result => result.Feature == "ai_support"), result => result.CitationGrounded),
            Rate(results, result => result.DeterministicPass),
            stoppedByCost || results.Count < plan.LiveEligibleCases.Count * plan.Trials
                ? "INCOMPLETE"
                : results.All(result => result.DeterministicPass) ? "PASS" : "FAIL",
            runOptions.OutputDirectory);

        var resultLines = results.Select(result => JsonSerializer.Serialize(result, JsonOptions));
        await File.WriteAllLinesAsync(
            Path.Combine(runOptions.OutputDirectory, "case-results.jsonl"),
            resultLines,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(runOptions.OutputDirectory, "summary.json"),
            JsonSerializer.Serialize(summary, JsonOptions),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(runOptions.OutputDirectory, "human-review.md"),
            CreateHumanReview(results),
            cancellationToken);
        return summary;
    }

    private decimal CalculateCost(string feature, AiSupportModelUsage? usage)
    {
        if (usage is null)
        {
            return 0m;
        }

        var inputRate = feature == "product_search"
            ? _openAiOptions.ProductSearchInputCostPerMillionTokens
            : _openAiOptions.SupportInputCostPerMillionTokens;
        var outputRate = feature == "product_search"
            ? _openAiOptions.ProductSearchOutputCostPerMillionTokens
            : _openAiOptions.SupportOutputCostPerMillionTokens;
        return decimal.Round(
            usage.InputTokens / 1_000_000m * inputRate +
            usage.OutputTokens / 1_000_000m * outputRate,
            6,
            MidpointRounding.AwayFromZero);
    }

    private static AiSupportModelUsage? AddUsage(
        AiSupportModelUsage? first,
        AiSupportModelUsage? second) =>
        second is null
            ? first
            : first is null
                ? second
                : new AiSupportModelUsage(
                    second.Model,
                    checked(first.InputTokens + second.InputTokens),
                    checked(first.OutputTokens + second.OutputTokens));

    private static bool IntentMatches(JsonElement expected, AiProductSearchIntent? actual)
    {
        if (actual is null)
        {
            return false;
        }

        var expectedIntent = expected.GetProperty("intent").GetString();
        var expectedPurposes = expected.GetProperty("purposes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => item is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        decimal? expectedMaximum = null;
        if (expected.TryGetProperty("budget.maxTwd", out var maximum) &&
            maximum.ValueKind == JsonValueKind.Number)
        {
            expectedMaximum = maximum.GetDecimal();
        }

        return string.Equals(actual.Intent.ToString(), expectedIntent, StringComparison.Ordinal) &&
            expectedPurposes.SetEquals(actual.Purposes) &&
            actual.Budget?.Maximum == expectedMaximum;
    }

    private static bool ClarificationMatches(JsonElement expected, AiProductSearchIntent? actual)
    {
        if (actual is null)
        {
            return false;
        }

        var required = expected.GetProperty("required").GetBoolean();
        var maximum = expected.GetProperty("maximumQuestions").GetInt32();
        return required
            ? actual.Clarifications.Count is > 0 && actual.Clarifications.Count <= maximum
            : actual.Clarifications.Count == 0;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement owner, string propertyName) =>
        owner.GetProperty(propertyName)
            .EnumerateArray()
            .Select(item => item.GetString() ?? throw new JsonException($"'{propertyName}' must contain strings."))
            .ToArray();

    private static bool HasRequiredAnswerPoints(JsonElement expected) =>
        expected.GetProperty("answer").GetProperty("requiredPoints").GetArrayLength() > 0;

    private static LiveEvaluationCaseResult Failed(
        EvaluationCasePlan item,
        int trial,
        string errorCode) =>
        new(
            item.CaseId,
            trial,
            item.Feature,
            item.ExpectedOutcome,
            "Failed",
            SchemaValid: false,
            IntentFieldsMatch: false,
            ClarificationShapeMatch: false,
            CitationGrounded: false,
            DeterministicPass: false,
            HumanReviewRequired: HasRequiredAnswerPoints(item.Expected),
            LatencyMilliseconds: 0,
            Model: null,
            InputTokens: 0,
            OutputTokens: 0,
            CostUsd: 0,
            Answer: null,
            StructuredOutput: null,
            Citations: [],
            errorCode);

    private static decimal Rate(
        IEnumerable<LiveEvaluationCaseResult> source,
        Func<LiveEvaluationCaseResult, bool> predicate)
    {
        var items = source.ToArray();
        return items.Length == 0
            ? 0m
            : decimal.Round(items.Count(predicate) / (decimal)items.Length, 4);
    }

    private static long P95(IEnumerable<long> source)
    {
        var ordered = source.Order().ToArray();
        return ordered.Length == 0
            ? 0
            : ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
    }

    private static string ReadDatasetVersion(string projectRoot)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            projectRoot,
            "evals",
            "ai",
            "v1",
            "manifest.json")));
        return manifest.RootElement.GetProperty("datasetVersion").GetString() ?? "unknown";
    }

    private static async Task<string> ReadCommitShaAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? output.Trim() : "uncommitted";
    }

    private static string CreateHumanReview(IReadOnlyList<LiveEvaluationCaseResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI Live evaluation human review");
        builder.AppendLine();
        builder.AppendLine("Only synthetic/deidentified evaluation inputs were used. Review required points, helpfulness, unsupported claims, refusal quality, and language quality.");
        builder.AppendLine();
        foreach (var result in results.Where(result => result.HumanReviewRequired))
        {
            builder.AppendLine($"## {result.CaseId} / trial {result.Trial}");
            builder.AppendLine();
            builder.AppendLine($"- Deterministic pass: `{result.DeterministicPass}`");
            builder.AppendLine($"- Model: `{result.Model ?? "unavailable"}`");
            builder.AppendLine($"- Answer: {result.Answer ?? "（無回答／已降級）"}");
            builder.AppendLine("- Human verdict: `pending`");
            builder.AppendLine("- Reviewer notes:");
            builder.AppendLine();
        }

        return builder.ToString();
    }
}

public sealed class EvaluationFixtureStore : IDisposable
{
    private readonly JsonDocument _document;
    private readonly string _fixtureVersion;
    private readonly IReadOnlyDictionary<string, JsonElement> _fixtures;

    private EvaluationFixtureStore(JsonDocument document)
    {
        _document = document;
        _fixtureVersion = document.RootElement.GetProperty("fixtureVersion").GetString()
            ?? throw new JsonException("Fixture version must be a string.");
        _fixtures = document.RootElement.GetProperty("fixtures")
            .EnumerateArray()
            .ToDictionary(
                fixture => fixture.GetProperty("fixtureId").GetString()!,
                fixture => fixture,
                StringComparer.Ordinal);
    }

    public static EvaluationFixtureStore Load(string path) =>
        new(JsonDocument.Parse(File.ReadAllText(path)));

    public AiProductSearchMetadata CreateProductSearchMetadata()
    {
        var categories = CatalogCandidates()
            .Select(candidate => NormalizeCategory(candidate.GetProperty("category").GetString()!))
            .Concat(["CPU", "MOTHERBOARD", "MEMORY", "GPU", "PSU", "CASE", "CPU_COOLER"])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var semanticKeys = typeof(CompatibilityCatalogContract.SemanticKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new AiProductSearchMetadata(
            categories,
            ["DOSELECT", "NOVACORE", "PIXELFORGE"],
            semanticKeys);
    }

    public IReadOnlyList<ProductCardDto> CreateProductCards(IReadOnlyList<string> candidateIds)
    {
        var candidates = CatalogCandidates().ToDictionary(
            candidate => candidate.GetProperty("id").GetString()!,
            StringComparer.Ordinal);
        return candidateIds.Select(id =>
        {
            var candidate = candidates[id];
            var category = candidate.GetProperty("category").GetString()!;
            var price = candidate.GetProperty("price").GetDecimal();
            return new ProductCardDto(
                StableGuid($"product:{id}"),
                StableGuid($"sku:{id}"),
                $"EVAL-{id.ToUpperInvariant()}",
                $"EVAL-{id.ToUpperInvariant()}-01",
                id,
                new ProductBrandRef("DOSELECT", "懂選"),
                new ProductCategoryRef(NormalizeCategory(category), category),
                new ProductPrice(price, null, "TWD"),
                ProductAvailabilityCodes.InStock,
                PrimaryImage: null,
                Badges: []);
        }).ToArray();
    }

    public IReadOnlyList<AiSupportContextItem> CreateSupportContext(
        IReadOnlyList<string> fixtureIds,
        string message)
    {
        var results = new List<AiSupportContextItem>();
        foreach (var fixtureId in fixtureIds)
        {
            var fixture = _fixtures[fixtureId];
            var sourceType = fixtureId switch
            {
                "policy.returns.v1" => "return_policy",
                "policy.payment-shipping.v1" or "faq.public.v1" => "faq",
                "orders.synthetic.v1" => "order",
                _ => null,
            };
            if (sourceType is null)
            {
                continue;
            }

            var content = fixtureId == "orders.synthetic.v1"
                ? CreateOwnedOrderContext(fixture, message)
                : fixture.GetProperty("description").GetString()!;
            results.Add(new AiSupportContextItem(
                sourceType,
                fixtureId,
                fixtureId,
                _fixtureVersion,
                content));
        }

        return results;
    }

    public void Dispose() => _document.Dispose();

    private IEnumerable<JsonElement> CatalogCandidates() =>
        _fixtures["catalog.synthetic.v1"].GetProperty("candidates").EnumerateArray();

    private static string CreateOwnedOrderContext(JsonElement fixture, string message)
    {
        var orders = fixture.GetProperty("orders")
            .EnumerateArray()
            .Where(order => order.GetProperty("owner").GetString() == "current_member")
            .Where(order => message.Contains(order.GetProperty("id").GetString()!, StringComparison.Ordinal))
            .Select(order => new
            {
                orderNumber = order.GetProperty("id").GetString(),
                status = order.GetProperty("status").GetString(),
                items = order.GetProperty("items").EnumerateArray().Select(item => item.GetString()).ToArray(),
            })
            .ToArray();
        return JsonSerializer.Serialize(new { orders });
    }

    private static string NormalizeCategory(string category) => category switch
    {
        "PrebuiltComputer" => "PREBUILT_COMPUTER",
        "CustomBuild" => "CUSTOM_BUILD",
        _ => string.Concat(category.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{character}" : character.ToString())).ToUpperInvariant(),
    };

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
