using System.Text;
using DoSelect.Application.Common;
using DoSelect.Application.OperationalReports;

namespace DoSelect.Application.Tests.OperationalReports;

public sealed class OperationalReportCsvExporterTests
{
    [Fact]
    public async Task ExportAsync_WritesDemoMetadataUtf8BomAndNeutralizesFormulaCells()
    {
        var row = new ProductAbcReportRowDto(
            Guid.NewGuid(), "+SUM(A1:A2)", "=HYPERLINK(\"bad\")", 2,
            100m, 1m, 1m, "A", 1);
        var exporter = new OperationalReportCsvExporter(new FakeQueryService(
            (definition, query) => Result(definition, query, [row], hasMore: false, null)));

        var export = await exporter.ExportAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ProductAbc),
            Query(),
            CancellationToken.None);

        Assert.Equal([0xEF, 0xBB, 0xBF], export.Content[..3]);
        var csv = Encoding.UTF8.GetString(export.Content);
        Assert.Contains("\"DEMO DATA\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"Report Key\",\"product-abc\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'+SUM(A1:A2)\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\")\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Recipient", csv, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("product-abc-20260901-20260907.csv", export.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_FollowsOpaqueCursorsUntilTheLastPage()
    {
        var calls = new List<string?>();
        var exporter = new OperationalReportCsvExporter(new FakeQueryService(
            (definition, query) =>
            {
                calls.Add(query.Cursor);
                return query.Cursor is null
                    ? Result(definition, query, [SalesRow("2026-09-01")], true, "next")
                    : Result(definition, query, [SalesRow("2026-09-02")], false, null);
            }));

        var export = await exporter.ExportAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.SalesOverview),
            Query() with { Cursor = "ignored" },
            CancellationToken.None);

        Assert.Equal([null, "next"], calls);
        var csv = Encoding.UTF8.GetString(export.Content);
        Assert.Contains("2026-09-01", csv, StringComparison.Ordinal);
        Assert.Contains("2026-09-02", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WritesStableHeadersWhenTheReportHasNoRows()
    {
        var exporter = new OperationalReportCsvExporter(new FakeQueryService(
            (definition, query) => Result(definition, query, [], false, null)));

        var export = await exporter.ExportAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ForecastAnomalies),
            Query(),
            CancellationToken.None);

        var csv = Encoding.UTF8.GetString(export.Content);
        Assert.Contains(
            "\"Date\",\"Actual Value\",\"Forecast Value\",\"Residual Z-Score\",\"Is Anomaly\",\"Is Insufficient Data\"",
            csv,
            StringComparison.Ordinal);
        Assert.DoesNotContain("No rows", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_RejectsMoreThanOneHundredThousandRows()
    {
        var repeated = Enumerable.Repeat<ReportRowDto>(
            SalesRow("2026-09-01"),
            OperationalReportCsvExporter.MaximumRows).ToArray();
        var exporter = new OperationalReportCsvExporter(new FakeQueryService(
            (definition, query) => Result(definition, query, repeated, true, "more")));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            exporter.ExportAsync(
                OperationalReportCatalog.Require(OperationalReportKeys.SalesOverview),
                Query(),
                CancellationToken.None));

        Assert.Equal(OperationalReportErrorCodes.ReportExportTooLarge, exception.Code);
    }

    private static SalesOverviewReportRowDto SalesRow(string bucket) =>
        new(bucket, 100m, 1, 100m, 0m, 0m, 0, 0m);

    private static ValidatedReportQuery Query() => new(
        new DateOnly(2026, 9, 1),
        new DateOnly(2026, 9, 8),
        OperationalReportQueryValidator.SupportedTimeZone,
        CategoryCode: null,
        BrandCode: null,
        OrderStatuses: [],
        ReportGranularities.Day,
        Cursor: null,
        PageSize: 20);

    private static ReportResultDto Result(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        IReadOnlyList<ReportRowDto> rows,
        bool hasMore,
        string? nextCursor) => new(
        definition.Key,
        definition.Title,
        definition.TimeBasis,
        query.TimeZone,
        query.FromDate,
        query.ToDate,
        DateTimeOffset.Parse("2026-09-08T00:00:00Z"),
        DateTimeOffset.Parse("2026-09-08T00:00:00Z"),
        [new ReportMetricDto("row_count", rows.Count, "count")],
        [],
        new CursorPage<ReportRowDto>(rows, nextCursor, hasMore));

    private sealed class FakeQueryService(
        Func<OperationalReportDefinition, ValidatedReportQuery, ReportResultDto> result)
        : IOperationalReportQueryService
    {
        public Task<ReportResultDto> QueryAsync(
            OperationalReportDefinition definition,
            ValidatedReportQuery query,
            CancellationToken cancellationToken) => Task.FromResult(result(definition, query));
    }
}
