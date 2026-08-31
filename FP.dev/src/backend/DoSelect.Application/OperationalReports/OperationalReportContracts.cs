using System.Text.Json.Serialization;
using DoSelect.Application.Common;

namespace DoSelect.Application.OperationalReports;

public static class OperationalReportErrorCodes
{
    public const string ReportKeyInvalid = "report_key_invalid";
    public const string ReportRangeInvalid = "report_range_invalid";
    public const string ReportExportTooLarge = "report_export_too_large";
}

public static class OperationalReportKeys
{
    public const string SalesOverview = "sales-overview";
    public const string ProductAbc = "product-abc";
    public const string PeriodComparison = "period-comparison";
    public const string InventoryTurnover = "inventory-turnover";
    public const string GrossMargin = "gross-margin";
    public const string ProductAssociations = "product-associations";
    public const string ForecastAnomalies = "forecast-anomalies";
}

public static class ReportGranularities
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly([Day, Week, Month]);
}

public enum OperationalReportSensitivity
{
    General,
    Financial,
}

public sealed record OperationalReportDefinition(
    string Key,
    string Title,
    string TimeBasis,
    OperationalReportSensitivity Sensitivity);

public static class OperationalReportCatalog
{
    public static IReadOnlyList<OperationalReportDefinition> All { get; } =
        Array.AsReadOnly<OperationalReportDefinition>(
    [
        new(
            OperationalReportKeys.SalesOverview,
            "銷售總覽",
            "Payment.PaidAtUtc / Refund.SucceededAtUtc",
            OperationalReportSensitivity.General),
        new(
            OperationalReportKeys.ProductAbc,
            "商品排行與 ABC 分級",
            "Order.CompletedAtUtc / Refund.SucceededAtUtc",
            OperationalReportSensitivity.General),
        new(
            OperationalReportKeys.PeriodComparison,
            "同期比較",
            "Payment.PaidAtUtc / Refund.SucceededAtUtc",
            OperationalReportSensitivity.General),
        new(
            OperationalReportKeys.InventoryTurnover,
            "庫存周轉分析",
            "Order.CompletedAtUtc",
            OperationalReportSensitivity.Financial),
        new(
            OperationalReportKeys.GrossMargin,
            "毛利分析",
            "Order.CompletedAtUtc / Refund.SucceededAtUtc",
            OperationalReportSensitivity.Financial),
        new(
            OperationalReportKeys.ProductAssociations,
            "關聯組合分析",
            "Order.CompletedAtUtc",
            OperationalReportSensitivity.General),
        new(
            OperationalReportKeys.ForecastAnomalies,
            "預測與異常偵測",
            "Payment.PaidAtUtc / Refund.SucceededAtUtc",
            OperationalReportSensitivity.General),
    ]);

    public static OperationalReportDefinition Require(string? reportKey)
    {
        var definition = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, reportKey, StringComparison.Ordinal));

        return definition ?? throw DomainProblemException.BadRequest(
            OperationalReportErrorCodes.ReportKeyInvalid,
            "The report key is not in the approved operational-report whitelist.");
    }
}

public sealed record ReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string TimeZone,
    string? CategoryCode,
    string? BrandCode,
    IReadOnlyList<string>? OrderStatuses,
    string Granularity,
    string? Cursor,
    int PageSize);

public sealed record ValidatedReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string TimeZone,
    string? CategoryCode,
    string? BrandCode,
    IReadOnlyList<string> OrderStatuses,
    string Granularity,
    string? Cursor,
    int PageSize);

public sealed record ReportMetricDto(string MetricKey, decimal? Value, string Unit);

public sealed record ReportSeriesPointDto(
    string Bucket,
    IReadOnlyList<ReportMetricDto> Metrics);

public sealed record ReportResultDto(
    string ReportKey,
    string Title,
    string TimeBasis,
    string TimeZone,
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<ReportMetricDto> Summary,
    IReadOnlyList<ReportSeriesPointDto> Series,
    CursorPage<ReportRowDto> Rows);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "rowType")]
