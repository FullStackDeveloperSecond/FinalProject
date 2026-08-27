using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Catalog;

public sealed record ProductRef(Guid PublicId, string ProductCode, string NameZhTw);

public sealed record SpecValueInput(
    string SemanticKey,
    string ValueType,
    string? StringValue,
    decimal? DecimalValue,
    bool? BooleanValue,
    string? OptionCode,
    IReadOnlyList<string>? OptionCodes = null,
    Guid? SpecificationSourcePublicId = null);

public sealed record SkuSpecValueDto(
    string SemanticKey,
    string Label,
    string ValueType,
    string? StringValue,
    decimal? DecimalValue,
    bool? BooleanValue,
    string? OptionCode,
    IReadOnlyList<string>? OptionCodes = null,
    Guid? SpecificationSourcePublicId = null);

public sealed record SkuInventorySummary(int OnHandQuantity, int ReservedQuantity, int AvailableQuantity);

public sealed record SkuDto(
    Guid PublicId,
    string SkuCode,
    ProductRef Product,
    string NameZhTw,
    decimal ListPrice,
    decimal UnitCost,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    string Status,
    bool IsDefault,
    bool RequiresPrepayment,
    IReadOnlyList<SkuSpecValueDto> Specifications,
    SkuInventorySummary? Inventory,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

/// <summary>
/// Length limits mirror the EF configuration in CatalogConfigurations.cs (Sku.SkuCode
/// nvarchar(64), NameZhTw nvarchar(160)) — enforced here so an over-long value gets a stable
/// 400 validation_failed at the API boundary instead of riding through to a SQL Server
/// truncation DbUpdateException (500).
///
/// Numeric bounds use the <c>[Range(typeof(decimal), min, max)]</c> overload, not
/// <c>[Range(double, double)]</c> — 組長 PR #24 round 5 review, item 3: a double-typed Range
/// silently loses precision converting back to decimal at these magnitudes, and
/// double.MaxValue is nowhere near SQL Server's actual column limits, so an over-large value
/// would still ride through to a DbUpdateException (500) instead of a 400. Bounds mirror
/// CatalogConfigurations.cs's HasPrecision calls exactly: ListPrice/UnitCost decimal(18,2),
/// WeightKg decimal(10,3), Length/Width/HeightCm decimal(10,2). The Domain guards
/// (CatalogGuard.NonNegative / OptionalPositive) only require price >=0 and dimensions >0 with
/// no upper bound of their own — the *lower* bound for the nullable dimensions is the smallest
/// value the column can actually store at its configured scale (0.001 / 0.01), not an
/// arbitrary "practical minimum".
/// </summary>
public sealed record CreateSkuRequest(
    [Required, StringLength(64, MinimumLength = 1)] string SkuCode,
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    [Range(typeof(decimal), "0", "9999999999999999.99")] decimal ListPrice,
    [Range(typeof(decimal), "0", "9999999999999999.99")] decimal UnitCost,
    [Range(typeof(decimal), "0.001", "9999999.999")] decimal? WeightKg,
    [Range(typeof(decimal), "0.01", "99999999.99")] decimal? LengthCm,
    [Range(typeof(decimal), "0.01", "99999999.99")] decimal? WidthCm,
    [Range(typeof(decimal), "0.01", "99999999.99")] decimal? HeightCm,
    [Required] string Status,
    bool IsDefault,
    bool RequiresPrepayment,
    IReadOnlyList<SpecValueInput> Specifications);

public sealed record UpdateSkuRequest(
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    [Range(typeof(decimal), "0", "9999999999999999.99")] decimal ListPrice,
    [Range(typeof(decimal), "0", "9999999999999999.99")] decimal UnitCost,
    [Range(typeof(decimal), "0.001", "9999999.999")] decimal? WeightKg,
    [Range(typeof(decimal), "0.01", "99999999.99")] decimal? LengthCm,
    [Range(typeof(decimal), "0.01", "99999999.99")] decimal? WidthCm,
    [Range(typeof(decimal), "0.01", "99999999.99")] decimal? HeightCm,
    [Required] string Status,
    bool IsDefault,
    bool RequiresPrepayment,
    IReadOnlyList<SpecValueInput> Specifications,
    byte[] RowVersion);

public interface ISkuAdminService
{
    Task<SkuDto> CreateAsync(
        Guid productPublicId,
        CreateSkuRequest request,
        CancellationToken cancellationToken);

    Task<SkuDto?> GetByPublicIdAsync(Guid skuPublicId, CancellationToken cancellationToken);

    Task<SkuDto> UpdateAsync(
        Guid skuPublicId,
        UpdateSkuRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid skuPublicId, byte[] rowVersion, CancellationToken cancellationToken);
}
