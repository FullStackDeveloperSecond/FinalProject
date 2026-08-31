using System.Globalization;
using System.Text;
using DoSelect.Application.Common;

namespace DoSelect.Application.OperationalReports;

public sealed class OperationalReportCsvExporter(
    IOperationalReportQueryService queryService) : IOperationalReportCsvExporter
{
    public const int MaximumRows = OperationalReportExportLimits.MaximumRows;
    private const int ExportPageSize = OperationalReportQueryValidator.MaximumPageSize;

    public async Task<OperationalReportCsvExport> ExportAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(query);

        var rows = new List<ReportRowDto>();
        ReportResultDto? firstResult = null;
        string? cursor = null;
        do
        {
            var pageQuery = query with { Cursor = cursor, PageSize = ExportPageSize };
            var result = await queryService.QueryAsync(definition, pageQuery, cancellationToken);
            firstResult ??= result;
            rows.AddRange(result.Rows.Items);
            if (rows.Count > MaximumRows ||
                rows.Count == MaximumRows && result.Rows.HasMore)
            {
                throw DomainProblemException.BadRequest(
                    OperationalReportErrorCodes.ReportExportTooLarge,
                    $"The export exceeds the {MaximumRows:N0}-row limit. Narrow the report range or filters.");
            }

            cursor = result.Rows.HasMore
                ? result.Rows.NextCursor ?? throw new InvalidOperationException(
                    "A report page marked as incomplete must provide a continuation cursor.")
                : null;
        }
        while (cursor is not null);

        if (firstResult is null)
        {
            throw new InvalidOperationException("The report query returned no result envelope.");
        }

        return new OperationalReportCsvExport(
            EncodeUtf8Bom(BuildCsv(firstResult, query, rows)),
            $"{definition.Key}-{query.FromDate:yyyyMMdd}-{query.ToDate.AddDays(-1):yyyyMMdd}.csv");
    }

    private static string BuildCsv(
        ReportResultDto result,
        ValidatedReportQuery query,
        IReadOnlyList<ReportRowDto> rows)
    {
        var csv = new StringBuilder();
        Append(csv, "DEMO DATA");
        Append(csv, "Report Key", result.ReportKey);
        Append(csv, "Report Name", result.Title);
        Append(csv, "Time Basis", result.TimeBasis);
        Append(csv, "Time Zone", result.TimeZone);
        Append(csv, "From", result.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(csv, "To (exclusive)", result.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(csv, "Category", query.CategoryCode);
        Append(csv, "Brand", query.BrandCode);
        Append(csv, "Order Statuses", string.Join('|', query.OrderStatuses));
        Append(csv, "Granularity", query.Granularity);
        Append(csv, "Generated At UTC", result.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Append(csv, "Data As Of UTC", result.AsOfUtc.ToString("O", CultureInfo.InvariantCulture));
        csv.AppendLine();
        Append(csv, "SUMMARY");
        Append(csv, "Metric Key", "Display Name", "Value", "Unit");
        foreach (var metric in result.Summary)
        {
            Append(
                csv,
                metric.MetricKey,
                DisplayName(metric.MetricKey),
                Decimal(metric.Value),
                metric.Unit);
        }

        csv.AppendLine();
        Append(csv, "ROWS");
        AppendRows(csv, result.ReportKey, rows);
        return csv.ToString();
    }

    private static void AppendRows(
        StringBuilder csv,
        string reportKey,
        IReadOnlyList<ReportRowDto> rows)
    {
        switch (reportKey)
        {
            case OperationalReportKeys.SalesOverview:
                Append(csv, "Bucket", "Net Revenue", "Order Count", "Average Order Value", "Refund Amount", "Refund Amount Rate", "Cancelled Order Count", "Cancellation Rate");
                foreach (var row in rows.Cast<SalesOverviewReportRowDto>())
                    Append(csv, row.Bucket, Decimal(row.NetRevenue), Integer(row.OrderCount), Decimal(row.AverageOrderValue), Decimal(row.RefundAmount), Decimal(row.RefundAmountRate), Integer(row.CancelledOrderCount), Decimal(row.CancellationRate));
                break;
            case OperationalReportKeys.ProductAbc:
                Append(csv, "SKU PublicId", "SKU Code", "SKU Name", "Quantity", "Net Revenue", "Revenue Share", "Cumulative Revenue Share", "ABC Class", "Rank");
                foreach (var row in rows.Cast<ProductAbcReportRowDto>())
                    Append(csv, row.SkuPublicId.ToString(), row.SkuCode, row.SkuName, Integer(row.Quantity), Decimal(row.NetRevenue), Decimal(row.RevenueShare), Decimal(row.CumulativeRevenueShare), row.AbcClass, Integer(row.Rank));
                break;
            case OperationalReportKeys.PeriodComparison:
                Append(csv, "Metric Key", "Display Name", "Current Value", "Previous Value", "Change Rate", "Is New");
                foreach (var row in rows.Cast<PeriodComparisonReportRowDto>())
                    Append(csv, row.MetricKey, DisplayName(row.MetricKey), Decimal(row.CurrentValue), Decimal(row.PreviousValue), Decimal(row.ChangeRate), Boolean(row.IsNew));
                break;
            case OperationalReportKeys.InventoryTurnover:
                Append(csv, "SKU PublicId", "SKU Code", "SKU Name", "Cost Of Goods Sold", "Beginning Inventory Cost", "Ending Inventory Cost", "Average Inventory Cost", "Turnover Rate", "Turnover Days", "Available Quantity", "Reorder Level", "Is Low Stock", "Is Out Of Stock", "Is Long Term Unsold", "Is Insufficient Data");
                foreach (var row in rows.Cast<InventoryTurnoverReportRowDto>())
                    Append(csv, row.SkuPublicId.ToString(), row.SkuCode, row.SkuName, Decimal(row.CostOfGoodsSold), Decimal(row.BeginningInventoryCost), Decimal(row.EndingInventoryCost), Decimal(row.AverageInventoryCost), Decimal(row.TurnoverRate), Decimal(row.TurnoverDays), Integer(row.AvailableQuantity), Integer(row.ReorderLevel), Boolean(row.IsLowStock), Boolean(row.IsOutOfStock), Boolean(row.IsLongTermUnsold), Boolean(row.IsInsufficientData));
                break;
            case OperationalReportKeys.GrossMargin:
                Append(csv, "SKU PublicId", "SKU Code", "SKU Name", "Net Revenue", "Cost Of Goods Sold", "Gross Profit", "Gross Margin Rate", "Quantity Sold", "Refunded Quantity");
                foreach (var row in rows.Cast<GrossMarginReportRowDto>())
                    Append(csv, row.SkuPublicId.ToString(), row.SkuCode, row.SkuName, Decimal(row.NetRevenue), Decimal(row.CostOfGoodsSold), Decimal(row.GrossProfit), Decimal(row.GrossMarginRate), Integer(row.QuantitySold), Integer(row.RefundedQuantity));
                break;
            case OperationalReportKeys.ProductAssociations:
                Append(csv, "Left SKU PublicId", "Left SKU Code", "Left SKU Name", "Right SKU PublicId", "Right SKU Code", "Right SKU Name", "Co-occurrence Order Count", "Support", "Confidence", "Lift");
                foreach (var row in rows.Cast<ProductAssociationReportRowDto>())
                    Append(csv, row.LeftSkuPublicId.ToString(), row.LeftSkuCode, row.LeftSkuName, row.RightSkuPublicId.ToString(), row.RightSkuCode, row.RightSkuName, Integer(row.CoOccurrenceOrderCount), Decimal(row.Support), Decimal(row.Confidence), Decimal(row.Lift));
                break;
            case OperationalReportKeys.ForecastAnomalies:
                Append(csv, "Date", "Actual Value", "Forecast Value", "Residual Z-Score", "Is Anomaly", "Is Insufficient Data");
                foreach (var row in rows.Cast<ForecastAnomalyReportRowDto>())
                    Append(csv, row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Decimal(row.ActualValue), Decimal(row.ForecastValue), Decimal(row.ZScore), Boolean(row.IsAnomaly), Boolean(row.IsInsufficientData));
                break;
            default:
                throw new InvalidOperationException($"Unsupported report key '{reportKey}'.");
        }
    }

    private static void Append(StringBuilder csv, params string?[] cells)
    {
        csv.AppendJoin(',', cells.Select(Escape));
        csv.AppendLine();
    }

    private static string Escape(string? value)
    {
        var safe = value ?? string.Empty;
        var first = safe.AsSpan().TrimStart();
        if (!first.IsEmpty && first[0] is '=' or '+' or '-' or '@')
        {
            safe = "'" + safe;
        }

        return '"' + safe.Replace("\"", "\"\"") + '"';
    }

    private static string Decimal(decimal? value) =>
        value?.ToString("0.############################", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Boolean(bool value) => value ? "true" : "false";

    private static string DisplayName(string metricKey) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(metricKey.Replace('_', ' '));

    private static byte[] EncodeUtf8Bom(string value)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = Encoding.UTF8.GetBytes(value);
        var result = new byte[preamble.Length + bytes.Length];
        preamble.CopyTo(result, 0);
        bytes.CopyTo(result, preamble.Length);
        return result;
    }
}
