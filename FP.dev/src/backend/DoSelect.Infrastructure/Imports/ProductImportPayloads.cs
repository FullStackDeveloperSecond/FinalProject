namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Normalized-payload shapes persisted as ImportRow.NormalizedPayloadJson — one record per
/// dataset, matching 匯入暫存與庫存調整設計.md's field contracts. These are what the
/// (not-yet-built) admin preview UI will eventually bind to; only normalized/validated values are
/// stored here, never the raw uploaded text (that lives separately in ImportRow.RawJson).
/// </summary>
internal sealed record ProductPayload(
    string ProductKey,
    string? ProductCode,
    string? NameZhTw,
    string? BrandCode,
    string? CategoryCode,
    string? DescriptionZhTw,
    int? WarrantyMonths,
    string? Status);

internal sealed record SkuPayload(
    string SkuKey,
    string? SkuCode,
    string ProductKey,
    string? NameZhTw,
    decimal? ListPrice,
    decimal? UnitCost,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    bool? RequiresPrepayment,
    string? Status);

internal sealed record SpecificationPayload(
    string SkuKey,
    string? SemanticKey,
    string? ValueType,
    string? StringValue,
    decimal? DecimalValue,
    bool? BooleanValue,
    string? OptionCode);
