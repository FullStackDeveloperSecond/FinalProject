using ClosedXML.Excel;
using DoSelect.Application.Common;
using DoSelect.Application.OperationalReports;

namespace DoSelect.Application.Tests.OperationalReports;

public sealed class OperationalReportXlsxExporterTests
{
    public static TheoryData<string, ReportRowDto, string> RowSchemas => new()
    {
        {
            OperationalReportKeys.SalesOverview,
            new SalesOverviewReportRowDto("2026-09-01", 100m, 1, 100m, 0m, 0m, 0, 0m),
            "Cancellation Rate"
        },
        {
            OperationalReportKeys.ProductAbc,
            new ProductAbcReportRowDto(Guid.NewGuid(), "SKU", "Name", 1, 100m, 1m, 1m, "A", 1),
            "Rank"
        },
        {
            OperationalReportKeys.PeriodComparison,
            new PeriodComparisonReportRowDto("net_revenue", 100m, 90m, 0.1m, false),
            "Is New"
        },
        {
            OperationalReportKeys.InventoryTurnover,
            new InventoryTurnoverReportRowDto(
                Guid.NewGuid(), "SKU", "Name", 50m, 20m, 30m, 25m, 2m, 15m,
                4, 2, false, false, false, false),
            "Is Insufficient Data"
        },
        {
            OperationalReportKeys.GrossMargin,
            new GrossMarginReportRowDto(Guid.NewGuid(), "SKU", "Name", 100m, 60m, 40m, 0.4m, 1, 0),
            "Refunded Quantity"
        },
        {
            OperationalReportKeys.ProductAssociations,
            new ProductAssociationReportRowDto(
                Guid.NewGuid(), "LEFT", "Left", Guid.NewGuid(), "RIGHT", "Right", 2, 0.2m, 0.5m, 1.5m),
            "Lift"
        },
        {
            OperationalReportKeys.ForecastAnomalies,
            new ForecastAnomalyReportRowDto(new DateOnly(2026, 9, 1), 10m, 9m, 1m, false, false),
            "Is Insufficient Data"
        },
    };

    [Fact]
    public async Task ExportAsync_WritesMetadataTypedRowsAndLiteralUntrustedText()
    {
        var skuPublicId = Guid.NewGuid();
        var row = new ProductAbcReportRowDto(
            skuPublicId, "+SUM(A1:A2)", "=HYPERLINK(\"bad\")", 2,
            100m, 1m, 1m, "A", 1);
        var exporter = new OperationalReportXlsxExporter(new FakeQueryService(
            (definition, query) => Result(definition, query, [row], hasMore: false, null)));

        var export = await exporter.ExportAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ProductAbc),
            Query(),
            CancellationToken.None);

        Assert.Equal([0x50, 0x4B], export.Content[..2]);
        Assert.EndsWith("product-abc-20260901-20260907.xlsx", export.FileName, StringComparison.Ordinal);
        using var workbook = Open(export.Content);
        var readme = workbook.Worksheet("README");
        Assert.Equal("DEMO DATA", readme.Cell("A1").GetString());
        Assert.Equal("product-abc", readme.Cell("B2").GetString());
        Assert.DoesNotContain("Recipient", readme.CellsUsed().Select(cell => cell.GetString()));

        var rows = workbook.Worksheet("Rows");
        Assert.Equal("SKU PublicId", rows.Cell("A1").GetString());
        Assert.Equal(skuPublicId.ToString("D"), rows.Cell("A2").GetString());
        Assert.Equal("+SUM(A1:A2)", rows.Cell("B2").GetString());
        Assert.Equal("=HYPERLINK(\"bad\")", rows.Cell("C2").GetString());
        Assert.False(rows.Cell("B2").HasFormula);
        Assert.False(rows.Cell("C2").HasFormula);
        Assert.Equal(2d, rows.Cell("D2").GetDouble());
        Assert.Equal(100d, rows.Cell("E2").GetDouble());
    }

    [Fact]
    public async Task ExportAsync_FollowsOpaqueCursorsUntilTheLastPage()
    {
        var calls = new List<string?>();
        var exporter = new OperationalReportXlsxExporter(new FakeQueryService(
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
        using var workbook = Open(export.Content);
        var rows = workbook.Worksheet("Rows");
        Assert.Equal("2026-09-01", rows.Cell("A2").GetString());
        Assert.Equal("2026-09-02", rows.Cell("A3").GetString());
    }

    [Fact]
    public async Task ExportAsync_WritesStableHeadersWhenTheReportHasNoRows()
    {
        var exporter = new OperationalReportXlsxExporter(new FakeQueryService(
            (definition, query) => Result(definition, query, [], false, null)));

        var export = await exporter.ExportAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ForecastAnomalies),
            Query(),
            CancellationToken.None);

        using var workbook = Open(export.Content);
        var rows = workbook.Worksheet("Rows");
        Assert.Equal("Date", rows.Cell("A1").GetString());
        Assert.Equal("Is Insufficient Data", rows.Cell("F1").GetString());
        Assert.True(rows.Cell("A2").IsEmpty());
    }

    [Fact]
    public async Task ExportAsync_RejectsMoreThanOneHundredThousandRows()
    {
        var repeated = Enumerable.Repeat<ReportRowDto>(
            SalesRow("2026-09-01"),
            OperationalReportXlsxExporter.MaximumRows).ToArray();
        var exporter = new OperationalReportXlsxExporter(new FakeQueryService(
            (definition, query) => Result(definition, query, repeated, true, "more")));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            exporter.ExportAsync(
                OperationalReportCatalog.Require(OperationalReportKeys.SalesOverview),
                Query(),
                CancellationToken.None));

        Assert.Equal(OperationalReportErrorCodes.ReportExportTooLarge, exception.Code);
    }

    [Theory]
    [MemberData(nameof(RowSchemas))]
    public async Task ExportAsync_WritesEveryWhitelistedRowSchema(
        string reportKey,
        ReportRowDto row,
        string finalHeader)
    {
        var exporter = new OperationalReportXlsxExporter(new FakeQueryService(
            (definition, query) => Result(definition, query, [row], false, null)));

        var export = await exporter.ExportAsync(
            OperationalReportCatalog.Require(reportKey),
            Query(),
            CancellationToken.None);

        using var workbook = Open(export.Content);
        var sheet = workbook.Worksheet("Rows");
        Assert.Equal(finalHeader, sheet.Row(1).LastCellUsed()!.GetString());
        Assert.False(sheet.Cell("A2").IsEmpty());
    }

    private static XLWorkbook Open(byte[] content) => new(new MemoryStream(content));

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
