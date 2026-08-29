using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.OperationalReports;

public sealed partial class EfOperationalReportQueryService : IOperationalReportQueryService
{
    private const string CursorFingerprintTag = "operational-report-rows-v1";
    private const int TaipeiUtcOffsetHours = 8;

    private static readonly DateTime DayNumberAnchor =
        new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly DoSelectDbContext _context;
    private readonly TimeProvider _timeProvider;

    public EfOperationalReportQueryService(
        DoSelectDbContext context,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _timeProvider = timeProvider;
    }

    public Task<ReportResultDto> QueryAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(query);

        return definition.Key switch
        {
            OperationalReportKeys.SalesOverview =>
                QuerySalesOverviewAsync(definition, query, cancellationToken),
            OperationalReportKeys.ProductAbc =>
                QueryProductAbcAsync(definition, query, cancellationToken),
            OperationalReportKeys.PeriodComparison =>
                QueryPeriodComparisonAsync(definition, query, cancellationToken),
            OperationalReportKeys.InventoryTurnover =>
                QueryInventoryTurnoverAsync(definition, query, cancellationToken),
            OperationalReportKeys.GrossMargin =>
                QueryGrossMarginAsync(definition, query, cancellationToken),
            OperationalReportKeys.ProductAssociations =>
                QueryProductAssociationsAsync(definition, query, cancellationToken),
            OperationalReportKeys.ForecastAnomalies =>
                QueryForecastAnomaliesAsync(definition, query, cancellationToken),
            _ => throw new NotSupportedException(
                $"The '{definition.Key}' operational report has not been implemented yet."),
        };
    }

    private async Task<ReportResultDto> QuerySalesOverviewAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var fromUtc = ToUtcBoundary(query.FromDate);
        var toUtc = ToUtcBoundary(query.ToDate);
        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), query);
        var payments = FilteredPayments(query);
        var refunds = FilteredRefunds(query);

        var paymentRows = await (
            from payment in payments
            where payment.PaidAtUtc >= fromUtc && payment.PaidAtUtc < toUtc
            group payment by EF.Functions.DateDiffDay(
                DayNumberAnchor,
                payment.PaidAtUtc.AddHours(TaipeiUtcOffsetHours))
            into paymentDay
            select new DailyPaymentRow(
                paymentDay.Key,
                paymentDay.Sum(payment => payment.Amount),
                paymentDay.Select(payment => payment.OrderId).Distinct().Count()))
            .ToListAsync(cancellationToken);

        var paymentMethodRows = await (
            from payment in payments
            where payment.PaidAtUtc >= fromUtc && payment.PaidAtUtc < toUtc
            group payment by payment.Method
            into methods
            select new PaymentMethodRow(methods.Key, methods.Sum(payment => payment.Amount)))
            .ToListAsync(cancellationToken);

        var refundRows = await (
            from refund in refunds
            where refund.SucceededAtUtc >= fromUtc && refund.SucceededAtUtc < toUtc
            group refund by EF.Functions.DateDiffDay(
                DayNumberAnchor,
                refund.SucceededAtUtc.AddHours(TaipeiUtcOffsetHours))
            into refundDay
            select new DailyRefundRow(
                refundDay.Key,
                refundDay.Sum(refund => refund.Amount)))
            .ToListAsync(cancellationToken);

        var orderCohortRows = await orders
            .Where(order => order.CreatedAtUtc >= fromUtc && order.CreatedAtUtc < toUtc)
            .GroupBy(order => EF.Functions.DateDiffDay(
                DayNumberAnchor,
                order.CreatedAtUtc.AddHours(TaipeiUtcOffsetHours)))
            .Select(daily => new DailyOrderCohortRow(
                daily.Key,
                daily.Count(),
                daily.Count(order => order.OrderStatus == OrderStatus.Cancelled)))
            .ToListAsync(cancellationToken);

        var daily = new Dictionary<DateOnly, MutableSalesAggregate>();
        foreach (var payment in paymentRows)
        {
            var aggregate = GetOrCreate(daily, FromDayNumber(payment.DayNumber));
            aggregate.PaidAmount += payment.PaidAmount;
            aggregate.OrderCount += payment.OrderCount;
        }

        foreach (var refund in refundRows)
        {
            GetOrCreate(daily, FromDayNumber(refund.DayNumber)).RefundAmount += refund.RefundAmount;
        }

        foreach (var cohort in orderCohortRows)
        {
            var aggregate = GetOrCreate(daily, FromDayNumber(cohort.DayNumber));
            aggregate.CreatedOrderCount += cohort.CreatedOrderCount;
            aggregate.CancelledOrderCount += cohort.CancelledOrderCount;
        }

        var rows = daily
            .GroupBy(pair => BucketStart(pair.Key, query.Granularity))
            .Select(group => ToRow(group.Key, group.Select(pair => pair.Value)))
            .OrderBy(row => row.Bucket, StringComparer.Ordinal)
            .ToArray();

        var paidAmount = paymentRows.Sum(row => row.PaidAmount);
        var refundAmount = refundRows.Sum(row => row.RefundAmount);
        var netRevenue = paidAmount - refundAmount;
        var orderCount = paymentRows.Sum(row => row.OrderCount);
        var createdOrderCount = orderCohortRows.Sum(row => row.CreatedOrderCount);
        var cancelledOrderCount = orderCohortRows.Sum(row => row.CancelledOrderCount);

        var summary = new List<ReportMetricDto>
        {
            new("paid_amount", paidAmount, "TWD"),
            new("refund_amount", refundAmount, "TWD"),
            new("net_revenue", netRevenue, "TWD"),
            new("order_count", orderCount, "count"),
            new(
                "average_order_value",
                OperationalReportMath.RatioOrNull(netRevenue, orderCount),
                "TWD"),
            new(
                "refund_amount_rate",
                OperationalReportMath.RatioOrNull(refundAmount, paidAmount),
                "ratio"),
            new(
                "cancellation_rate",
                OperationalReportMath.RatioOrNull(cancelledOrderCount, createdOrderCount),
                "ratio"),
        };

        foreach (var method in paymentMethodRows.OrderBy(row => row.Method))
        {
            summary.Add(new ReportMetricDto(
                $"payment_method_{PaymentMethodToken(method.Method)}_share",
                OperationalReportMath.RatioOrNull(method.PaidAmount, paidAmount),
                "ratio"));
        }

        var page = CreatePage(definition, query, rows);
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
            summary,
            rows.Select(row => new ReportSeriesPointDto(
                row.Bucket,
                [
                    new ReportMetricDto("net_revenue", row.NetRevenue, "TWD"),
                    new ReportMetricDto("order_count", row.OrderCount, "count"),
                    new ReportMetricDto("refund_amount", row.RefundAmount, "TWD"),
                ])).ToArray(),
            page);
    }

    private IQueryable<Order> ApplyOrderFilters(
        IQueryable<Order> orders,
        ValidatedReportQuery query)
    {
        if (query.OrderStatuses.Count > 0)
        {
            var statuses = query.OrderStatuses
                .Select(status => Enum.Parse<OrderStatus>(status, ignoreCase: false))
                .ToArray();
            orders = orders.Where(order => statuses.Contains(order.OrderStatus));
        }

        if (query.CategoryCode is null && query.BrandCode is null)
        {
            return orders;
        }

        var matchingOrderIds =
            from item in _context.OrderItems.AsNoTracking()
            join sku in _context.Skus.AsNoTracking() on item.SkuId equals (long?)sku.Id
            join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
            join category in _context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in _context.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where (query.CategoryCode == null || category.Code == query.CategoryCode) &&
                  (query.BrandCode == null || brand.Code == query.BrandCode)
            select item.OrderId;

        return orders.Where(order => matchingOrderIds.Contains(order.Id));
    }

    private IQueryable<FilteredPaymentRow> FilteredPayments(ValidatedReportQuery query)
    {
        var orders = ApplyOrderStatusFilters(_context.Orders.AsNoTracking(), query);
        if (query.CategoryCode is null && query.BrandCode is null)
        {
            return
                from attempt in _context.PaymentAttempts.AsNoTracking()
                join order in orders on attempt.OrderId equals order.Id
                where attempt.Status == PaymentAttemptStatus.Paid && attempt.PaidAtUtc != null
                select new FilteredPaymentRow
                {
                    OrderId = attempt.OrderId,
                    Method = attempt.Method,
                    PaidAtUtc = attempt.PaidAtUtc!.Value,
                    Amount = attempt.Amount,
                };
        }

        var matchingTotals =
            from item in _context.OrderItems.AsNoTracking()
            join sku in _context.Skus.AsNoTracking() on item.SkuId equals (long?)sku.Id
            join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
            join category in _context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in _context.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where (query.CategoryCode == null || category.Code == query.CategoryCode) &&
                  (query.BrandCode == null || brand.Code == query.BrandCode)
            group item by item.OrderId
            into orderItems
            select new { OrderId = orderItems.Key, Amount = orderItems.Sum(item => item.LineTotal) };
        var orderItemTotals =
            from item in _context.OrderItems.AsNoTracking()
            group item by item.OrderId
            into orderItems
            select new { OrderId = orderItems.Key, Amount = orderItems.Sum(item => item.LineTotal) };

        return
            from attempt in _context.PaymentAttempts.AsNoTracking()
            join order in orders on attempt.OrderId equals order.Id
            join matching in matchingTotals on order.Id equals matching.OrderId
            join total in orderItemTotals on order.Id equals total.OrderId
            where attempt.Status == PaymentAttemptStatus.Paid && attempt.PaidAtUtc != null
            select new FilteredPaymentRow
            {
                OrderId = attempt.OrderId,
                Method = attempt.Method,
                PaidAtUtc = attempt.PaidAtUtc!.Value,
                Amount = total.Amount == 0m
                    ? 0m
                    : attempt.Amount * matching.Amount / total.Amount,
            };
    }

    private IQueryable<FilteredRefundRow> FilteredRefunds(ValidatedReportQuery query)
    {
        var orders = ApplyOrderStatusFilters(_context.Orders.AsNoTracking(), query);
        if (query.CategoryCode is null && query.BrandCode is null)
        {
            return
                from refund in _context.Refunds.AsNoTracking()
                join order in orders on refund.OrderId equals order.Id
                where refund.Status == RefundStatus.Succeeded &&
                      refund.SucceededAtUtc != null && refund.SucceededAmount != null
                select new FilteredRefundRow
                {
                    SucceededAtUtc = refund.SucceededAtUtc!.Value,
                    Amount = refund.SucceededAmount!.Value,
                };
        }

        return
            from refund in _context.Refunds.AsNoTracking()
            join order in orders on refund.OrderId equals order.Id
            join allocation in _context.RefundAllocations.AsNoTracking() on refund.Id equals allocation.RefundId
            join item in _context.OrderItems.AsNoTracking() on allocation.OrderItemId equals (long?)item.Id
            join sku in _context.Skus.AsNoTracking() on item.SkuId equals (long?)sku.Id
            join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
            join category in _context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in _context.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where refund.Status == RefundStatus.Succeeded && refund.SucceededAtUtc != null &&
                  (query.CategoryCode == null || category.Code == query.CategoryCode) &&
                  (query.BrandCode == null || brand.Code == query.BrandCode)
            select new FilteredRefundRow
            {
                SucceededAtUtc = refund.SucceededAtUtc!.Value,
                Amount = allocation.Amount,
            };
    }

    private static IQueryable<Order> ApplyOrderStatusFilters(
        IQueryable<Order> orders,
        ValidatedReportQuery query)
    {
        if (query.OrderStatuses.Count == 0)
        {
            return orders;
        }

        var statuses = query.OrderStatuses
            .Select(status => Enum.Parse<OrderStatus>(status, ignoreCase: false))
            .ToArray();
        return orders.Where(order => statuses.Contains(order.OrderStatus));
    }

    private static CursorPage<ReportRowDto> CreatePage(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        IReadOnlyList<SalesOverviewReportRowDto> rows)
    {
        var fingerprint = OpaqueCursorCodec.ComputeFingerprint(
            CursorFingerprintTag,
            definition.Key,
            query.FromDate.ToString("yyyy-MM-dd"),
            query.ToDate.ToString("yyyy-MM-dd"),
            query.TimeZone,
            query.CategoryCode,
            query.BrandCode,
            string.Join(",", query.OrderStatuses),
            query.Granularity);

        string? afterBucket = null;
        if (query.Cursor is not null)
        {
            if (!OpaqueCursorCodec.TryDecode<ReportBucketCursor>(
                    query.Cursor,
                    fingerprint,
                    out var decoded))
            {
                throw DomainProblemException.BadRequest(
                    OperationalReportErrorCodes.ReportRangeInvalid,
                    "The report cursor is invalid or no longer applicable to these filters.");
            }

            afterBucket = decoded!.Bucket;
        }

        var candidates = rows
            .Where(row => afterBucket is null ||
                string.CompareOrdinal(row.Bucket, afterBucket) > 0)
            .Take(query.PageSize + 1)
            .ToArray();
        var hasMore = candidates.Length > query.PageSize;
        var items = candidates.Take(query.PageSize).Cast<ReportRowDto>().ToArray();
        var nextCursor = hasMore && items.Length > 0
            ? OpaqueCursorCodec.Encode(
                new ReportBucketCursor(((SalesOverviewReportRowDto)items[^1]).Bucket),
                fingerprint)
            : null;

        return new CursorPage<ReportRowDto>(items, nextCursor, hasMore);
    }

    private static SalesOverviewReportRowDto ToRow(
        DateOnly bucket,
        IEnumerable<MutableSalesAggregate> parts)
    {
        var aggregate = new MutableSalesAggregate();
        foreach (var part in parts)
        {
            aggregate.PaidAmount += part.PaidAmount;
            aggregate.RefundAmount += part.RefundAmount;
            aggregate.OrderCount += part.OrderCount;
            aggregate.CreatedOrderCount += part.CreatedOrderCount;
            aggregate.CancelledOrderCount += part.CancelledOrderCount;
        }

        var netRevenue = aggregate.PaidAmount - aggregate.RefundAmount;
        return new SalesOverviewReportRowDto(
            bucket.ToString("yyyy-MM-dd"),
            netRevenue,
            aggregate.OrderCount,
            OperationalReportMath.RatioOrNull(netRevenue, aggregate.OrderCount),
            aggregate.RefundAmount,
            OperationalReportMath.RatioOrNull(aggregate.RefundAmount, aggregate.PaidAmount),
            aggregate.CancelledOrderCount,
            OperationalReportMath.RatioOrNull(
                aggregate.CancelledOrderCount,
                aggregate.CreatedOrderCount));
    }

    private static DateOnly BucketStart(DateOnly date, string granularity) => granularity switch
    {
        ReportGranularities.Day => date,
        ReportGranularities.Week => date.AddDays(-((7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7)),
        ReportGranularities.Month => new DateOnly(date.Year, date.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(granularity)),
    };

    private static DateTime ToUtcBoundary(DateOnly localDate) =>
        DateTime.SpecifyKind(
            localDate.ToDateTime(TimeOnly.MinValue).AddHours(-TaipeiUtcOffsetHours),
            DateTimeKind.Utc);

    private static DateOnly FromDayNumber(int dayNumber) =>
        DateOnly.FromDateTime(DayNumberAnchor.AddDays(dayNumber));

    private static MutableSalesAggregate GetOrCreate(
        IDictionary<DateOnly, MutableSalesAggregate> daily,
        DateOnly date)
    {
        if (!daily.TryGetValue(date, out var aggregate))
        {
            aggregate = new MutableSalesAggregate();
            daily.Add(date, aggregate);
        }

        return aggregate;
    }

    private static string PaymentMethodToken(PaymentMethod method) => method switch
    {
        PaymentMethod.CreditCard => "credit_card",
        PaymentMethod.ATM => "atm",
        PaymentMethod.ConvenienceCode => "convenience_code",
        PaymentMethod.CashOnDelivery => "cash_on_delivery",
        PaymentMethod.LinePay => "line_pay",
        PaymentMethod.ApplePay => "apple_pay",
        PaymentMethod.GooglePay => "google_pay",
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    private sealed record DailyPaymentRow(int DayNumber, decimal PaidAmount, int OrderCount);
    private sealed record PaymentMethodRow(PaymentMethod Method, decimal PaidAmount);
    private sealed record DailyRefundRow(int DayNumber, decimal RefundAmount);
    private sealed record DailyOrderCohortRow(
        int DayNumber,
        int CreatedOrderCount,
        int CancelledOrderCount);
    private sealed class FilteredPaymentRow
    {
        public long OrderId { get; init; }
        public PaymentMethod Method { get; init; }
        public DateTime PaidAtUtc { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class FilteredRefundRow
    {
        public DateTime SucceededAtUtc { get; init; }
        public decimal Amount { get; init; }
    }
    private sealed record ReportBucketCursor(string Bucket);

    private sealed class MutableSalesAggregate
    {
        public decimal PaidAmount { get; set; }
        public decimal RefundAmount { get; set; }
        public int OrderCount { get; set; }
        public int CreatedOrderCount { get; set; }
        public int CancelledOrderCount { get; set; }
    }
}
