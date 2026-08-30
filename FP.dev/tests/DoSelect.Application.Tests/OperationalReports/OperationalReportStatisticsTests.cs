using DoSelect.Application.OperationalReports;

namespace DoSelect.Application.Tests.OperationalReports;

public sealed class OperationalReportStatisticsTests
{
    [Fact]
    public void AnalyzeRegressionProjectsSevenDaysAndLeavesZeroVarianceZScoresUndefined()
    {
        var values = Enumerable.Range(1, 30).Select(value => (decimal)value).ToArray();

        var result = OperationalReportStatistics.AnalyzeRegression(values, forecastDays: 7);

        Assert.Equal(1m, result.Slope);
        Assert.Equal(0m, result.Intercept);
        Assert.Equal([31m, 32m, 33m, 34m, 35m, 36m, 37m], result.Forecasts);
        Assert.All(result.ResidualZScores, value => Assert.Null(value));
    }

    [Fact]
    public void AnalyzeRegressionUsesPopulationStandardDeviationForResidualAnomalies()
    {
        var values = Enumerable.Range(1, 30).Select(value => (decimal)value).ToArray();
        values[14] = 100m;

        var result = OperationalReportStatistics.AnalyzeRegression(values, forecastDays: 7);

        Assert.NotNull(result.ResidualZScores[14]);
        Assert.True(Math.Abs(result.ResidualZScores[14]!.Value) > 2m);
    }

    [Fact]
    public void AnalyzeRegressionClampsNegativeForecastsAtZero()
    {
        var values = Enumerable.Range(1, 14)
            .Select(value => 15m - value)
            .ToArray();

        var result = OperationalReportStatistics.AnalyzeRegression(values, forecastDays: 7);

        Assert.All(result.Forecasts, value => Assert.True(value >= 0m));
        Assert.Equal(0m, result.Forecasts[^1]);
    }
}
