using System.Net;
using DoSelect.Api.Security;
using DoSelect.Api.IntegrationTests.Support;
using DoSelect.Application.Common;
using DoSelect.Application.OperationalReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.OperationalReports;

public sealed class OperationalReportHttpAcceptanceTests(
    WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string QueryString =
        "?fromDate=2026-09-01&toDate=2026-09-08&timeZone=Asia%2FTaipei&granularity=day&pageSize=20";

    [Fact]
    public async Task Query_WhenAnonymous_Returns401WithoutCallingTheReportService()
    {
        var fakes = new ReportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/v1/admin/reports/{OperationalReportKeys.SalesOverview}{QueryString}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fakes.QueryCalls);
    }

    [Theory]
    [InlineData(DoSelectRoles.MarketingAnalyst, OperationalReportKeys.SalesOverview, HttpStatusCode.OK)]
    [InlineData(DoSelectRoles.FinanceManager, OperationalReportKeys.SalesOverview, HttpStatusCode.OK)]
    [InlineData(DoSelectRoles.CustomerService, OperationalReportKeys.SalesOverview, HttpStatusCode.Forbidden)]
    [InlineData(DoSelectRoles.MarketingAnalyst, OperationalReportKeys.GrossMargin, HttpStatusCode.Forbidden)]
    [InlineData(DoSelectRoles.FinanceManager, OperationalReportKeys.GrossMargin, HttpStatusCode.OK)]
    public async Task Query_EnforcesGeneralAndFinancialRoleBoundaries(
        string role,
        string reportKey,
        HttpStatusCode expected)
    {
        var fakes = new ReportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.GetAsync(
            $"/api/v1/admin/reports/{reportKey}{QueryString}");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.QueryCalls);
    }

    [Fact]
    public async Task Export_WhenAuthorized_ReturnsDownloadableUtf8Csv()
    {
        var fakes = new ReportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, DoSelectRoles.MarketingAnalyst);

        using var response = await client.GetAsync(
            $"/api/v1/admin/reports/{OperationalReportKeys.ProductAbc}/export{QueryString}");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal(1, fakes.ExportCalls);
    }

    [Fact]
    public async Task ExportXlsx_WhenAuthorized_ReturnsDownloadableWorkbook()
    {
        var fakes = new ReportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, DoSelectRoles.MarketingAnalyst);

        using var response = await client.GetAsync(
            $"/api/v1/admin/reports/{OperationalReportKeys.ProductAbc}/export/xlsx{QueryString}");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0x50, 0x4B], bytes[..2]);
        Assert.Equal(1, fakes.XlsxExportCalls);
    }

    private WebApplicationFactory<Program> CreateFactory(ReportHttpFakes fakes) =>
        baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            TestAuthHandler.Configure(services);
            services.AddAuthorization(options =>
            {
                options.AddPolicy(
                    DoSelectPolicies.OperationalReportView,
                    ReportPolicy(DoSelectRoles.MarketingAnalyst, DoSelectRoles.FinanceManager, DoSelectRoles.SuperAdmin));
                options.AddPolicy(
                    DoSelectPolicies.OperationalReportFinanceView,
                    ReportPolicy(DoSelectRoles.FinanceManager, DoSelectRoles.SuperAdmin));
            });
            services.RemoveAll<IOperationalReportQueryService>();
            services.RemoveAll<IOperationalReportCsvExporter>();
            services.RemoveAll<IOperationalReportXlsxExporter>();
            services.AddSingleton<IOperationalReportQueryService>(fakes);
            services.AddSingleton<IOperationalReportCsvExporter>(fakes);
            services.AddSingleton<IOperationalReportXlsxExporter>(fakes);
        }));

    private static AuthorizationPolicy ReportPolicy(params string[] roles) =>
        new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin)
            .RequireClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor)
            .RequireRole(roles)
            .Build();

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, "report-admin");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, role);
        return client;
    }

    private sealed class ReportHttpFakes :
        IOperationalReportQueryService,
        IOperationalReportCsvExporter,
        IOperationalReportXlsxExporter
    {
        public int QueryCalls { get; private set; }
        public int ExportCalls { get; private set; }
        public int XlsxExportCalls { get; private set; }

        public Task<ReportResultDto> QueryAsync(
            OperationalReportDefinition definition,
            ValidatedReportQuery query,
            CancellationToken cancellationToken)
        {
            QueryCalls++;
            return Task.FromResult(new ReportResultDto(
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
                new CursorPage<ReportRowDto>([], null, false)));
        }

        public Task<OperationalReportCsvExport> ExportAsync(
            OperationalReportDefinition definition,
            ValidatedReportQuery query,
            CancellationToken cancellationToken)
        {
            ExportCalls++;
            return Task.FromResult(new OperationalReportCsvExport(
                [0xEF, 0xBB, 0xBF, 0x44, 0x45, 0x4D, 0x4F],
                "report.csv"));
        }

        Task<OperationalReportXlsxExport> IOperationalReportXlsxExporter.ExportAsync(
            OperationalReportDefinition definition,
            ValidatedReportQuery query,
            CancellationToken cancellationToken)
        {
            XlsxExportCalls++;
            return Task.FromResult(new OperationalReportXlsxExport(
                [0x50, 0x4B, 0x03, 0x04],
                "report.xlsx"));
        }
    }
}
