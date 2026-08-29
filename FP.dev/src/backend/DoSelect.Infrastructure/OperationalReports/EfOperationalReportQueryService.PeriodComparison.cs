using DoSelect.Application.OperationalReports;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.OperationalReports;

public sealed partial class EfOperationalReportQueryService
{
    private async Task<ReportResultDto> QueryPeriodComparisonAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var periodLength = query.ToDate.DayNumber - query.FromDate.DayNumber;
        var previousToDate = query.FromDate;
        var previousFromDate = previousToDate.AddDays(-periodLength);
        var current = await LoadRevenuePeriodAsync(
            query,
            query.FromDate,
            query.ToDate,
            cancellationToken);
        var previous = await LoadRevenuePeriodAsync(
            query,
            previousFromDate,
            previousToDate,
            cancellationToken);
        var currentAverageOrderValue = OperationalReportMath.RatioOrNull(
            current.NetRevenue,
            current.OrderCount);
        var previousAverageOrderValue = OperationalReportMath.RatioOrNull(
            previous.NetRevenue,
            previous.OrderCount);
        var rows = new PeriodComparisonReportRowDto[]
        {
            ComparisonRow("net_revenue", current.NetRevenue, previous.NetRevenue),
            ComparisonRow("paid_amount", current.PaidAmount, previous.PaidAmount),
            ComparisonRow("refund_amount", current.RefundAmount, previous.RefundAmount),
            ComparisonRow("order_count", current.OrderCount, previous.OrderCount),
            ComparisonRow(
                "average_order_value",
                currentAverageOrderValue,
                previousAverageOrderValue),
        };
        var generatedAtUtc = _timeProvider.GetUtcNow();

        return new ReportResultDto(
            definition.Key,
            definition.Title,
            definition.TimeBasis,
            query.TimeZone,
            query.FromDate,
            query.ToDate,
            generatedAtUtc,
            generatedAtUtc,
            rows.Select(row => new ReportMetricDto(
                row.MetricKey,
                row.CurrentValue,
                UnitForComparisonMetric(row.MetricKey))).ToArray(),
            [
                new ReportSeriesPointDto(
                    "previous",
                    RevenuePeriodMetrics(previous, previousAverageOrderValue)),
                new ReportSeriesPointDto(
                    "current",
                    RevenuePeriodMetrics(current, currentAverageOrderValue)),
            ],
            CreateOffsetPage(definition, query, rows));
    }

    private async Task<RevenuePeriodAggregate> LoadRevenuePeriodAsync(
        ValidatedReportQuery query,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var fromUtc = ToUtcBoundary(fromDate);
        var toUtc = ToUtcBoundary(toDate);
        var paymentsQuery = FilteredPayments(query);
        var refundsQuery = FilteredRefunds(query);
        var payments = await (
            from payment in paymentsQuery
            where payment.PaidAtUtc >= fromUtc && payment.PaidAtUtc < toUtc
            group payment by 1
            into period
            select new PeriodPaymentRow(
                period.Sum(payment => payment.Amount),
                period.Select(payment => payment.OrderId).Distinct().Count()))
            .SingleOrDefaultAsync(cancellationToken);
        var refundAmount = await (
            from refund in refundsQuery
            where refund.SucceededAtUtc >= fromUtc && refund.SucceededAtUtc < toUtc
            group refund by 1
            into period
            select period.Sum(refund => refund.Amount))
            .SingleOrDefaultAsync(cancellationToken);
        var paidAmount = payments?.PaidAmount ?? 0m;
        return new RevenuePeriodAggregate(
            paidAmount,
            refundAmount,
            paidAmount - refundAmount,
            payments?.OrderCount ?? 0);
    }

    private static PeriodComparisonReportRowDto ComparisonRow(
        string metricKey,
        decimal? current,
        decimal? previous)
    {
        var changeRate = current.HasValue && previous.HasValue
            ? OperationalReportMath.ChangeRateOrNull(current.Value, previous.Value)
            : null;
        var isNew = current is > 0m && previous is null or 0m;
        return new PeriodComparisonReportRowDto(
            metricKey,
            current,
            previous,
            changeRate,
            isNew);
    }

    private static IReadOnlyList<ReportMetricDto> RevenuePeriodMetrics(
        RevenuePeriodAggregate period,
        decimal? averageOrderValue) =>
    [
        new ReportMetricDto("net_revenue", period.NetRevenue, "TWD"),
        new ReportMetricDto("paid_amount", period.PaidAmount, "TWD"),
        new ReportMetricDto("refund_amount", period.RefundAmount, "TWD"),
        new ReportMetricDto("order_count", period.OrderCount, "count"),
        new ReportMetricDto("average_order_value", averageOrderValue, "TWD"),
    ];

    private static string UnitForComparisonMetric(string metricKey) => metricKey switch
    {
        "order_count" => "count",
        _ => "TWD",
    };

    private sealed record PeriodPaymentRow(decimal PaidAmount, int OrderCount);

    private sealed record RevenuePeriodAggregate(
        decimal PaidAmount,
        decimal RefundAmount,
        decimal NetRevenue,
        int OrderCount);
}
