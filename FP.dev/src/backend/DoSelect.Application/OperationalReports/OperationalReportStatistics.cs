namespace DoSelect.Application.OperationalReports;

public sealed record RegressionAnalysis(
    decimal Intercept,
    decimal Slope,
    IReadOnlyList<decimal?> ResidualZScores,
    IReadOnlyList<decimal> Forecasts);

public static class OperationalReportStatistics
{
    public static RegressionAnalysis AnalyzeRegression(
        IReadOnlyList<decimal> values,
        int forecastDays)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        if (forecastDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(forecastDays));
        }

        var count = values.Count;
        var meanX = (count + 1d) / 2d;
        var meanY = values.Average(value => (double)value);
        var numerator = 0d;
        var denominator = 0d;
        for (var index = 0; index < count; index++)
        {
            var x = index + 1d;
            numerator += (x - meanX) * ((double)values[index] - meanY);
            denominator += (x - meanX) * (x - meanX);
        }

        var slope = numerator / denominator;
        var intercept = meanY - slope * meanX;
        var residuals = new double[count];
        for (var index = 0; index < count; index++)
        {
            residuals[index] = (double)values[index] - (intercept + slope * (index + 1d));
        }

        var residualMean = residuals.Average();
        var residualVariance = residuals.Sum(value =>
            (value - residualMean) * (value - residualMean)) / count;
        var residualStandardDeviation = Math.Sqrt(residualVariance);
        var residualZScores = residualStandardDeviation <= 1e-12d
            ? Enumerable.Repeat<decimal?>(null, count).ToArray()
            : residuals
                .Select(value => (decimal?)((decimal)((value - residualMean) /
                    residualStandardDeviation)))
                .ToArray();
        var forecasts = Enumerable.Range(1, forecastDays)
            .Select(day => Math.Max(0m, (decimal)(intercept + slope * (count + day))))
            .ToArray();

        return new RegressionAnalysis(
            (decimal)intercept,
            (decimal)slope,
            residualZScores,
            forecasts);
    }
}
