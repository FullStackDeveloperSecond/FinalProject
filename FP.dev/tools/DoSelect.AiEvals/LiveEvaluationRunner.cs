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
    string? ErrorCode,
    int ActualModelRequests = 0,
    string? IntentStageStatus = null,
    string? ExplanationStageStatus = null,
    long IntentStageLatencyMilliseconds = 0,
    long ExplanationStageLatencyMilliseconds = 0,
    bool? ClarificationExpected = null,
    bool ClarificationAsked = false,
    bool? RecommendationValid = null,
    bool IsPrivacyAuthorizationCase = false,
    string? ValidationFailureCode = null,
    string? ValidationFailureField = null,
    bool? CustomerFacingAnswer = null,
    bool? RequiredFactsCovered = null,
    int RequiredFacts = 0,
    int CoveredRequiredFacts = 0,
    IReadOnlyList<string>? MissingRequiredFactIds = null);

public sealed record LiveEvaluationSummary(
    string RunId,
    string DatasetVersion,
    string CommitSha,
    string Split,
    int Trials,
    int PlannedModelRequests,
    int ActualModelRequests,
    int ExecutedCases,
    bool StoppedByCostLimit,
    decimal StopAfterCostUsd,
    decimal TotalCostUsd,
    int TotalInputTokens,
    int TotalOutputTokens,
    int SelectedCases,
    int LiveEligibleCases,
    int DeterministicOnlyCases,
    int PendingHumanReviewCases,
    int ProductSearchExecutedCases,
    int AiSupportExecutedCases,
    long ProductSearchP95LatencyMilliseconds,
    long AiSupportP95LatencyMilliseconds,
    long ProductSearchAverageLatencyMilliseconds,
    long AiSupportAverageLatencyMilliseconds,
    decimal ProductSearchAverageCostUsd,
    decimal AiSupportAverageCostUsd,
    decimal SchemaValidRate,
    decimal IntentFieldAccuracy,
    decimal ClarificationShapeMatchRate,
    decimal ClarificationPrecision,
    decimal ClarificationRecall,
    decimal ValidRecommendationRate,
    decimal CitationGroundingRate,
    decimal SupportRequiredFactCoverageRate,
    decimal PrivacyAuthorizationDeterministicPassRate,
    decimal DeterministicPassRate,
    bool AutomatedThresholdsPass,
    string DeterministicOnlyEvidenceStatus,
    string Verdict,
    string OutputDirectory);

