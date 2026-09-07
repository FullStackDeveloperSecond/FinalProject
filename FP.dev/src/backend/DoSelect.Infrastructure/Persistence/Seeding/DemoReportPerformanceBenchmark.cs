using System.Diagnostics;
using DoSelect.Application.OperationalReports;
using DoSelect.Infrastructure.OperationalReports;

namespace DoSelect.Infrastructure.Persistence.Seeding;

public sealed class DemoReportPerformanceBenchmark(DoSelectDbContext dbContext)
{
    public static DemoReportBenchmarkOptions DefaultOptions { get; } = new(
        WarmupIterations: 3,
        MeasuredIterations: 30,
        P95LimitMilliseconds: 3_000m);

    public async Task<DemoReportBenchmarkResult> MeasureAsync(
        DemoReportBenchmarkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= DefaultOptions;
        ValidateOptions(options);
        DemoDatabaseSafety.EnsureAllowedLocalDatabase(dbContext);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var validation = await new DemoDataValidator(dbContext).ValidateAsync(cancellationToken);
        if (!validation.IsValid)
        {
            return CreateResult(
                startedAtUtc,
                options,
                datasetIsValid: false,
                validation.Failures,
                []);
        }

        var queryService = new EfOperationalReportQueryService(
            dbContext,
            new FixedUtcTimeProvider(DemoSeedManifest.PeriodEndUtc.AddSeconds(1)));
        var query = OperationalReportQueryValidator.Normalize(new ReportQuery(
            DateOnly.FromDateTime(DemoSeedManifest.PeriodStartUtc),
            DateOnly.FromDateTime(DemoSeedManifest.PeriodEndUtc).AddDays(1),
            OperationalReportQueryValidator.SupportedTimeZone,
            CategoryCode: null,
            BrandCode: null,
            OrderStatuses: null,
            ReportGranularities.Day,
            Cursor: null,
            OperationalReportQueryValidator.MaximumPageSize));
        var reports = new List<DemoReportPerformanceResult>(OperationalReportCatalog.All.Count);

        foreach (var definition in OperationalReportCatalog.All)
        {
            for (var iteration = 0; iteration < options.WarmupIterations; iteration++)
            {
                await queryService.QueryAsync(definition, query, cancellationToken);
            }

            var durations = new decimal[options.MeasuredIterations];
            for (var iteration = 0; iteration < durations.Length; iteration++)
            {
                var startTimestamp = Stopwatch.GetTimestamp();
                await queryService.QueryAsync(definition, query, cancellationToken);
                durations[iteration] = Math.Round(
                    (decimal)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    3,
                    MidpointRounding.AwayFromZero);
            }

            var ordered = durations.Order().ToArray();
            var p50 = NearestRank(ordered, 0.50m);
            var p95 = NearestRank(ordered, 0.95m);
            reports.Add(new DemoReportPerformanceResult(
                definition.Key,
                durations,
                ordered[0],
                p50,
                p95,
                ordered[^1],
                p95 <= options.P95LimitMilliseconds));
        }

        return CreateResult(
            startedAtUtc,
            options,
            datasetIsValid: true,
            [],
            reports);
    }

    private static DemoReportBenchmarkResult CreateResult(
        DateTimeOffset startedAtUtc,
        DemoReportBenchmarkOptions options,
        bool datasetIsValid,
        IReadOnlyList<string> validationFailures,
        IReadOnlyList<DemoReportPerformanceResult> reports) => new(
            SchemaVersion: "demo-report-performance-v1",
            DatasetVersion: DemoSeedManifest.Version,
            DemoSeedManifest.RandomSeed,
            DemoSeedManifest.PeriodStartUtc,
            DemoSeedManifest.PeriodEndUtc,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            options.WarmupIterations,
            options.MeasuredIterations,
            options.P95LimitMilliseconds,
            PercentileMethod: "nearest-rank",
            Scope: "query-service-and-sql-materialization",
            ExecutionMode: "sequential-single-client",
            DatasetIsValid: datasetIsValid,
            IsValid: datasetIsValid && reports.Count == OperationalReportCatalog.All.Count &&
                reports.All(report => report.Passed),
            ValidationFailures: validationFailures,
            Reports: reports);

    private static decimal NearestRank(IReadOnlyList<decimal> orderedValues, decimal percentile)
    {
        var rank = (int)Math.Ceiling(orderedValues.Count * percentile);
        return orderedValues[Math.Max(rank, 1) - 1];
    }

    private static void ValidateOptions(DemoReportBenchmarkOptions options)
    {
        if (options.WarmupIterations is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Warm-up iterations must be between 0 and 100.");
        }

        if (options.MeasuredIterations is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Measured iterations must be between 1 and 1,000.");
        }

        if (options.P95LimitMilliseconds <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The P95 limit must be greater than zero.");
        }
    }

    private sealed class FixedUtcTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(utcNow);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}

public sealed record DemoReportBenchmarkOptions(
    int WarmupIterations,
    int MeasuredIterations,
    decimal P95LimitMilliseconds);

public sealed record DemoReportPerformanceResult(
    string ReportKey,
    IReadOnlyList<decimal> DurationsMilliseconds,
    decimal MinimumMilliseconds,
    decimal P50Milliseconds,
    decimal P95Milliseconds,
    decimal MaximumMilliseconds,
    bool Passed);

public sealed record DemoReportBenchmarkResult(
    string SchemaVersion,
    string DatasetVersion,
    int RandomSeed,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    int WarmupIterations,
    int MeasuredIterations,
    decimal P95LimitMilliseconds,
    string PercentileMethod,
    string Scope,
    string ExecutionMode,
    bool DatasetIsValid,
    bool IsValid,
    IReadOnlyList<string> ValidationFailures,
    IReadOnlyList<DemoReportPerformanceResult> Reports);
