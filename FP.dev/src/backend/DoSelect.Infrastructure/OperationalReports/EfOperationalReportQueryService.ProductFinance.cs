using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Refunds;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.OperationalReports;

public sealed partial class EfOperationalReportQueryService
{
    private async Task<ReportResultDto> QueryProductAbcAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var aggregates = await LoadSkuFinancialsAsync(query, cancellationToken);
        var ranked = aggregates
            .Where(aggregate => aggregate.NetRevenue > 0m)
            .OrderByDescending(aggregate => aggregate.NetRevenue)
            .ThenBy(aggregate => aggregate.SkuCode, StringComparer.Ordinal)
            .ToArray();
        var totalRevenue = ranked.Sum(aggregate => aggregate.NetRevenue);
        var rows = new ProductAbcReportRowDto[ranked.Length];
        var cumulativeRevenue = 0m;

        for (var index = 0; index < ranked.Length; index++)
        {
            var aggregate = ranked[index];
            cumulativeRevenue += aggregate.NetRevenue;
            rows[index] = new ProductAbcReportRowDto(
                aggregate.SkuPublicId,
                aggregate.SkuCode,
                aggregate.SkuName,
                aggregate.NetQuantity,
                aggregate.NetRevenue,
                aggregate.NetRevenue / totalRevenue,
                cumulativeRevenue / totalRevenue,
                OperationalReportMath.ClassifyAbc(cumulativeRevenue, totalRevenue),
                index + 1);
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
                new ReportMetricDto("net_revenue", totalRevenue, "TWD"),
                new ReportMetricDto("quantity", rows.Sum(row => row.Quantity), "count"),
                new ReportMetricDto("sku_count", rows.Length, "count"),
            ],
            [],
            CreateOffsetPage(definition, query, rows));
    }

    private async Task<ReportResultDto> QueryGrossMarginAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var aggregates = await LoadSkuFinancialsAsync(query, cancellationToken);
        var rows = aggregates
            .Select(aggregate => new GrossMarginReportRowDto(
                aggregate.SkuPublicId,
                aggregate.SkuCode,
                aggregate.SkuName,
                aggregate.NetRevenue,
                aggregate.CostOfGoodsSold,
                aggregate.NetRevenue - aggregate.CostOfGoodsSold,
                aggregate.NetRevenue == 0m
                    ? null
                    : (aggregate.NetRevenue - aggregate.CostOfGoodsSold) /
                      aggregate.NetRevenue,
                aggregate.NetQuantity,
                aggregate.RefundedQuantity))
            .OrderByDescending(row => row.GrossProfit)
            .ThenBy(row => row.SkuCode, StringComparer.Ordinal)
            .ToArray();
        var netRevenue = rows.Sum(row => row.NetRevenue);
        var costOfGoodsSold = rows.Sum(row => row.CostOfGoodsSold);
        var grossProfit = netRevenue - costOfGoodsSold;
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
                new ReportMetricDto("net_revenue", netRevenue, "TWD"),
                new ReportMetricDto("cost_of_goods_sold", costOfGoodsSold, "TWD"),
                new ReportMetricDto("gross_profit", grossProfit, "TWD"),
                new ReportMetricDto(
                    "gross_margin_rate",
                    netRevenue == 0m ? null : grossProfit / netRevenue,
                    "ratio"),
                new ReportMetricDto("quantity_sold", rows.Sum(row => row.QuantitySold), "count"),
                new ReportMetricDto(
                    "refunded_quantity",
                    rows.Sum(row => row.RefundedQuantity),
                    "count"),
            ],
            rows.Select(row => new ReportSeriesPointDto(
                row.SkuCode,
                [
                    new ReportMetricDto("net_revenue", row.NetRevenue, "TWD"),
                    new ReportMetricDto("gross_profit", row.GrossProfit, "TWD"),
                    new ReportMetricDto("gross_margin_rate", row.GrossMarginRate, "ratio"),
                ])).ToArray(),
            CreateOffsetPage(definition, query, rows));
    }

    private async Task<IReadOnlyCollection<SkuFinancialAggregate>> LoadSkuFinancialsAsync(
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var fromUtc = ToUtcBoundary(query.FromDate);
        var toUtc = ToUtcBoundary(query.ToDate);
        var statuses = query.OrderStatuses
            .Select(status => Enum.Parse<OrderStatus>(status, ignoreCase: false))
            .ToArray();

        var saleRows = await (
            from item in _context.OrderItems.AsNoTracking()
            join order in _context.Orders.AsNoTracking() on item.OrderId equals order.Id
            join sku in _context.Skus.AsNoTracking() on item.SkuId equals (long?)sku.Id
            join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
            join category in _context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in _context.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where order.CompletedAtUtc >= fromUtc &&
                  order.CompletedAtUtc < toUtc &&
                  (statuses.Length == 0 || statuses.Contains(order.OrderStatus)) &&
                  (query.CategoryCode == null || category.Code == query.CategoryCode) &&
                  (query.BrandCode == null || brand.Code == query.BrandCode)
            group item by new { sku.PublicId, sku.SkuCode, sku.NameZhTw }
            into skuSales
            select new SkuSaleRow(
                skuSales.Key.PublicId,
                skuSales.Key.SkuCode,
                skuSales.Key.NameZhTw,
                skuSales.Sum(item => item.LineTotal),
                skuSales.Sum(item => item.Quantity),
                skuSales.Sum(item => item.UnitCostSnapshot * item.Quantity)))
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
            group new { allocation, item } by new { sku.PublicId, sku.SkuCode, sku.NameZhTw }
            into skuRefunds
            select new SkuRefundRow(
                skuRefunds.Key.PublicId,
                skuRefunds.Key.SkuCode,
                skuRefunds.Key.NameZhTw,
                skuRefunds.Sum(row => row.allocation.Amount),
                skuRefunds.Sum(row => row.allocation.Quantity!.Value),
                skuRefunds.Sum(
                    row => row.item.UnitCostSnapshot * row.allocation.Quantity!.Value)))
            .ToListAsync(cancellationToken);

        var aggregates = new Dictionary<Guid, SkuFinancialAggregate>();
        foreach (var sale in saleRows)
        {
            var aggregate = GetOrCreate(aggregates, sale.SkuPublicId, sale.SkuCode, sale.SkuName);
            aggregate.SalesRevenue += sale.Revenue;
            aggregate.SoldQuantity += sale.Quantity;
            aggregate.SalesCost += sale.Cost;
        }

        foreach (var refund in refundRows)
        {
            var aggregate = GetOrCreate(
                aggregates,
                refund.SkuPublicId,
                refund.SkuCode,
                refund.SkuName);
            aggregate.RefundAmount += refund.Amount;
            aggregate.RefundedQuantity += refund.Quantity;
            aggregate.RefundedCost += refund.Cost;
        }

        return aggregates.Values;
    }

    private static SkuFinancialAggregate GetOrCreate(
        IDictionary<Guid, SkuFinancialAggregate> aggregates,
        Guid skuPublicId,
        string skuCode,
        string skuName)
    {
        if (!aggregates.TryGetValue(skuPublicId, out var aggregate))
        {
            aggregate = new SkuFinancialAggregate(skuPublicId, skuCode, skuName);
            aggregates.Add(skuPublicId, aggregate);
        }

        return aggregate;
    }

    private static CursorPage<ReportRowDto> CreateOffsetPage<T>(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        IReadOnlyList<T> rows)
        where T : ReportRowDto
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
        var offset = 0;

        if (query.Cursor is not null)
        {
            if (!OpaqueCursorCodec.TryDecode<ReportOffsetCursor>(
                    query.Cursor,
                    fingerprint,
                    out var decoded) ||
                decoded!.Offset < 0 ||
                decoded.Offset > rows.Count)
            {
                throw DomainProblemException.BadRequest(
                    OperationalReportErrorCodes.ReportRangeInvalid,
                    "The report cursor is invalid or no longer applicable to these filters.");
            }

            offset = decoded.Offset;
        }

        var candidates = rows.Skip(offset).Take(query.PageSize + 1).ToArray();
        var hasMore = candidates.Length > query.PageSize;
        var items = candidates.Take(query.PageSize).Cast<ReportRowDto>().ToArray();
        var nextCursor = hasMore
            ? OpaqueCursorCodec.Encode(
                new ReportOffsetCursor(offset + items.Length),
                fingerprint)
            : null;
        return new CursorPage<ReportRowDto>(items, nextCursor, hasMore);
    }

    private sealed record SkuSaleRow(
        Guid SkuPublicId,
        string SkuCode,
        string SkuName,
        decimal Revenue,
        int Quantity,
        decimal Cost);

    private sealed record SkuRefundRow(
        Guid SkuPublicId,
        string SkuCode,
        string SkuName,
        decimal Amount,
        int Quantity,
        decimal Cost);

    private sealed record ReportOffsetCursor(int Offset);

    private sealed class SkuFinancialAggregate(
        Guid skuPublicId,
        string skuCode,
        string skuName)
    {
        public Guid SkuPublicId { get; } = skuPublicId;
        public string SkuCode { get; } = skuCode;
        public string SkuName { get; } = skuName;
        public decimal SalesRevenue { get; set; }
        public int SoldQuantity { get; set; }
        public decimal SalesCost { get; set; }
        public decimal RefundAmount { get; set; }
        public int RefundedQuantity { get; set; }
        public decimal RefundedCost { get; set; }
        public decimal NetRevenue => SalesRevenue - RefundAmount;
        public int NetQuantity => SoldQuantity - RefundedQuantity;
        public decimal CostOfGoodsSold => SalesCost - RefundedCost;
    }
}