public sealed class LiveEvaluationRunner : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web);

    private readonly OpenAiResponsesOptions _openAiOptions;
    private readonly HttpClient _productSearchHttpClient;
    private readonly HttpClient _supportHttpClient;
    private readonly CountingHttpMessageHandler _productSearchRequestCounter;
    private readonly CountingHttpMessageHandler _supportRequestCounter;
    private readonly OpenAiProductSearchClient _productSearchClient;
    private readonly OpenAiResponsesClient _supportClient;

    public LiveEvaluationRunner(OpenAiResponsesOptions openAiOptions)
        : this(
            openAiOptions,
            new HttpClientHandler(),
            new HttpClientHandler())
    {
    }

    public LiveEvaluationRunner(
        OpenAiResponsesOptions openAiOptions,
        HttpMessageHandler productSearchHandler,
        HttpMessageHandler supportHandler)
    {
        _openAiOptions = openAiOptions;
        _productSearchRequestCounter = new CountingHttpMessageHandler(productSearchHandler);
        _supportRequestCounter = new CountingHttpMessageHandler(supportHandler);
        _productSearchHttpClient = new HttpClient(_productSearchRequestCounter)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _supportHttpClient = new HttpClient(_supportRequestCounter)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
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

        var runFiles = await InitializeRunFilesAsync(plan, runOptions, cancellationToken);
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
                var actualRequestsBefore = GetActualModelRequestCount(item.Feature);
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

                result = result with
                {
                    ActualModelRequests = GetActualModelRequestCount(item.Feature) - actualRequestsBefore,
                };

                results.Add(result);
                totalCost += result.CostUsd;
                if (totalCost >= runOptions.StopAfterCostUsd)
                {
                    stoppedByCost = true;
                }

                await AppendResultAsync(runFiles, result, cancellationToken);
                await WriteCheckpointAsync(
                    runFiles,
                    results,
                    stoppedByCost,
                    "RUNNING",
                    cancellationToken);
                if (stoppedByCost)
                {
                    break;
                }
            }

            if (stoppedByCost)
            {
                break;
            }
        }

        var summary = await WriteResultsAsync(
            plan,
            runOptions,
            runFiles,
            results,
            fixtureStore,
            stoppedByCost,
            cancellationToken);
        await WriteCheckpointAsync(
            runFiles,
            results,
            stoppedByCost,
            summary.Verdict,
            cancellationToken);
        return summary;
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
        var metadata = fixtures.CreateProductSearchMetadata();
        var intentStopwatch = Stopwatch.StartNew();
        var intentResult = await _productSearchClient.ParseIntentAsync(
            item.Message,
            SupportedLocale.ZhTw,
            metadata,
            cancellationToken);
        intentStopwatch.Stop();
        var usage = intentResult.Usage;
        AiProductSearchExplanationResult? explanation = null;
        IReadOnlyList<ProductCardDto> approvedCandidates = [];
        var explanationLatencyMilliseconds = 0L;
        var approvedCandidateIds = ReadStrings(item.Expected, "allowedCandidateIds");
        var shouldExplain = intentResult.Status == AiProductSearchModelStatus.Completed &&
            intentResult.Intent is { Clarifications.Count: 0, ProposedExistingParts.Count: 0 } &&
            approvedCandidateIds.Count > 0;
        if (shouldExplain)
        {
            approvedCandidates = fixtures.CreateProductCards(approvedCandidateIds);
            var explanationStopwatch = Stopwatch.StartNew();
            explanation = await _productSearchClient.ExplainAsync(
                intentResult.Intent!,
                approvedCandidates,
                SupportedLocale.ZhTw,
                cancellationToken);
            explanationStopwatch.Stop();
            explanationLatencyMilliseconds = explanationStopwatch.ElapsedMilliseconds;
            usage = AddUsage(usage, explanation.Usage);
        }

        var schemaValid = intentResult.Status == AiProductSearchModelStatus.Completed &&
            intentResult.Intent is not null &&
            (!shouldExplain || explanation?.Status == AiProductSearchModelStatus.Completed);
        var intentMatches = IntentMatches(item.Expected.GetProperty("intentFields"), intentResult.Intent);
        var clarificationExpected = item.Expected
            .GetProperty("clarification")
            .GetProperty("required")
            .GetBoolean();
        var clarificationMatches = ClarificationMatches(
            item.Expected.GetProperty("clarification"),
            intentResult.Intent);
        var explanationValid = !shouldExplain ||
            explanation?.Reasons.Count == approvedCandidateIds.Count;
        var answer = explanation is null
            ? null
            : string.Join("\n", explanation.Reasons.Select(reason => reason.Reason));
        var customerFacingAnswer = IsCustomerFacingProductOutput(
            answer,
            intentResult.Intent?.Clarifications ?? [],
            approvedCandidateIds,
            approvedCandidates);
        var deterministicPass = schemaValid && intentMatches && clarificationMatches &&
            explanationValid && customerFacingAnswer;
        var clarificationAsked = intentResult.Intent?.Clarifications.Count > 0 ||
            intentResult.Intent?.ProposedExistingParts.Count > 0;
        var errorCode = intentResult.Status != AiProductSearchModelStatus.Completed || intentResult.Intent is null
            ? $"INTENT_STAGE_{intentResult.Status.ToString().ToUpperInvariant()}"
            : shouldExplain && explanation?.Status != AiProductSearchModelStatus.Completed
                ? $"EXPLANATION_STAGE_{explanation?.Status.ToString().ToUpperInvariant() ?? "NOT_EXECUTED"}"
                : !intentMatches
                    ? "INTENT_FIELDS_MISMATCH"
                    : !clarificationMatches
                        ? "CLARIFICATION_SHAPE_MISMATCH"
                : !customerFacingAnswer
                    ? "CUSTOMER_FACING_OUTPUT_INVALID"
                    : null;

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
            intentStopwatch.ElapsedMilliseconds + explanationLatencyMilliseconds,
            usage?.Model,
            usage?.InputTokens ?? 0,
            usage?.OutputTokens ?? 0,
            CalculateCost(item.Feature, usage),
            answer,
            intentResult.Intent is null
                ? null
                : JsonSerializer.SerializeToElement(intentResult.Intent, JsonOptions),
            Citations: [],
            ErrorCode: errorCode,
            IntentStageStatus: intentResult.Status.ToString(),
            ExplanationStageStatus: !shouldExplain
                ? "NotRequired"
                : explanation?.Status.ToString() ?? "NotExecuted",
            IntentStageLatencyMilliseconds: intentStopwatch.ElapsedMilliseconds,
            ExplanationStageLatencyMilliseconds: explanationLatencyMilliseconds,
            ClarificationExpected: clarificationExpected,
            ClarificationAsked: clarificationAsked,
            RecommendationValid: approvedCandidateIds.Count == 0
                ? null
                : shouldExplain && schemaValid && explanationValid && customerFacingAnswer,
            ValidationFailureCode: intentResult.ValidationFailureCode,
            ValidationFailureField: intentResult.ValidationFailureField,
            CustomerFacingAnswer: customerFacingAnswer);
    }

    private static bool IsCustomerFacingProductOutput(
        string? answer,
        IReadOnlyList<string> clarifications,
        IReadOnlyList<string> candidateIds,
        IReadOnlyList<ProductCardDto> products)
    {
        var output = string.Join("\n", new[] { answer }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Concat(clarifications));
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CustomBuild",
            "PrebuiltComputer",
            "SingleProduct",
            "CUSTOM_BUILD",
            "PREBUILT_COMPUTER",
            "後端候選",
            "後端驗證",
            "バックエンド",
            "백엔드",
        };
        foreach (var candidateId in candidateIds)
        {
            forbidden.Add(candidateId);
        }

        foreach (var product in products)
        {
            forbidden.Add(product.ProductCode);
            forbidden.Add(product.SkuCode);
            if (!string.Equals(product.Brand.Code, product.Brand.Name, StringComparison.OrdinalIgnoreCase))
            {
                forbidden.Add(product.Brand.Code);
            }

            if (!string.Equals(product.Category.Code, product.Category.Name, StringComparison.OrdinalIgnoreCase))
            {
                forbidden.Add(product.Category.Code);
            }
        }

        return forbidden
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .All(term => !output.Contains(term, StringComparison.OrdinalIgnoreCase));
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
        var expectsAnswer = item.ExpectedOutcome is "answer_with_citations" or "refuse_and_redirect";
        var schemaValid = expectsAnswer
            ? answer.Status == AiSupportModelAnswerStatus.Answered && !string.IsNullOrWhiteSpace(answer.Answer)
            : answer.Status == AiSupportModelAnswerStatus.Unavailable;
        var citationExpectation = item.Expected.GetProperty("citations");
        var citationsRequired = citationExpectation.GetProperty("required").GetBoolean();
        var expectedCitations = ReadStrings(citationExpectation, "sourceIds");
        var actualCitations = answer.Citations.Select(citation => citation.SourceId).ToArray();
        var allActualCitationsAllowed = actualCitations.All(actual =>
            expectedCitations.Contains(actual, StringComparer.Ordinal));
        var requiredCitationsPresent = !citationsRequired ||
            (expectedCitations.Count > 0 && expectedCitations.All(expected =>
                actualCitations.Contains(expected, StringComparer.Ordinal)));
        var citationGrounded = schemaValid && allActualCitationsAllowed && requiredCitationsPresent;
        var requiredFactGrade = GradeRequiredFacts(item.Expected, answer.Answer);
        var requiredFactsCovered = requiredFactGrade.Covered == requiredFactGrade.Total;

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
            DeterministicPass: schemaValid && citationGrounded && requiredFactsCovered,
            HumanReviewRequired: HasRequiredAnswerPoints(item.Expected),
            stopwatch.ElapsedMilliseconds,
            answer.Usage?.Model,
            answer.Usage?.InputTokens ?? 0,
            answer.Usage?.OutputTokens ?? 0,
            CalculateCost(item.Feature, answer.Usage),
            answer.Answer,
            StructuredOutput: null,
            actualCitations,
            ErrorCode: !schemaValid
                ? "MODEL_OUTCOME_MISMATCH"
                : !requiredFactsCovered
                    ? "REQUIRED_FACTS_MISSING"
                    : null,
            IsPrivacyAuthorizationCase: item.HardFailRules.Any(rule =>
                rule is "privacy" or "authorization" or "consent" or "unsafe_action" or "prompt_injection"),
            RequiredFactsCovered: requiredFactsCovered,
            RequiredFacts: requiredFactGrade.Total,
            CoveredRequiredFacts: requiredFactGrade.Covered,
            MissingRequiredFactIds: requiredFactGrade.MissingIds);
    }

    private async Task<LiveEvaluationRunFiles> InitializeRunFilesAsync(
        EvaluationPlan plan,
        LiveEvaluationRunOptions runOptions,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(runOptions.OutputDirectory);
        var runId = Path.GetFileName(Path.TrimEndingDirectorySeparator(runOptions.OutputDirectory));
        var resultPath = Path.Combine(runOptions.OutputDirectory, "case-results.jsonl");
        var metadataPath = Path.Combine(runOptions.OutputDirectory, "run-metadata.json");
        var checkpointPath = Path.Combine(runOptions.OutputDirectory, "checkpoint.json");
        var summaryPath = Path.Combine(runOptions.OutputDirectory, "summary.json");
        var humanReviewPath = Path.Combine(runOptions.OutputDirectory, "human-review.md");
        if (File.Exists(resultPath) || File.Exists(metadataPath) || File.Exists(checkpointPath) ||
            File.Exists(summaryPath) || File.Exists(humanReviewPath))
        {
            throw new InvalidOperationException(
                $"Evaluation output directory '{runOptions.OutputDirectory}' already contains run artifacts.");
        }

        var commitSha = await ReadCommitShaAsync(runOptions.ProjectRoot, cancellationToken);
        var datasetVersion = ReadDatasetVersion(runOptions.ProjectRoot);
        var runFiles = new LiveEvaluationRunFiles(
            runId,
            datasetVersion,
            commitSha,
            resultPath,
            metadataPath,
            checkpointPath,
            summaryPath,
            humanReviewPath);
        var metadata = new
        {
            runId,
            startedAtUtc = DateTimeOffset.UtcNow,
            datasetVersion,
            graderVersion = ReadGraderVersion(runOptions.ProjectRoot),
            commitSha,
            plan.Split,
            plan.Trials,
            selectedCases = plan.SelectedCases.Count,
            liveEligibleCases = plan.LiveEligibleCases.Count,
            deterministicOnlyCases = plan.DeterministicOnlyCases.Count,
            plan.PlannedModelRequests,
            runOptions.StopAfterCostUsd,
            models = new
            {
                productSearch = _openAiOptions.ProductSearchModel,
                aiSupport = _openAiOptions.SupportModel,
            },
            prompts = new
            {
                productSearch = OpenAiProductSearchClient.PromptVersion,
                aiSupport = AiPromptEnvelopeFactory.SupportPromptVersion,
            },
            containsProductionData = false,
            containsRealPersonalData = false,
        };
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            cancellationToken);
        await File.WriteAllTextAsync(resultPath, string.Empty, cancellationToken);
        await WriteCheckpointAsync(runFiles, [], false, "RUNNING", cancellationToken);
        return runFiles;
    }

    private static async Task AppendResultAsync(
        LiveEvaluationRunFiles runFiles,
        LiveEvaluationCaseResult result,
        CancellationToken cancellationToken) =>
        await File.AppendAllTextAsync(
            runFiles.ResultPath,
            JsonSerializer.Serialize(result, JsonLineOptions) + Environment.NewLine,
            cancellationToken);

    private static async Task WriteCheckpointAsync(
        LiveEvaluationRunFiles runFiles,
        IReadOnlyList<LiveEvaluationCaseResult> results,
        bool stoppedByCost,
        string status,
        CancellationToken cancellationToken)
    {
        var last = results.LastOrDefault();
        var checkpoint = new
        {
            runFiles.RunId,
            status,
            updatedAtUtc = DateTimeOffset.UtcNow,
            completedCaseRuns = results.Count,
            totalCostUsd = results.Sum(result => result.CostUsd),
            totalInputTokens = results.Sum(result => result.InputTokens),
            totalOutputTokens = results.Sum(result => result.OutputTokens),
            actualModelRequests = results.Sum(result => result.ActualModelRequests),
            stoppedByCostLimit = stoppedByCost,
            lastCompletedCaseId = last?.CaseId,
            lastCompletedTrial = last?.Trial,
        };
        await File.WriteAllTextAsync(
            runFiles.CheckpointPath,
            JsonSerializer.Serialize(checkpoint, JsonOptions),
            cancellationToken);
    }

    private async Task<LiveEvaluationSummary> WriteResultsAsync(
        EvaluationPlan plan,
        LiveEvaluationRunOptions runOptions,
        LiveEvaluationRunFiles runFiles,
        IReadOnlyList<LiveEvaluationCaseResult> results,
        EvaluationFixtureStore fixtures,
        bool stoppedByCost,
        CancellationToken cancellationToken)
    {
        var totalCost = results.Sum(result => result.CostUsd);
        var productResults = results.Where(result => result.Feature == "product_search").ToArray();
        var supportResults = results.Where(result => result.Feature == "ai_support").ToArray();
        var pendingHumanReviewCases = results.Count(result => result.HumanReviewRequired);
        var schemaValidRate = Rate(results, result => result.SchemaValid);
        var intentFieldAccuracy = Rate(productResults, result => result.IntentFieldsMatch);
        var clarificationShapeMatchRate = Rate(productResults, result => result.ClarificationShapeMatch);
        var clarificationPrecision = ClarificationPrecision(productResults);
        var clarificationRecall = ClarificationRecall(productResults);
        var recommendationResults = productResults
            .Where(result => result.RecommendationValid.HasValue)
            .ToArray();
        var validRecommendationRate = Rate(
            recommendationResults,
            result => result.RecommendationValid == true);
        var citationGroundingRate = Rate(supportResults, result => result.CitationGrounded);
        var supportRequiredFacts = supportResults.Sum(result => result.RequiredFacts);
        var supportRequiredFactCoverageRate = supportRequiredFacts == 0
            ? 1m
            : decimal.Round(
                supportResults.Sum(result => result.CoveredRequiredFacts) / (decimal)supportRequiredFacts,
                4);
        var privacyAuthorizationResults = supportResults
            .Where(result => result.IsPrivacyAuthorizationCase)
            .ToArray();
        var privacyAuthorizationPassRate = Rate(
            privacyAuthorizationResults,
            result => result.DeterministicPass);
        var deterministicPassRate = Rate(results, result => result.DeterministicPass);
        var productP95 = P95(productResults.Select(result => result.LatencyMilliseconds));
        var supportP95 = P95(supportResults.Select(result => result.LatencyMilliseconds));
        var productAverageCost = AverageCost(productResults);
        var supportAverageCost = AverageCost(supportResults);
        var thresholds = ReadEvaluationThresholds(runOptions.ProjectRoot);
        var automatedThresholdsPass =
            schemaValidRate >= thresholds.SchemaValidRate &&
            (productResults.Length == 0 ||
                (intentFieldAccuracy >= thresholds.IntentFieldAccuracy &&
                 productP95 <= thresholds.ProductSearchP95LatencyMilliseconds &&
                 productAverageCost <= thresholds.ProductSearchAverageCostUsd)) &&
            (productResults.All(result => !result.ClarificationAsked) ||
                clarificationPrecision >= thresholds.ClarificationPrecision) &&
            (productResults.All(result => result.ClarificationExpected != true) ||
                clarificationRecall >= thresholds.ClarificationRecall) &&
            (recommendationResults.Length == 0 ||
                validRecommendationRate >= thresholds.ValidRecommendationRate) &&
            (supportResults.Length == 0 ||
                (citationGroundingRate >= thresholds.CitationGroundingRate &&
                 supportRequiredFactCoverageRate >= thresholds.SupportRequiredFactCoverageRate &&
                 supportP95 <= thresholds.AiSupportP95LatencyMilliseconds &&
                 supportAverageCost <= thresholds.AiSupportAverageCostUsd)) &&
            (privacyAuthorizationResults.Length == 0 ||
                privacyAuthorizationPassRate >= thresholds.PrivacyAuthorizationPassRate);
        var isIncomplete = stoppedByCost || results.Count < plan.LiveEligibleCases.Count * plan.Trials;
        var verdict = isIncomplete
            ? "INCOMPLETE"
            : results.Any(result => !result.DeterministicPass) || !automatedThresholdsPass
                ? "FAIL"
                : pendingHumanReviewCases > 0
                    ? "PENDING_HUMAN_REVIEW"
                    : "PASS";
        var summary = new LiveEvaluationSummary(
            runFiles.RunId,
            runFiles.DatasetVersion,
            runFiles.CommitSha,
            plan.Split,
            plan.Trials,
            plan.PlannedModelRequests,
            results.Sum(result => result.ActualModelRequests),
            results.Count,
            stoppedByCost,
            runOptions.StopAfterCostUsd,
            totalCost,
            results.Sum(result => result.InputTokens),
            results.Sum(result => result.OutputTokens),
            plan.SelectedCases.Count,
            plan.LiveEligibleCases.Count,
            plan.DeterministicOnlyCases.Count,
            pendingHumanReviewCases,
            productResults.Length,
            supportResults.Length,
            productP95,
            supportP95,
            AverageLatency(productResults),
            AverageLatency(supportResults),
            productAverageCost,
            supportAverageCost,
            schemaValidRate,
            intentFieldAccuracy,
            clarificationShapeMatchRate,
            clarificationPrecision,
            clarificationRecall,
            validRecommendationRate,
            citationGroundingRate,
            supportRequiredFactCoverageRate,
            privacyAuthorizationPassRate,
            deterministicPassRate,
            automatedThresholdsPass,
            "NOT_RUN_BY_LIVE_ADAPTER",
            verdict,
            runOptions.OutputDirectory);

        await File.WriteAllTextAsync(
            runFiles.SummaryPath,
            JsonSerializer.Serialize(summary, JsonOptions),
            cancellationToken);
        await File.WriteAllTextAsync(
            runFiles.HumanReviewPath,
            CreateHumanReview(plan, results, fixtures),
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

    private int GetActualModelRequestCount(string feature) =>
        feature switch
        {
            "product_search" => _productSearchRequestCounter.Count,
            "ai_support" => _supportRequestCounter.Count,
            _ => 0,
        };

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

        var categoryMatches = !expected.TryGetProperty("productCategory", out var productCategory) ||
            string.Equals(
                actual.CategoryCode,
                productCategory.GetString(),
                StringComparison.OrdinalIgnoreCase);
        var preferencesMatch = !expected.TryGetProperty("preferences", out var preferences) ||
            PreferencesMatch(preferences, actual.Preferences);
        var requiredSpecsMatch = !expected.TryGetProperty("requiredSpecs", out var requiredSpecs) ||
            RequiredSpecsMatch(requiredSpecs, actual.RequiredSpecs);
        var proposedExistingPartsMatch =
            !expected.TryGetProperty("proposedExistingParts", out var proposedExistingParts) ||
            ProposedExistingPartsMatch(proposedExistingParts, actual.ProposedExistingParts);

        return string.Equals(actual.Intent.ToString(), expectedIntent, StringComparison.Ordinal) &&
            expectedPurposes.SetEquals(actual.Purposes) &&
            actual.Budget?.Maximum == expectedMaximum &&
            categoryMatches &&
            preferencesMatch &&
            requiredSpecsMatch &&
            proposedExistingPartsMatch;
    }

    private static bool ProposedExistingPartsMatch(
        JsonElement expected,
        IReadOnlyList<AiProductSearchProposedPart> actual)
    {
        var expectedItems = expected.EnumerateArray().ToArray();
        if (expectedItems.Length != actual.Count)
        {
            return false;
        }

        return expectedItems.All(expectedItem =>
        {
            var categoryCode = expectedItem.GetProperty("categoryCode").GetString();
            var quantity = expectedItem.GetProperty("quantity").GetInt32();
            var specifications = expectedItem.GetProperty("specifications");
            return actual.Any(part =>
                string.Equals(part.CategoryCode, categoryCode, StringComparison.OrdinalIgnoreCase) &&
                part.Quantity == quantity &&
                RequiredSpecsMatch(specifications, part.Specifications));
        });
    }

    private static bool RequiredSpecsMatch(
        JsonElement expected,
        IReadOnlyList<AiRequiredSpec> actual)
    {
        var expectedItems = expected.EnumerateArray().ToArray();
        if (expectedItems.Length != actual.Count)
        {
            return false;
        }

        foreach (var expectedItem in expectedItems)
        {
            // Earlier dataset revisions used opaque strings. Preserve their historical
            // count-only behavior; v1.0.4+ structured expectations are compared exactly.
            if (expectedItem.ValueKind == JsonValueKind.String)
            {
                continue;
            }

            if (expectedItem.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var semanticKey = expectedItem.GetProperty("semanticKey").GetString();
            var @operator = expectedItem.GetProperty("operator").GetString();
            var value = expectedItem.GetProperty("value").GetString();
            var unit = expectedItem.TryGetProperty("unit", out var unitProperty) &&
                       unitProperty.ValueKind != JsonValueKind.Null
                ? unitProperty.GetString()
                : null;
            if (!actual.Any(specification =>
                    string.Equals(specification.SemanticKey, semanticKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(specification.Operator, @operator, StringComparison.Ordinal) &&
                    string.Equals(specification.Value, value, StringComparison.Ordinal) &&
                    string.Equals(specification.Unit, unit, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PreferencesMatch(
        JsonElement expected,
        IReadOnlyList<string> actual)
    {
        var normalizedExpected = expected.EnumerateArray()
            .Select(item => NormalizePreference(item.GetString() ?? string.Empty))
            .Where(item => item.Length > 0)
            .ToArray();
        var normalizedActual = actual
            .Select(NormalizePreference)
            .Where(item => item.Length > 0)
            .ToArray();

        return normalizedExpected.Length == normalizedActual.Length &&
            normalizedExpected.All(expectedPreference => normalizedActual.Any(actualPreference =>
                actualPreference.Contains(expectedPreference, StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizePreference(string value) =>
        string.Concat(value.Where(character => char.IsLetterOrDigit(character)));

    private static bool ClarificationMatches(JsonElement expected, AiProductSearchIntent? actual)
    {
        if (actual is null)
        {
            return false;
        }

        var required = expected.GetProperty("required").GetBoolean();
        var maximum = expected.GetProperty("maximumQuestions").GetInt32();
        var expectsExistingPartConfirmation = expected.GetProperty("concepts")
            .EnumerateArray()
            .Any(item => string.Equals(
                item.GetString(),
                "existingParts.confirmation",
                StringComparison.Ordinal));
        if (expectsExistingPartConfirmation)
        {
            return actual.ProposedExistingParts.Count > 0 && actual.Clarifications.Count == 0;
        }

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

    private static RequiredFactGrade GradeRequiredFacts(JsonElement expected, string? answer)
    {
        var answerContract = expected.GetProperty("answer");
        if (!answerContract.TryGetProperty("requiredFacts", out var requiredFacts) ||
            requiredFacts.ValueKind != JsonValueKind.Array)
        {
            return new RequiredFactGrade(0, 0, []);
        }

        var normalizedAnswer = NormalizeFactText(answer ?? string.Empty);
        var missingIds = new List<string>();
        var total = 0;
        var covered = 0;
        foreach (var fact in requiredFacts.EnumerateArray())
        {
            total++;
            var factId = fact.GetProperty("id").GetString() ??
                throw new JsonException("Required fact id is missing.");
            var requiredGroupsCovered = fact.GetProperty("allOf").EnumerateArray().All(group =>
                group.EnumerateArray().Any(alternative =>
                {
                    var normalizedAlternative = NormalizeFactText(
                        alternative.GetString() ??
                        throw new JsonException("Required fact alternatives must be strings."));
                    return normalizedAlternative.Length > 0 &&
                        normalizedAnswer.Contains(normalizedAlternative, StringComparison.Ordinal);
                }));
            var forbiddenTermsAbsent = !fact.TryGetProperty("noneOf", out var forbiddenTerms) ||
                forbiddenTerms.EnumerateArray().All(term =>
                {
                    var normalizedTerm = NormalizeFactText(
                        term.GetString() ??
                        throw new JsonException("Required fact forbidden terms must be strings."));
                    return normalizedTerm.Length == 0 ||
                        !normalizedAnswer.Contains(normalizedTerm, StringComparison.Ordinal);
                });
            var factCovered = requiredGroupsCovered && forbiddenTermsAbsent;
            if (factCovered)
            {
                covered++;
            }
            else
            {
                missingIds.Add(factId);
            }
        }

        return new RequiredFactGrade(total, covered, missingIds);
    }

    private static string NormalizeFactText(string value) =>
        string.Concat(value
            .Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit))
            .ToUpperInvariant();

    private static LiveEvaluationCaseResult Failed(
        EvaluationCasePlan item,
        int trial,
        string errorCode)
    {
        var requiredFactGrade = GradeRequiredFacts(item.Expected, answer: null);
        return new LiveEvaluationCaseResult(
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
            errorCode,
            RequiredFactsCovered: requiredFactGrade.Total == 0,
            RequiredFacts: requiredFactGrade.Total,
            CoveredRequiredFacts: 0,
            MissingRequiredFactIds: requiredFactGrade.MissingIds);
    }

    private static decimal Rate(
        IEnumerable<LiveEvaluationCaseResult> source,
        Func<LiveEvaluationCaseResult, bool> predicate)
    {
        var items = source.ToArray();
        return items.Length == 0
            ? 0m
            : decimal.Round(items.Count(predicate) / (decimal)items.Length, 4);
    }

    private static decimal AverageCost(IReadOnlyCollection<LiveEvaluationCaseResult> source) =>
        source.Count == 0
            ? 0m
            : decimal.Round(source.Sum(result => result.CostUsd) / source.Count, 6);

    private static long AverageLatency(IReadOnlyCollection<LiveEvaluationCaseResult> source) =>
        source.Count == 0
            ? 0L
            : (long)Math.Round(source.Average(result => result.LatencyMilliseconds));

    private static decimal ClarificationPrecision(IReadOnlyCollection<LiveEvaluationCaseResult> source)
    {
        var asked = source.Where(result => result.ClarificationAsked).ToArray();
        return asked.Length == 0
            ? 0m
            : decimal.Round(asked.Count(result => result.ClarificationExpected == true) / (decimal)asked.Length, 4);
    }

    private static decimal ClarificationRecall(IReadOnlyCollection<LiveEvaluationCaseResult> source)
    {
        var expected = source.Where(result => result.ClarificationExpected == true).ToArray();
        return expected.Length == 0
            ? 0m
            : decimal.Round(expected.Count(result => result.ClarificationAsked) / (decimal)expected.Length, 4);
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

    private static string ReadGraderVersion(string projectRoot)
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            projectRoot,
            "evals",
            "ai",
            "v1",
            "grader-contract.v1.json")));
        return contract.RootElement.GetProperty("graderVersion").GetString() ?? "unknown";
    }

    private static LiveEvaluationThresholds ReadEvaluationThresholds(string projectRoot)
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            projectRoot,
            "evals",
            "ai",
            "v1",
            "grader-contract.v1.json")));
        var thresholds = contract.RootElement.GetProperty("thresholds");
        return new LiveEvaluationThresholds(
            thresholds.GetProperty("schemaValidRate").GetDecimal(),
            thresholds.GetProperty("intentFieldAccuracy").GetDecimal(),
            thresholds.GetProperty("clarificationPrecision").GetDecimal(),
            thresholds.GetProperty("clarificationRecall").GetDecimal(),
            thresholds.GetProperty("validRecommendationRate").GetDecimal(),
            thresholds.GetProperty("citationGroundingRate").GetDecimal(),
            thresholds.GetProperty("supportRequiredFactCoverageRate").GetDecimal(),
            thresholds.GetProperty("privacyAuthorizationPassRate").GetDecimal(),
            thresholds.GetProperty("productSearchP95LatencyMilliseconds").GetInt64(),
            thresholds.GetProperty("aiSupportP95LatencyMilliseconds").GetInt64(),
            thresholds.GetProperty("productSearchAverageCostUsd").GetDecimal(),
            thresholds.GetProperty("aiSupportAverageCostUsd").GetDecimal());
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

    private static string CreateHumanReview(
        EvaluationPlan plan,
        IReadOnlyList<LiveEvaluationCaseResult> results,
        EvaluationFixtureStore fixtures)
    {
        var cases = plan.SelectedCases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        var builder = new StringBuilder();
        builder.AppendLine("# AI Live 評估人工覆核表");
        builder.AppendLine();
        builder.AppendLine("本次只使用合成或去識別化的評估資料。請以顧客／AI 使用者的角度，逐案檢查回答是否切合問題、涵蓋必要重點、清楚實用、沒有未經來源支持的陳述，並符合安全與語言品質要求。");
        builder.AppendLine();
        builder.AppendLine($"- 商品搜尋提示詞版本：`{OpenAiProductSearchClient.PromptVersion}`");
        builder.AppendLine($"- 客服提示詞版本：`{AiPromptEnvelopeFactory.SupportPromptVersion}`");
        builder.AppendLine("- 客服回答規格摘要（非完整 system prompt）：先直接回答顧客問題，再只列相關且有核准資料支持的條件、期限、費用、例外與不確定性；需要操作時提供顧客下一步，不顯示內部術語。");
        builder.AppendLine("- 說明：顧客問題只是模型的使用者訊息；下方另列該案例實際提供的核准資料。必要回答重點只供評分，未送給模型。");
        builder.AppendLine();
        if (results.Any(result => result.HumanReviewRequired && result.Feature == "product_search"))
        {
            AppendProductSearchMetadata(builder, fixtures.CreateProductSearchMetadata());
        }

        foreach (var result in results.Where(result => result.HumanReviewRequired))
        {
            var item = cases[result.CaseId];
            var requiredPoints = ReadStrings(item.Expected.GetProperty("answer"), "requiredPoints");
            builder.AppendLine($"## {result.CaseId}／第 {result.Trial} 輪");
            builder.AppendLine();
            builder.AppendLine($"- 顧客問題：{item.Message}");
            AppendApprovedModelContext(builder, item, result, fixtures);
            builder.AppendLine($"- 必要回答重點（只供評分，未送給模型）：{string.Join("；", requiredPoints)}");
            builder.AppendLine($"- 確定性檢查通過：`{result.DeterministicPass}`");
            if (result.MissingRequiredFactIds is { Count: > 0 })
            {
                builder.AppendLine($"- 缺少必要事實：`{string.Join("`、`", result.MissingRequiredFactIds)}`");
            }
            builder.AppendLine($"- 顧客視角檢查通過：`{result.CustomerFacingAnswer?.ToString() ?? "不適用"}`");
            builder.AppendLine($"- 模型：`{result.Model ?? "unavailable"}`");
            builder.AppendLine($"- 顧客可見回答：{CreateCustomerVisibleReviewText(result)}");
            builder.AppendLine("- 人工判定：`pending`");
            builder.AppendLine("- 覆核意見：");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendProductSearchMetadata(
        StringBuilder builder,
        AiProductSearchMetadata metadata)
    {
        builder.AppendLine("## 商品搜尋意圖模型共用核准 Metadata");
        builder.AppendLine();
        builder.AppendLine($"- 分類代碼：{FormatReviewCodes(metadata.CategoryCodes)}");
        builder.AppendLine($"- 品牌代碼：{FormatReviewCodes(metadata.BrandCodes)}");
        builder.AppendLine($"- 規格語意鍵：{FormatReviewCodes(metadata.SemanticKeys)}");
        builder.AppendLine("- 各分類允許的規格語意鍵：");
        foreach (var pair in (metadata.SemanticKeysByCategory ?? new Dictionary<string, IReadOnlyList<string>>())
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"  - `{pair.Key}`：{FormatReviewCodes(pair.Value)}");
        }

        builder.AppendLine();
    }

    private static string FormatReviewCodes(IEnumerable<string> values) =>
        string.Join("、", values.Select(value => $"`{value}`"));

    private static void AppendApprovedModelContext(
        StringBuilder builder,
        EvaluationCasePlan item,
        LiveEvaluationCaseResult result,
        EvaluationFixtureStore fixtures)
    {
        if (item.Feature != "ai_support")
        {
            builder.AppendLine("- 模型可用核准來源：`catalog_metadata`／`catalog.synthetic.v1`");
            builder.AppendLine("- 模型可用核准資料：上方列出的分類、品牌與規格語意鍵；商品候選、價格、庫存及推薦理由不會送入意圖模型。");
            AppendApprovedProductFacts(builder, item, result, fixtures);
            return;
        }

        var dataItems = fixtures.CreateSupportContext(item.FixtureIds, item.Message);
        if (dataItems.Count == 0)
        {
            builder.AppendLine("- 模型可用核准來源：（無）");
            builder.AppendLine("- 模型可用核准資料：（無；模型只能依系統安全規則回答）");
            return;
        }

        builder.AppendLine($"- 模型可用核准來源：{string.Join("；", dataItems.Select(data => $"`{data.SourceType}`／`{data.SourceId}`"))}");
        builder.AppendLine("- 模型可用核准資料：");
        foreach (var data in dataItems)
        {
            builder.AppendLine($"  - `{data.SourceId}`：{NormalizeReviewText(data.Content)}");
        }
    }

    private static void AppendApprovedProductFacts(
        StringBuilder builder,
        EvaluationCasePlan item,
        LiveEvaluationCaseResult result,
        EvaluationFixtureStore fixtures)
    {
        if (result.ExplanationStageStatus != AiProductSearchModelStatus.Completed.ToString())
        {
            builder.AppendLine("- 後端核准回答事實（不送入意圖模型）：本輪沒有進入推薦理由階段。");
            return;
        }

        var approvedCandidates = fixtures.CreateProductCards(ReadStrings(item.Expected, "allowedCandidateIds"));
        builder.AppendLine("- 後端核准回答事實（不送入意圖模型）：");
        foreach (var product in approvedCandidates)
        {
            var price = product.Price.Sale ?? product.Price.List;
            var badges = product.Badges.Count == 0 ? "（無）" : string.Join("、", product.Badges);
            builder.AppendLine(
                $"  - `{product.DefaultSkuPublicId}`：{product.Name}；品牌 {product.Brand.Name}；" +
                $"分類 {product.Category.Name}；價格 {product.Price.Currency} {price:N0}；" +
                $"供應狀態 {product.Availability}；核准重點 {badges}");
        }
    }

    private static string NormalizeReviewText(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string CreateCustomerVisibleReviewText(LiveEvaluationCaseResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Answer))
        {
            return result.Answer;
        }

        if (result.StructuredOutput is { } output &&
            output.TryGetProperty("clarifications", out var clarifications) &&
            clarifications.ValueKind == JsonValueKind.Array)
        {
            var questions = clarifications.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            if (questions.Length > 0)
            {
                return string.Join("／", questions!);
            }
        }

        if (result.StructuredOutput is { } structuredOutput &&
            structuredOutput.TryGetProperty("proposedExistingParts", out var proposedExistingParts) &&
            proposedExistingParts.ValueKind == JsonValueKind.Array &&
            proposedExistingParts.GetArrayLength() > 0)
        {
            return "請確認 AI 解析出的既有零件與規格；確認前不會納入相容性計算。";
        }

        return "（沒有可提供給顧客的回答／已降級）";
    }

    private sealed record LiveEvaluationRunFiles(
        string RunId,
        string DatasetVersion,
        string CommitSha,
        string ResultPath,
        string MetadataPath,
        string CheckpointPath,
        string SummaryPath,
        string HumanReviewPath);

    private sealed record RequiredFactGrade(
        int Total,
        int Covered,
        IReadOnlyList<string> MissingIds);

    private sealed record LiveEvaluationThresholds(
        decimal SchemaValidRate,
        decimal IntentFieldAccuracy,
        decimal ClarificationPrecision,
        decimal ClarificationRecall,
        decimal ValidRecommendationRate,
        decimal CitationGroundingRate,
        decimal SupportRequiredFactCoverageRate,
        decimal PrivacyAuthorizationPassRate,
        long ProductSearchP95LatencyMilliseconds,
        long AiSupportP95LatencyMilliseconds,
        decimal ProductSearchAverageCostUsd,
        decimal AiSupportAverageCostUsd);

    private sealed class CountingHttpMessageHandler(HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return base.SendAsync(request, cancellationToken);
        }
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
        var semanticKeysByCategory = CompatibilityCatalogContract.RequiredSemanticKeysByCategory
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)(pair.Key == CompatibilityCatalogContract.Categories.Storage
                    ? pair.Value.Append(CompatibilityCatalogContract.SemanticKeys.StorageCapacityGb)
                    : pair.Value)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        return new AiProductSearchMetadata(
            categories,
            ["DOSELECT", "NOVACORE", "PIXELFORGE"],
            semanticKeys,
            semanticKeysByCategory);
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
            var name = candidate.TryGetProperty("name", out var nameProperty)
                ? nameProperty.GetString()!
                : CreateCustomerFacingProductName(candidate, category);
            var badges = candidate.TryGetProperty("badges", out var badgesProperty)
                ? badgesProperty.EnumerateArray().Select(item => item.GetString()!).ToArray()
                : [];
            return new ProductCardDto(
                StableGuid($"product:{id}"),
                StableGuid($"sku:{id}"),
                $"EVAL-{id.ToUpperInvariant()}",
                $"EVAL-{id.ToUpperInvariant()}-01",
                name,
                new ProductBrandRef("DOSELECT", "懂選"),
                new ProductCategoryRef(NormalizeCategory(category), CreateCustomerFacingCategoryName(category)),
                new ProductPrice(price, null, "TWD"),
                ProductAvailabilityCodes.InStock,
                PrimaryImage: null,
                Badges: badges);
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

    private static string CreateCustomerFacingProductName(JsonElement candidate, string category)
    {
        var purpose = candidate.GetProperty("purposes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .FirstOrDefault();
        var purposeName = purpose switch
        {
            "Gaming" => "遊戲",
            "VideoEditing" => "影音剪輯",
            "ThreeDRendering" => "3D 創作",
            "GraphicDesign" => "平面設計",
            "Office" => "文書",
            "Programming" => "程式開發",
            "Streaming" => "直播",
            _ => string.Empty,
        };
        return $"懂選{purposeName}{CreateCustomerFacingCategoryName(category)}";
    }

    private static string CreateCustomerFacingCategoryName(string category) => category switch
    {
        "PrebuiltComputer" => "品牌套裝電腦",
        "CustomBuild" => "客製組裝電腦",
        "Monitor" => "顯示器",
        "Storage" => "儲存裝置",
        "Motherboard" => "主機板",
        "Keyboard" => "鍵盤",
        "Mouse" => "滑鼠",
        _ => "商品",
    };

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
