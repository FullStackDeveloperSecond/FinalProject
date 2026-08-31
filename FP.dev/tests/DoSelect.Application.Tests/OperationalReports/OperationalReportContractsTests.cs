using DoSelect.Application.Common;
using DoSelect.Application.OperationalReports;

namespace DoSelect.Application.Tests.OperationalReports;

public sealed class OperationalReportContractsTests
{
    [Fact]
    public void CatalogContainsExactlyTheSevenApprovedReportKeys()
    {
        Assert.Equal(
            [
                "sales-overview",
                "product-abc",
                "period-comparison",
                "inventory-turnover",
                "gross-margin",
                "product-associations",
                "forecast-anomalies",
            ],
            OperationalReportCatalog.All.Select(definition => definition.Key));
    }

    [Theory]
    [InlineData("inventory-turnover")]
    [InlineData("gross-margin")]
    public void CostReportsRequireTheFinancialPolicy(string reportKey)
    {
        Assert.Equal(
            OperationalReportSensitivity.Financial,
            OperationalReportCatalog.Require(reportKey).Sensitivity);
    }

    [Theory]
    [InlineData("sales-overview")]
    [InlineData("product-abc")]
    [InlineData("period-comparison")]
    [InlineData("product-associations")]
    [InlineData("forecast-anomalies")]
    public void NonCostReportsUseTheGeneralOperationalReportPolicy(string reportKey)
    {
        Assert.Equal(
            OperationalReportSensitivity.General,
            OperationalReportCatalog.Require(reportKey).Sensitivity);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("SALES-OVERVIEW")]
    [InlineData("")]
    public void RequireRejectsAnythingOutsideTheExactWhitelist(string reportKey)
    {
        var exception = Assert.Throws<DomainProblemException>(
            () => OperationalReportCatalog.Require(reportKey));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(OperationalReportErrorCodes.ReportKeyInvalid, exception.Code);
    }

    [Fact]
    public void ValidatorNormalizesTheSupportedQueryWithoutChangingItsDateBoundary()
    {
        var query = OperationalReportQueryValidator.Normalize(new ReportQuery(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 1),
            OperationalReportQueryValidator.SupportedTimeZone,
            "  cpu  ",
            " intel ",
            ["Completed", "completed", "Cancelled"],
            "WEEK",
            Cursor: "cursor-1",
            PageSize: 50));

        Assert.Equal(new DateOnly(2026, 8, 1), query.FromDate);
        Assert.Equal(new DateOnly(2026, 9, 1), query.ToDate);
        Assert.Equal("cpu", query.CategoryCode);
        Assert.Equal("intel", query.BrandCode);
        Assert.Equal(["Completed", "Cancelled"], query.OrderStatuses);
        Assert.Equal(ReportGranularities.Week, query.Granularity);
        Assert.Equal("cursor-1", query.Cursor);
        Assert.Equal(50, query.PageSize);
    }

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public void ValidatorRejectsInvalidRangesAndFilters(ReportQuery query)
    {
        var exception = Assert.Throws<DomainProblemException>(
            () => OperationalReportQueryValidator.Normalize(query));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(OperationalReportErrorCodes.ReportRangeInvalid, exception.Code);
    }

    [Theory]
    [InlineData(80, 100, "A")]
    [InlineData(80.01, 100, "B")]
    [InlineData(95, 100, "B")]
    [InlineData(95.01, 100, "C")]
    public void AbcClassificationUsesTheApprovedInclusiveBoundaries(
        decimal cumulativeRevenue,
        decimal totalRevenue,
        string expected)
    {
        Assert.Equal(
            expected,
            OperationalReportMath.ClassifyAbc(cumulativeRevenue, totalRevenue));
    }

    [Fact]
    public void RatioReturnsNullWhenThereIsNoSample()
    {
        Assert.Null(OperationalReportMath.RatioOrNull(25m, 0m));
        Assert.Equal(0.25m, OperationalReportMath.RatioOrNull(25m, 100m));
    }

    [Fact]
    public void ChangeRateReturnsNullForAZeroComparisonPeriod()
    {
        Assert.Null(OperationalReportMath.ChangeRateOrNull(100m, 0m));
        Assert.Equal(1m, OperationalReportMath.ChangeRateOrNull(100m, 50m));
        Assert.Equal(-0.5m, OperationalReportMath.ChangeRateOrNull(50m, 100m));
    }

    public static TheoryData<ReportQuery> InvalidQueries => new()
    {
        ValidQuery() with { ToDate = new DateOnly(2026, 8, 1) },
        ValidQuery() with { TimeZone = "UTC" },
        ValidQuery() with { CategoryCode = new string('c', 65) },
        ValidQuery() with { BrandCode = new string('b', 65) },
        ValidQuery() with { OrderStatuses = Enumerable.Repeat("Completed", 11).ToArray() },
        ValidQuery() with { OrderStatuses = ["NotAStatus"] },
        ValidQuery() with { Granularity = "quarter" },
        ValidQuery() with { Cursor = new string('x', 513) },
        ValidQuery() with { PageSize = 0 },
        ValidQuery() with { PageSize = 101 },
    };

    private static ReportQuery ValidQuery() => new(
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 9, 1),
        OperationalReportQueryValidator.SupportedTimeZone,
        CategoryCode: null,
        BrandCode: null,
        OrderStatuses: [],
        ReportGranularities.Day,
        Cursor: null,
        PageSize: 50);
}
