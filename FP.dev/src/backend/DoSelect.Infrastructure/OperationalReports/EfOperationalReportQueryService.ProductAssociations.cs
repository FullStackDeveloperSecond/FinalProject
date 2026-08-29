using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.OperationalReports;

public sealed partial class EfOperationalReportQueryService
{
    private const int MinimumCoOccurrenceOrders = 5;
    private const decimal MinimumAssociationSupport = 0.01m;
    private const decimal MinimumAssociationConfidence = 0.20m;

    private async Task<ReportResultDto> QueryProductAssociationsAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var fromUtc = ToUtcBoundary(query.FromDate);
        var toUtc = ToUtcBoundary(query.ToDate);
        var statuses = query.OrderStatuses
            .Select(status => Enum.Parse<OrderStatus>(status, ignoreCase: false))
            .ToArray();
        var orderSkuRows = await (
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
            select new AssociationOrderSku(
                order.Id,
                sku.Id,
                sku.PublicId,
                sku.SkuCode,
                sku.NameZhTw))
            .Distinct()
            .ToListAsync(cancellationToken);
        var orders = orderSkuRows
            .GroupBy(row => row.OrderId)
            .Select(group => group.OrderBy(row => row.SkuCode, StringComparer.Ordinal).ToArray())
            .ToArray();
        var totalOrders = orders.Length;
        var marginalCounts = orderSkuRows
            .GroupBy(row => row.SkuId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.OrderId).Distinct().Count());
        var pairCounts = new Dictionary<(long LeftSkuId, long RightSkuId), int>();

        foreach (var order in orders)
        {
            for (var leftIndex = 0; leftIndex < order.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < order.Length; rightIndex++)
                {
                    var key = (order[leftIndex].SkuId, order[rightIndex].SkuId);
                    pairCounts[key] = pairCounts.GetValueOrDefault(key) + 1;
                }
            }
        }

        var skuById = orderSkuRows
            .GroupBy(row => row.SkuId)
            .ToDictionary(group => group.Key, group => group.First());
        var rows = new List<ProductAssociationReportRowDto>();
        if (totalOrders > 0)
        {
            foreach (var pair in pairCounts)
            {
                AddDirectionalAssociation(
                    rows,
                    skuById[pair.Key.LeftSkuId],
                    skuById[pair.Key.RightSkuId],
                    pair.Value,
                    marginalCounts,
                    totalOrders);
                AddDirectionalAssociation(
                    rows,
                    skuById[pair.Key.RightSkuId],
                    skuById[pair.Key.LeftSkuId],
                    pair.Value,
                    marginalCounts,
                    totalOrders);
            }
        }

        var orderedRows = rows
            .OrderByDescending(row => row.Support)
            .ThenByDescending(row => row.Confidence)
            .ThenBy(row => row.LeftSkuCode, StringComparer.Ordinal)
            .ThenBy(row => row.RightSkuCode, StringComparer.Ordinal)
            .ToArray();
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
                new ReportMetricDto("completed_order_count", totalOrders, "count"),
                new ReportMetricDto("directional_rule_count", orderedRows.Length, "count"),
            ],
            [],
            CreateOffsetPage(definition, query, orderedRows));
    }

    private static void AddDirectionalAssociation(
        ICollection<ProductAssociationReportRowDto> rows,
        AssociationOrderSku left,
        AssociationOrderSku right,
        int coOccurrenceOrders,
        IReadOnlyDictionary<long, int> marginalCounts,
        int totalOrders)
    {
        var support = (decimal)coOccurrenceOrders / totalOrders;
        var confidence = (decimal)coOccurrenceOrders / marginalCounts[left.SkuId];
        var rightSupport = (decimal)marginalCounts[right.SkuId] / totalOrders;
        var lift = confidence / rightSupport;
        if (coOccurrenceOrders < MinimumCoOccurrenceOrders ||
            support < MinimumAssociationSupport ||
            confidence < MinimumAssociationConfidence ||
            lift <= 1m)
        {
            return;
        }

        rows.Add(new ProductAssociationReportRowDto(
            left.SkuPublicId,
            left.SkuCode,
            left.SkuName,
            right.SkuPublicId,
            right.SkuCode,
            right.SkuName,
            coOccurrenceOrders,
            support,
            confidence,
            lift));
    }

    private sealed record AssociationOrderSku(
        long OrderId,
        long SkuId,
        Guid SkuPublicId,
        string SkuCode,
        string SkuName);
}
