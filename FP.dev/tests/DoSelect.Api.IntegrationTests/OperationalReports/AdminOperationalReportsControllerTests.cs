using System.Security.Claims;
using DoSelect.Api.OperationalReports;
using DoSelect.Application.Common;
using DoSelect.Application.OperationalReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.IntegrationTests.OperationalReports;

public sealed class AdminOperationalReportsControllerTests
{
    [Fact]
    public async Task Get_NormalizesTheSharedQueryAndReturnsTheRequestedGeneralReport()
    {
        var queryService = new FakeQueryService();
        var controller = CreateController(queryService, financialAuthorized: true);

        var action = await controller.Get(
            OperationalReportKeys.SalesOverview,
            Query(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        Assert.IsType<ReportResultDto>(ok.Value);
        Assert.Equal(OperationalReportKeys.SalesOverview, queryService.Definition?.Key);
        Assert.Equal(OperationalReportQueryValidator.SupportedTimeZone, queryService.Query?.TimeZone);
    }

    [Fact]
    public async Task Get_FinancialReportRequiresTheAdditionalFinancePolicy()
    {
        var queryService = new FakeQueryService();
        var controller = CreateController(queryService, financialAuthorized: false);

        var action = await controller.Get(
            OperationalReportKeys.GrossMargin,
            Query(),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        Assert.Null(queryService.Definition);
    }

    [Fact]
    public async Task Export_ReturnsUtf8CsvAndIgnoresAListCursor()
    {
        var exporter = new FakeCsvExporter();
        var controller = CreateController(
            new FakeQueryService(),
            financialAuthorized: true,
            exporter);

        var action = await controller.Export(
            OperationalReportKeys.ProductAbc,
            Query() with { Cursor = "list-cursor" },
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(action);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("report.csv", file.FileDownloadName);
        Assert.Null(exporter.Query?.Cursor);
    }

    [Fact]
    public async Task ExportXlsx_ReturnsWorkbookAndIgnoresAListCursor()
    {
        var exporter = new FakeXlsxExporter();
        var controller = CreateController(
            new FakeQueryService(),
            financialAuthorized: true,
            xlsxExporter: exporter);

        var action = await controller.ExportXlsx(
            OperationalReportKeys.ProductAbc,
            Query() with { Cursor = "list-cursor" },
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(action);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.ContentType);
        Assert.Equal("report.xlsx", file.FileDownloadName);
        Assert.Null(exporter.Query?.Cursor);
    }

    [Fact]
    public async Task ExportXlsx_FinancialReportRequiresTheAdditionalFinancePolicy()
    {
        var exporter = new FakeXlsxExporter();
        var controller = CreateController(
            new FakeQueryService(),
            financialAuthorized: false,
            xlsxExporter: exporter);

        var action = await controller.ExportXlsx(
            OperationalReportKeys.GrossMargin,
            Query(),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(action);
        Assert.Null(exporter.Query);
    }

    private static AdminOperationalReportsController CreateController(
        FakeQueryService queryService,
        bool financialAuthorized,
        FakeCsvExporter? exporter = null,
        FakeXlsxExporter? xlsxExporter = null)
    {
        var controller = new AdminOperationalReportsController(
            queryService,
            exporter ?? new FakeCsvExporter(),
            xlsxExporter ?? new FakeXlsxExporter(),
            new FakeAuthorizationService(financialAuthorized));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "admin")], "test")),
            },
        };
        return controller;
    }

    private static ReportQuery Query() => new(
        new DateOnly(2026, 9, 1),
        new DateOnly(2026, 9, 8),
        OperationalReportQueryValidator.SupportedTimeZone,
        CategoryCode: null,
        BrandCode: null,
        OrderStatuses: [],
        ReportGranularities.Day,
        Cursor: null,
        PageSize: 20);

    private sealed class FakeQueryService : IOperationalReportQueryService
    {
        public OperationalReportDefinition? Definition { get; private set; }
        public ValidatedReportQuery? Query { get; private set; }

        public Task<ReportResultDto> QueryAsync(
            OperationalReportDefinition definition,
            ValidatedReportQuery query,
            CancellationToken cancellationToken)
        {
            Definition = definition;
            Query = query;
            var result = new ReportResultDto(
                definition.Key,
                definition.Title,
                definition.TimeBasis,
                query.TimeZone,
                query.FromDate,
                query.ToDate,
                DateTimeOffset.Parse("2026-09-08T00:00:00Z"),
                DateTimeOffset.Parse("2026-09-08T00:00:00Z"),
                [],
                [],
                new CursorPage<ReportRowDto>([], null, false));
            return Task.FromResult(result);
        }
    }

    private sealed class FakeCsvExporter : IOperationalReportCsvExporter
    {
        public ValidatedReportQuery? Query { get; private set; }

        public Task<OperationalReportCsvExport> ExportAsync(
            OperationalReportDefinition definition,
            ValidatedReportQuery query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(new OperationalReportCsvExport([0xEF, 0xBB, 0xBF], "report.csv"));
        }
    }

    private sealed class FakeXlsxExporter : IOperationalReportXlsxExporter
    {
        public ValidatedReportQuery? Query { get; private set; }

        public Task<OperationalReportXlsxExport> ExportAsync(
            OperationalReportDefinition definition,
            ValidatedReportQuery query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(new OperationalReportXlsxExport([0x50, 0x4B], "report.xlsx"));
        }
    }

    private sealed class FakeAuthorizationService(bool succeeds) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(succeeds ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(succeeds ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
