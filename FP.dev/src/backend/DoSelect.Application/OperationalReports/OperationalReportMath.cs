namespace DoSelect.Application.OperationalReports;

public static class OperationalReportMath
{
    public static decimal? RatioOrNull(decimal numerator, decimal denominator)
    {
        if (denominator < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        return denominator == 0m ? null : numerator / denominator;
    }

    public static decimal? ChangeRateOrNull(decimal current, decimal previous) =>
        previous == 0m ? null : (current - previous) / previous;

    public static string ClassifyAbc(decimal cumulativeRevenue, decimal totalRevenue)
    {
        if (cumulativeRevenue < 0m || totalRevenue <= 0m || cumulativeRevenue > totalRevenue)
        {
            throw new ArgumentOutOfRangeException(nameof(cumulativeRevenue));
        }

        var cumulativeShare = cumulativeRevenue / totalRevenue;
        if (cumulativeShare <= 0.80m)
        {
            return "A";
        }

        return cumulativeShare <= 0.95m ? "B" : "C";
    }
}
