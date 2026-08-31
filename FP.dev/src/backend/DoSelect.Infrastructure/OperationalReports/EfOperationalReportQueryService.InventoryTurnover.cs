using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.OperationalReports;

public sealed partial class EfOperationalReportQueryService
{
    private async Task<ReportResultDto> QueryInventoryTurnoverAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        var fromUtc = ToUtcBoundary(query.FromDate);
        var toUtc = ToUtcBoundary(query.ToDate);
        var skuRows = await (
            from balance in _context.InventoryBalances.AsNoTracking()
            join sku in _context.Skus.AsNoTracking() on balance.SkuId equals sku.Id
            join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
            join category in _context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in _context.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where sku.Status == SkuStatus.Published &&
                  (query.CategoryCode == null || category.Code == query.CategoryCode) &&
                  (query.BrandCode == null || brand.Code == query.BrandCode)
            select new InventorySkuRow(
                sku.Id,
                sku.PublicId,
                sku.SkuCode,
                sku.NameZhTw,
                sku.CreatedAtUtc,
                balance.AvailableQuantity,
                balance.ReorderLevel))
            .ToListAsync(cancellationToken);
        var skuIds = skuRows.Select(row => row.SkuId).ToArray();

        var movements = skuIds.Length == 0
            ? []
            : await _context.InventoryMovements.AsNoTracking()
                .Where(movement =>
                    skuIds.Contains(movement.SkuId) && movement.OccurredAtUtc < toUtc)
                .OrderBy(movement => movement.SkuId)
                .ThenBy(movement => movement.OccurredAtUtc)
                .ThenBy(movement => movement.Id)
                .Select(movement => new InventoryValuationMovement(
                    movement.SkuId,
                    movement.MovementType,
                    movement.AfterOnHand,
                    movement.UnitCostSnapshot,
                    movement.OccurredAtUtc,
                    movement.Id))
                .ToListAsync(cancellationToken);
        var movementsBySku = movements
            .GroupBy(movement => movement.SkuId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var financials = (await LoadSkuFinancialsAsync(query, cancellationToken))
            .ToDictionary(row => row.SkuPublicId);
        var recentSaleCutoffUtc = toUtc.AddDays(-60);
        var recentlySoldSkuIds = skuIds.Length == 0
            ? []
            : await (
                from item in _context.OrderItems.AsNoTracking()
                join order in _context.Orders.AsNoTracking() on item.OrderId equals order.Id
                where item.SkuId != null &&
                      skuIds.Contains(item.SkuId.Value) &&
                      order.CompletedAtUtc >= recentSaleCutoffUtc &&
                      order.CompletedAtUtc < toUtc
                select item.SkuId.GetValueOrDefault())
                .Distinct()
                .ToListAsync(cancellationToken);
        var recentlySold = recentlySoldSkuIds.ToHashSet();
        var periodDays = query.ToDate.DayNumber - query.FromDate.DayNumber;

        var rows = skuRows.Select(sku =>
        {
            movementsBySku.TryGetValue(sku.SkuId, out var skuMovements);
            var beginningCost = InventoryCost(skuMovements, fromUtc);
            var endingCost = InventoryCost(skuMovements, toUtc);
            decimal? averageCost = beginningCost is not null && endingCost is not null
                ? (beginningCost.Value + endingCost.Value) / 2m
                : null;
            var costOfGoodsSold = financials.GetValueOrDefault(sku.SkuPublicId)?.CostOfGoodsSold ?? 0m;
            decimal? turnoverRate = averageCost is > 0m
                ? costOfGoodsSold / averageCost.Value
                : null;
            decimal? turnoverDays = turnoverRate is > 0m
                ? periodDays / turnoverRate.Value
                : null;
            var isInsufficientData = beginningCost is null || endingCost is null;

            return new InventoryTurnoverReportRowDto(
                sku.SkuPublicId,
                sku.SkuCode,
                sku.SkuName,
                costOfGoodsSold,
                beginningCost,
                endingCost,
                averageCost,
                isInsufficientData ? null : turnoverRate,
                isInsufficientData ? null : turnoverDays,
                sku.AvailableQuantity,
                sku.ReorderLevel,
                sku.AvailableQuantity <= sku.ReorderLevel,
                sku.AvailableQuantity == 0,
                sku.CreatedAtUtc <= recentSaleCutoffUtc && !recentlySold.Contains(sku.SkuId),
                isInsufficientData);
        })
        .OrderByDescending(row => row.IsLowStock)
        .ThenBy(row => row.SkuCode, StringComparer.Ordinal)
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
                new ReportMetricDto("cost_of_goods_sold", rows.Sum(row => row.CostOfGoodsSold), "TWD"),
                new ReportMetricDto("sku_count", rows.Length, "count"),
                new ReportMetricDto("low_stock_count", rows.Count(row => row.IsLowStock), "count"),
                new ReportMetricDto("out_of_stock_count", rows.Count(row => row.IsOutOfStock), "count"),
                new ReportMetricDto("long_term_unsold_count", rows.Count(row => row.IsLongTermUnsold), "count"),
                new ReportMetricDto("insufficient_data_count", rows.Count(row => row.IsInsufficientData), "count"),
            ],
            rows.Select(row => new ReportSeriesPointDto(
                row.SkuCode,
                [
                    new ReportMetricDto("turnover_rate", row.TurnoverRate, "ratio"),
                    new ReportMetricDto("turnover_days", row.TurnoverDays, "day"),
                    new ReportMetricDto("available_quantity", row.AvailableQuantity, "count"),
                ])).ToArray(),
            CreateOffsetPage(definition, query, rows));
    }

    private static decimal? InventoryCost(
        IReadOnlyList<InventoryValuationMovement>? movements,
        DateTime exclusiveBoundaryUtc)
    {
        if (movements is null)
        {
            return null;
        }

        var quantityMovement = movements.LastOrDefault(movement =>
            movement.OccurredAtUtc < exclusiveBoundaryUtc &&
            !string.Equals(movement.MovementType, "CostChange", StringComparison.Ordinal));
        var costMovement = movements.LastOrDefault(movement =>
            movement.OccurredAtUtc < exclusiveBoundaryUtc &&
            movement.UnitCostSnapshot is not null);
        return quantityMovement is not null && costMovement?.UnitCostSnapshot is { } unitCost
            ? quantityMovement.AfterOnHand * unitCost
            : null;
    }

    private sealed record InventorySkuRow(
        long SkuId,
        Guid SkuPublicId,
        string SkuCode,
        string SkuName,
        DateTime CreatedAtUtc,
        int AvailableQuantity,
        int ReorderLevel);

    private sealed record InventoryValuationMovement(
        long SkuId,
        string MovementType,
        int AfterOnHand,
        decimal? UnitCostSnapshot,
        DateTime OccurredAtUtc,
        long Id);
}