[JsonDerivedType(typeof(SalesOverviewReportRowDto), OperationalReportKeys.SalesOverview)]
[JsonDerivedType(typeof(ProductAbcReportRowDto), OperationalReportKeys.ProductAbc)]
[JsonDerivedType(typeof(PeriodComparisonReportRowDto), OperationalReportKeys.PeriodComparison)]
[JsonDerivedType(typeof(InventoryTurnoverReportRowDto), OperationalReportKeys.InventoryTurnover)]
[JsonDerivedType(typeof(GrossMarginReportRowDto), OperationalReportKeys.GrossMargin)]
[JsonDerivedType(typeof(ProductAssociationReportRowDto), OperationalReportKeys.ProductAssociations)]
[JsonDerivedType(typeof(ForecastAnomalyReportRowDto), OperationalReportKeys.ForecastAnomalies)]
public abstract record ReportRowDto;

public sealed record SalesOverviewReportRowDto(
    string Bucket,
    decimal NetRevenue,
    int OrderCount,
    decimal? AverageOrderValue,
    decimal RefundAmount,
    decimal? RefundAmountRate,
    int CancelledOrderCount,
    decimal? CancellationRate) : ReportRowDto;

public sealed record ProductAbcReportRowDto(
    Guid SkuPublicId,
    string SkuCode,
    string SkuName,
    int Quantity,
    decimal NetRevenue,
    decimal RevenueShare,
    decimal CumulativeRevenueShare,
    string AbcClass,
    int Rank) : ReportRowDto;

public sealed record PeriodComparisonReportRowDto(
    string MetricKey,
    decimal? CurrentValue,
    decimal? PreviousValue,
    decimal? ChangeRate,
    bool IsNew) : ReportRowDto;

public sealed record InventoryTurnoverReportRowDto(
    Guid SkuPublicId,
    string SkuCode,
    string SkuName,
    decimal CostOfGoodsSold,
    decimal? BeginningInventoryCost,
    decimal? EndingInventoryCost,
    decimal? AverageInventoryCost,
    decimal? TurnoverRate,
    decimal? TurnoverDays,
    int AvailableQuantity,
    int ReorderLevel,
    bool IsLowStock,
    bool IsOutOfStock,
    bool IsLongTermUnsold,
    bool IsInsufficientData) : ReportRowDto;

public sealed record GrossMarginReportRowDto(
    Guid SkuPublicId,
    string SkuCode,
    string SkuName,
    decimal NetRevenue,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal? GrossMarginRate,
    int QuantitySold,
    int RefundedQuantity) : ReportRowDto;

public sealed record ProductAssociationReportRowDto(
    Guid LeftSkuPublicId,
    string LeftSkuCode,
    string LeftSkuName,
    Guid RightSkuPublicId,
    string RightSkuCode,
    string RightSkuName,
    int CoOccurrenceOrderCount,
    decimal Support,
    decimal Confidence,
    decimal Lift) : ReportRowDto;

public sealed record ForecastAnomalyReportRowDto(
    DateOnly Date,
    decimal? ActualValue,
    decimal? ForecastValue,
    decimal? ZScore,
    bool IsAnomaly,
    bool IsInsufficientData) : ReportRowDto;

public interface IOperationalReportQueryService
{
    Task<ReportResultDto> QueryAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken);
}

public sealed record OperationalReportCsvExport(byte[] Content, string FileName);

public static class OperationalReportExportLimits
{
    public const int MaximumRows = 100_000;
}

public interface IOperationalReportCsvExporter
{
    Task<OperationalReportCsvExport> ExportAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken);
}

public sealed record OperationalReportXlsxExport(byte[] Content, string FileName);

public interface IOperationalReportXlsxExporter
{
    Task<OperationalReportXlsxExport> ExportAsync(
        OperationalReportDefinition definition,
        ValidatedReportQuery query,
        CancellationToken cancellationToken);
}
