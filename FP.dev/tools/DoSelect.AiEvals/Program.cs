using System.Globalization;
using System.Text.Json;
using DoSelect.AiEvals;
using DoSelect.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;

var options = CliOptions.Parse(args);
var projectRoot = options.ProjectRoot ?? FindProjectRoot(AppContext.BaseDirectory);
var datasetPath = Path.Combine(projectRoot, "evals", "ai", "v1", "dataset.zh-TW.v1.jsonl");
var plan = EvaluationPlanBuilder.Load(
    datasetPath,
    options.Split,
    options.Trials,
    options.MaximumCases,
    options.AllowDraft,
    options.CaseIds);

if (!options.Execute)
{
    var output = new
    {
        mode = "dry-run",
        datasetPath,
        plan.Split,
        plan.Trials,
        selectedCases = plan.SelectedCases.Count,
        liveEligibleCases = plan.LiveEligibleCases.Count,
        deterministicOnlyCases = plan.DeterministicOnlyCases.Count,
        plan.PlannedModelRequests,
        plan.AnnotationsApproved,
        plan.HasSupportUsageAccountingBlocker,
        plan.IsLiveReady,
        blockers = new[]
        {
            !plan.AnnotationsApproved ? "ANNOTATIONS_NOT_APPROVED" : null,
            plan.HasSupportUsageAccountingBlocker ? "AI_SUPPORT_COMPLETED_DEGRADATION_USAGE_NOT_PRESERVED" : null,
        }.Where(value => value is not null),
    };
    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (!options.StopAfterCostUsd.HasValue || options.StopAfterCostUsd <= 0)
{
    Console.Error.WriteLine("--execute requires a positive --stop-after-cost-usd value.");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(projectRoot)
    .AddJsonFile(Path.Combine("src", "backend", "DoSelect.Api", "appsettings.json"), optional: false)
    .AddUserSecrets<UserSecretsAnchor>(optional: true)
    .AddEnvironmentVariables()
    .Build();
var openAi = new OpenAiResponsesOptions();
configuration.GetSection(OpenAiResponsesOptions.SectionName).Bind(openAi);
var configurationFailures = LiveEvaluationConfigurationValidator.Validate(openAi);
if (configurationFailures.Count > 0)
{
    Console.Error.WriteLine("Live configuration is incomplete:");
    foreach (var failure in configurationFailures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 3;
}

var outputDirectory = options.OutputDirectory ?? Path.Combine(
    projectRoot,
    ".run",
    "ai-evals",
    DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture));
using var runner = new LiveEvaluationRunner(openAi);
var summary = await runner.RunAsync(
    plan,
    new LiveEvaluationRunOptions(
        projectRoot,
        Path.GetFullPath(outputDirectory),
        options.StopAfterCostUsd.Value));
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
return summary.Verdict == "PASS" ? 0 : 4;

static string FindProjectRoot(string startDirectory)
{
    for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "evals", "ai", "v1", "manifest.json")))
        {
            return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException(
        "Could not find the FP.dev project root. Pass --project-root with an explicit path.");
}

internal sealed class UserSecretsAnchor;

internal sealed record CliOptions(
    string Split,
    int Trials,
    int? MaximumCases,
    bool AllowDraft,
    bool Execute,
    decimal? StopAfterCostUsd,
    string? ProjectRoot,
    string? OutputDirectory,
    IReadOnlySet<string>? CaseIds)
{
    public static CliOptions Parse(string[] args)
    {
        var split = "release";
        var trials = 3;
        int? maximumCases = null;
        var allowDraft = false;
        var execute = false;
        decimal? stopAfterCostUsd = null;
        string? projectRoot = null;
        string? outputDirectory = null;
        HashSet<string>? caseIds = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--split":
                    split = ReadValue(args, ref index, "--split");
                    break;
                case "--trials":
                    trials = int.Parse(ReadValue(args, ref index, "--trials"), CultureInfo.InvariantCulture);
                    break;
                case "--max-cases":
                    maximumCases = int.Parse(ReadValue(args, ref index, "--max-cases"), CultureInfo.InvariantCulture);
                    break;
                case "--project-root":
                    projectRoot = Path.GetFullPath(ReadValue(args, ref index, "--project-root"));
                    break;
                case "--output":
                    outputDirectory = Path.GetFullPath(ReadValue(args, ref index, "--output"));
                    break;
                case "--case-id":
                    caseIds ??= new HashSet<string>(StringComparer.Ordinal);
                    caseIds.Add(ReadValue(args, ref index, "--case-id"));
                    break;
                case "--stop-after-cost-usd":
                    stopAfterCostUsd = decimal.Parse(
                        ReadValue(args, ref index, "--stop-after-cost-usd"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--allow-draft":
                    allowDraft = true;
                    break;
                case "--execute":
                    execute = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (split is not ("development" or "release" or "challenge"))
        {
            throw new ArgumentException("Split must be development, release, or challenge.");
        }

        return new CliOptions(
            split,
            trials,
            maximumCases,
            allowDraft,
            execute,
            stopAfterCostUsd,
            projectRoot,
            outputDirectory,
            caseIds);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        index++;
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return args[index];
    }
}
