using System.Text.Json;
using DoSelect.Infrastructure.Ai;

namespace DoSelect.AiEvals;

public sealed record EvaluationCasePlan(
    string CaseId,
    string Split,
    string Feature,
    string ServiceCondition,
    string ModelCall,
    string AnnotationStatus,
    string ExpectedOutcome,
    int ModelRequestsPerTrial,
    string Message,
    IReadOnlyList<string> FixtureIds,
    JsonElement Expected);

public sealed record EvaluationPlan(
    string Split,
    int Trials,
    IReadOnlyList<EvaluationCasePlan> SelectedCases,
    IReadOnlyList<EvaluationCasePlan> LiveEligibleCases,
    IReadOnlyList<EvaluationCasePlan> DeterministicOnlyCases,
    int PlannedModelRequests,
    bool AnnotationsApproved,
    bool HasSupportUsageAccountingBlocker)
{
    public bool IsLiveReady => AnnotationsApproved && !HasSupportUsageAccountingBlocker;
}

public static class EvaluationPlanBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static EvaluationPlan Load(
        string datasetPath,
        string split,
        int trials,
        int? maximumCases = null,
        bool allowDraft = false,
        IReadOnlySet<string>? caseIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(split);
        if (trials is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(trials), "Trials must be between 1 and 10.");
        }

        if (maximumCases is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCases), "Maximum cases must be greater than zero.");
        }

        var cases = File.ReadLines(datasetPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(Parse)
            .Where(item => string.Equals(item.Split, split, StringComparison.Ordinal))
            .Where(item => caseIds is null || caseIds.Contains(item.CaseId))
            .OrderBy(item => item.CaseId, StringComparer.Ordinal)
            .ToList();
        if (caseIds is not null)
        {
            var missingCaseIds = caseIds
                .Except(cases.Select(item => item.CaseId), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missingCaseIds.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Evaluation case ids were not found in split '{split}': {string.Join(", ", missingCaseIds)}.");
            }
        }

        if (maximumCases.HasValue)
        {
            cases = cases.Take(maximumCases.Value).ToList();
        }

        if (cases.Count == 0)
        {
            throw new InvalidOperationException($"No evaluation cases were found for split '{split}'.");
        }

        var liveEligible = cases
            .Where(item => item.ServiceCondition == "available" && item.ModelCall == "allowed")
            .ToArray();
        var deterministicOnly = cases.Except(liveEligible).ToArray();
        var annotationsApproved = allowDraft || cases.All(item => item.AnnotationStatus == "approved");
        const bool hasSupportUsageAccountingBlocker = false;

        return new EvaluationPlan(
            split,
            trials,
            cases,
            liveEligible,
            deterministicOnly,
            liveEligible.Sum(item => item.ModelRequestsPerTrial) * trials,
            annotationsApproved,
            hasSupportUsageAccountingBlocker);
    }

    private static EvaluationCasePlan Parse(string jsonLine)
    {
        using var document = JsonDocument.Parse(jsonLine);
        var root = document.RootElement;
        var feature = RequiredString(root, "feature");
        var expected = root.GetProperty("expected");
        var allowedCandidateCount = expected.GetProperty("allowedCandidateIds").GetArrayLength();
        var requestsPerTrial = feature == "product_search" && allowedCandidateCount > 0 ? 2 : 1;

        return new EvaluationCasePlan(
            RequiredString(root, "caseId"),
            RequiredString(root, "split"),
            feature,
            RequiredString(root.GetProperty("prerequisites"), "serviceCondition"),
            RequiredString(expected, "modelCall"),
            RequiredString(root.GetProperty("annotation"), "status"),
            RequiredString(expected, "outcome"),
            requestsPerTrial,
            RequiredString(root.GetProperty("input"), "message"),
            root.GetProperty("prerequisites").GetProperty("fixtureIds")
                .EnumerateArray()
                .Select(item => item.GetString() ?? throw new JsonException("Fixture id must be a string."))
                .ToArray(),
            expected.Clone());
    }

    private static string RequiredString(JsonElement owner, string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException($"Required string property '{propertyName}' is missing.");
        }

        return value.GetString()!;
    }
}

public static class LiveEvaluationConfigurationValidator
{
    public static IReadOnlyList<string> Validate(OpenAiResponsesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("OpenAI:ApiKey is missing.");
        }

        AddPositivePrice(
            failures,
            options.ProductSearchInputCostPerMillionTokens,
            "OpenAI:ProductSearchInputCostPerMillionTokens");
        AddPositivePrice(
            failures,
            options.ProductSearchOutputCostPerMillionTokens,
            "OpenAI:ProductSearchOutputCostPerMillionTokens");
        AddPositivePrice(
            failures,
            options.SupportInputCostPerMillionTokens,
            "OpenAI:SupportInputCostPerMillionTokens");
        AddPositivePrice(
            failures,
            options.SupportOutputCostPerMillionTokens,
            "OpenAI:SupportOutputCostPerMillionTokens");
        return failures;
    }

    private static void AddPositivePrice(ICollection<string> failures, decimal value, string key)
    {
        if (value <= 0)
        {
            failures.Add($"{key} is missing or not positive.");
        }
    }
}
