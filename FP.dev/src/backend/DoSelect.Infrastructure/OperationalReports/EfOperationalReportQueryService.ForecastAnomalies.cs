using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.OperationalReports;

public sealed partial class EfOperationalReportQueryService
{
    private const int ForecastObservationDays = 30;
    private const int MinimumForecastDays = 14;
    private const int ForecastHorizonDays = 7;

    private async Task<ReportResultDto> QueryForecastAnomaliesAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var requestedDays = query.ToDate.DayNumber - query.FromDate.DayNumber;
        var observationDays = Math.Min(ForecastObservationDays, requestedDays);
        var windowStart = query.ToDate.AddDays(-observationDays);
        if (windowStart < query.FromDate)
        {
            windowStart = query.FromDate;
        }

        var fromUtc = ToUtcBoundary(windowStart);
        var toUtc = ToUtcBoundary(query.ToDate);
        var statuses = query.OrderStatuses
            .Select(status => Enum.Parse<OrderStatus>(status, ignoreCase: false))
            .ToArray();
        var paidOrders = _context.PaymentAttempts.AsNoTracking()
            .Where(attempt =>
                attempt.Status == PaymentAttemptStatus.Paid &&
                attempt.PaidAtUtc >= fromUtc &&
                attempt.PaidAtUtc < toUtc)
            .Select(attempt => new
            {
                attempt.OrderId,
                DayNumber = EF.Functions.DateDiffDay(
                    DayNumberAnchor,
                    attempt.PaidAtUtc!.Value.AddHours(TaipeiUtcOffsetHours)),
            })
            .Distinct();
        var saleRows = await (
            from paid in paidOrders
            join order in _context.Orders.AsNoTracking() on paid.OrderId equals order.Id
            join item in _context.OrderItems.AsNoTracking() on order.Id equals item.OrderId
            join sku in _context.Skus.AsNoTracking() on item.SkuId equals (long?)sku.Id
            join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
            join category in _context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in _context.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where (statuses.Length == 0 || statuses.Contains(order.OrderStatus)) &&
                  (query.CategoryCode == null || category.Code == query.CategoryCode) &&
                  (query.BrandCode == null || brand.Code == query.BrandCode)
            group item by paid.DayNumber
            into day
            select new ForecastDailyQuantity(day.Key, day.Sum(item => item.Quantity)))
            .ToListAsync(cancellationToken);
        var refundRows = await (
            from allocation in _context.RefundAllocations.AsNoTracking()
            join refund in _context.Refunds.AsNoTracking() on allocation.RefundId equals refund.Id
            join item in _context.OrderItems.AsNoTracking()
                on allocation.OrderItemId equals (long?)item.Id
            join order in _context.Orders.AsNoTracking() on item.OrderId equals order.Id
            join sku in _context.Skus.AsNoTracking() on item.SkuId equals (long?)sku.Id
            join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
            join category in _context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in _context.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where refund.Status == RefundStatus.Succeeded &&
                  refund.SucceededAtUtc >= fromUtc &&
                  refund.SucceededAtUtc < toUtc &&
                  allocation.AllocationType == RefundAllocationType.ItemRefund &&
                  allocation.Quantity != null &&
                  (statuses.Length == 0 || statuses.Contains(order.OrderStatus)) &&
                  (query.CategoryCode == null || category.Code == query.CategoryCode) &&
                  (query.BrandCode == null || brand.Code == query.BrandCode)
            group allocation by EF.Functions.DateDiffDay(
                DayNumberAnchor,
                refund.SucceededAtUtc!.Value.AddHours(TaipeiUtcOffsetHours))
            into day
            select new ForecastDailyQuantity(
                day.Key,
                day.Sum(allocation => allocation.Quantity!.Value)))
            .ToListAsync(cancellationToken);
        var salesByDate = saleRows.ToDictionary(
            row => FromDayNumber(row.DayNumber),
            row => row.Quantity);
        var refundsByDate = refundRows.ToDictionary(
            row => FromDayNumber(row.DayNumber),
            row => row.Quantity);
        var actualValues = Enumerable.Range(0, observationDays)
            .Select(offset =>
            {
                var date = windowStart.AddDays(offset);
                return (Date: date, Value: (decimal)(
                    salesByDate.GetValueOrDefault(date) - refundsByDate.GetValueOrDefault(date)));
            })
            .ToArray();
        var insufficientData = observationDays < MinimumForecastDays;
        var rows = new List<ForecastAnomalyReportRowDto>();
        RegressionAnalysis? analysis = null;

        if (insufficientData)
        {
            rows.AddRange(actualValues.Select(point => new ForecastAnomalyReportRowDto(
                point.Date,
                point.Value,
                ForecastValue: null,
                ZScore: null,
                IsAnomaly: false,
                IsInsufficientData: true)));
        }
        else
        {
            analysis = OperationalReportStatistics.AnalyzeRegression(
                actualValues.Select(point => point.Value).ToArray(),
                ForecastHorizonDays);
            for (var index = 0; index < actualValues.Length; index++)
            {
                var zScore = analysis.ResidualZScores[index];
                rows.Add(new ForecastAnomalyReportRowDto(
                    actualValues[index].Date,
                    actualValues[index].Value,
                    Math.Max(0m, analysis.Intercept + analysis.Slope * (index + 1)),
                    zScore,
                    zScore is not null && Math.Abs(zScore.Value) > 2m,
                    IsInsufficientData: false));
            }

            rows.AddRange(analysis.Forecasts.Select((forecast, index) =>
                new ForecastAnomalyReportRowDto(
                    query.ToDate.AddDays(index),
                    ActualValue: null,
                    forecast,
                    ZScore: null,
                    IsAnomaly: false,
                    IsInsufficientData: false)));
        }

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
            [
                new ReportMetricDto("observed_days", observationDays, "day"),
                new ReportMetricDto("forecast_days", insufficientData ? 0m : ForecastHorizonDays, "day"),
                new ReportMetricDto("anomaly_count", rows.Count(row => row.IsAnomaly), "count"),
                new ReportMetricDto("slope", analysis?.Slope, "quantity_per_day"),
            ],
            rows.Select(row => new ReportSeriesPointDto(
                row.Date.ToString("yyyy-MM-dd"),
                [
                    new ReportMetricDto("actual_quantity", row.ActualValue, "count"),
                    new ReportMetricDto("forecast_quantity", row.ForecastValue, "count"),
                    new ReportMetricDto("residual_z_score", row.ZScore, "z_score"),
                ])).ToArray(),
            CreateOffsetPage(definition, query, rows));
    }

    private sealed record ForecastDailyQuantity(int DayNumber, int Quantity);
}
