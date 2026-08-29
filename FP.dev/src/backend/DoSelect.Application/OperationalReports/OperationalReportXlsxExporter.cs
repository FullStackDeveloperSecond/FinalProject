using System.Globalization;
using ClosedXML.Excel;
using DoSelect.Application.Common;

namespace DoSelect.Application.OperationalReports;

public sealed class OperationalReportXlsxExporter(
    IOperationalReportQueryService queryService) : IOperationalReportXlsxExporter
{
    public const int MaximumRows = OperationalReportExportLimits.MaximumRows;
    private const int ExportPageSize = OperationalReportQueryValidator.MaximumPageSize;

    public async Task<OperationalReportXlsxExport> ExportAsync(
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

        return new OperationalReportXlsxExport(
            BuildWorkbook(firstResult, query, rows),
            $"{definition.Key}-{query.FromDate:yyyyMMdd}-{query.ToDate.AddDays(-1):yyyyMMdd}.xlsx");
    }

    private static byte[] BuildWorkbook(
        ReportResultDto result,
        ValidatedReportQuery query,
        IReadOnlyList<ReportRowDto> rows)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = result.Title;
        workbook.Properties.Subject = "DEMO DATA operational report";
        WriteReadme(workbook.Worksheets.Add("README"), result, query);
        WriteSummary(workbook.Worksheets.Add("Summary"), result.Summary);
        WriteRows(workbook.Worksheets.Add("Rows"), result.ReportKey, rows);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteReadme(
        IXLWorksheet sheet,
        ReportResultDto result,
        ValidatedReportQuery query)
    {
        var metadata = new (string Label, string? Value)[]
        {
            ("DEMO DATA", null),
            ("Report Key", result.ReportKey),
            ("Report Name", result.Title),
            ("Time Basis", result.TimeBasis),
            ("Time Zone", result.TimeZone),
            ("From", result.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("To (exclusive)", result.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Category", query.CategoryCode),
            ("Brand", query.BrandCode),
            ("Order Statuses", string.Join('|', query.OrderStatuses)),
            ("Granularity", query.Granularity),
            ("Generated At UTC", result.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            ("Data As Of UTC", result.AsOfUtc.ToString("O", CultureInfo.InvariantCulture)),
        };

        for (var index = 0; index < metadata.Length; index++)
        {
            var row = index + 1;
            SetCellValue(sheet.Cell(row, 1), metadata[index].Label);
            SetCellValue(sheet.Cell(row, 2), metadata[index].Value);
        }

        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        StyleHeader(sheet.Range(2, 1, metadata.Length, 1));
        sheet.Column(1).Width = 22;
        sheet.Column(2).Width = 60;
        sheet.SheetView.FreezeRows(1);
    }

    private static void WriteSummary(
        IXLWorksheet sheet,
        IReadOnlyList<ReportMetricDto> summary)
    {
        var headers = new[] { "Metric Key", "Display Name", "Value", "Unit" };
        WriteHeaderRow(sheet, headers);
        for (var index = 0; index < summary.Count; index++)
        {
            var metric = summary[index];
            var row = index + 2;
            SetCellValue(sheet.Cell(row, 1), metric.MetricKey);
            SetCellValue(sheet.Cell(row, 2), DisplayName(metric.MetricKey));
            SetCellValue(sheet.Cell(row, 3), metric.Value);
            SetCellValue(sheet.Cell(row, 4), metric.Unit);
        }

        FinishTable(sheet, headers.Length, summary.Count + 1);
    }

    private static void WriteRows(
        IXLWorksheet sheet,
        string reportKey,
        IReadOnlyList<ReportRowDto> rows)
    {
        var headers = RowHeaders(reportKey);
        WriteHeaderRow(sheet, headers);
        for (var index = 0; index < rows.Count; index++)
        {
            var values = RowValues(reportKey, rows[index]);
            for (var column = 0; column < values.Length; column++)
            {
                SetCellValue(sheet.Cell(index + 2, column + 1), values[column]);
            }
        }

        FinishTable(sheet, headers.Length, rows.Count + 1);
    }

    private static string[] RowHeaders(string reportKey) => reportKey switch
    {
        OperationalReportKeys.SalesOverview =>
            ["Bucket", "Net Revenue", "Order Count", "Average Order Value", "Refund Amount", "Refund Amount Rate", "Cancelled Order Count", "Cancellation Rate"],
        OperationalReportKeys.ProductAbc =>
            ["SKU PublicId", "SKU Code", "SKU Name", "Quantity", "Net Revenue", "Revenue Share", "Cumulative Revenue Share", "ABC Class", "Rank"],
        OperationalReportKeys.PeriodComparison =>
            ["Metric Key", "Display Name", "Current Value", "Previous Value", "Change Rate", "Is New"],
        OperationalReportKeys.InventoryTurnover =>
            ["SKU PublicId", "SKU Code", "SKU Name", "Cost Of Goods Sold", "Beginning Inventory Cost", "Ending Inventory Cost", "Average Inventory Cost", "Turnover Rate", "Turnover Days", "Available Quantity", "Reorder Level", "Is Low Stock", "Is Out Of Stock", "Is Long Term Unsold", "Is Insufficient Data"],
        OperationalReportKeys.GrossMargin =>
            ["SKU PublicId", "SKU Code", "SKU Name", "Net Revenue", "Cost Of Goods Sold", "Gross Profit", "Gross Margin Rate", "Quantity Sold", "Refunded Quantity"],
        OperationalReportKeys.ProductAssociations =>
            ["Left SKU PublicId", "Left SKU Code", "Left SKU Name", "Right SKU PublicId", "Right SKU Code", "Right SKU Name", "Co-occurrence Order Count", "Support", "Confidence", "Lift"],
        OperationalReportKeys.ForecastAnomalies =>
            ["Date", "Actual Value", "Forecast Value", "Residual Z-Score", "Is Anomaly", "Is Insufficient Data"],
        _ => throw new InvalidOperationException($"Unsupported report key '{reportKey}'."),
    };

    private static object?[] RowValues(string reportKey, ReportRowDto value) =>
        (reportKey, value) switch
        {
            (OperationalReportKeys.SalesOverview, SalesOverviewReportRowDto row) =>
                [row.Bucket, row.NetRevenue, row.OrderCount, row.AverageOrderValue, row.RefundAmount, row.RefundAmountRate, row.CancelledOrderCount, row.CancellationRate],
            (OperationalReportKeys.ProductAbc, ProductAbcReportRowDto row) =>
                [row.SkuPublicId, row.SkuCode, row.SkuName, row.Quantity, row.NetRevenue, row.RevenueShare, row.CumulativeRevenueShare, row.AbcClass, row.Rank],
            (OperationalReportKeys.PeriodComparison, PeriodComparisonReportRowDto row) =>
                [row.MetricKey, DisplayName(row.MetricKey), row.CurrentValue, row.PreviousValue, row.ChangeRate, row.IsNew],
            (OperationalReportKeys.InventoryTurnover, InventoryTurnoverReportRowDto row) =>
                [row.SkuPublicId, row.SkuCode, row.SkuName, row.CostOfGoodsSold, row.BeginningInventoryCost, row.EndingInventoryCost, row.AverageInventoryCost, row.TurnoverRate, row.TurnoverDays, row.AvailableQuantity, row.ReorderLevel, row.IsLowStock, row.IsOutOfStock, row.IsLongTermUnsold, row.IsInsufficientData],
            (OperationalReportKeys.GrossMargin, GrossMarginReportRowDto row) =>
                [row.SkuPublicId, row.SkuCode, row.SkuName, row.NetRevenue, row.CostOfGoodsSold, row.GrossProfit, row.GrossMarginRate, row.QuantitySold, row.RefundedQuantity],
            (OperationalReportKeys.ProductAssociations, ProductAssociationReportRowDto row) =>
                [row.LeftSkuPublicId, row.LeftSkuCode, row.LeftSkuName, row.RightSkuPublicId, row.RightSkuCode, row.RightSkuName, row.CoOccurrenceOrderCount, row.Support, row.Confidence, row.Lift],
            (OperationalReportKeys.ForecastAnomalies, ForecastAnomalyReportRowDto row) =>
                [row.Date, row.ActualValue, row.ForecastValue, row.ZScore, row.IsAnomaly, row.IsInsufficientData],
            _ => throw new InvalidOperationException(
                $"The row type '{value.GetType().Name}' does not match report key '{reportKey}'."),
        };

    private static void WriteHeaderRow(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            SetCellValue(sheet.Cell(1, index + 1), headers[index]);
        }

        StyleHeader(sheet.Range(1, 1, 1, headers.Count));
    }

    private static void FinishTable(IXLWorksheet sheet, int columnCount, int lastRow)
    {
        sheet.Range(1, 1, Math.Max(1, lastRow), columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, columnCount).Width = 18;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        range.Style.Alignment.WrapText = true;
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Clear(XLClearOptions.Contents);
                break;
            case string text:
                cell.SetValue(text);
                break;
            case Guid guid:
                cell.SetValue(guid.ToString("D", CultureInfo.InvariantCulture));
                break;
            case DateOnly date:
                cell.SetValue(date.ToDateTime(TimeOnly.MinValue));
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;
            case int number:
                cell.SetValue(number);
                break;
            case decimal number:
                cell.SetValue(number);
                break;
            case bool boolean:
                cell.SetValue(boolean);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported XLSX cell value type '{value.GetType().Name}'.");
        }
    }

    private static string DisplayName(string metricKey) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(metricKey.Replace('_', ' '));
}
