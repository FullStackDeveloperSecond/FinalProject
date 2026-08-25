using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Catalog;

public sealed record ProductRef(Guid PublicId, string ProductCode, string NameZhTw);

public sealed record SpecValueInput(
    string SemanticKey,
    string ValueType,
    string? StringValue,
    decimal? DecimalValue,
    bool? BooleanValue,
    string? OptionCode);

public sealed record SkuSpecValueDto(
    string SemanticKey,
    string Label,
    string ValueType,
    string? StringValue,
    decimal? DecimalValue,
    bool? BooleanValue,
    string? OptionCode);

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
/// </summary>
public sealed record CreateSkuRequest(
    [Required, StringLength(64, MinimumLength = 1)] string SkuCode,
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    decimal ListPrice,
    decimal UnitCost,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    [Required] string Status,
    bool IsDefault,
    bool RequiresPrepayment,
    IReadOnlyList<SpecValueInput> Specifications);

public sealed record UpdateSkuRequest(
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    decimal ListPrice,
    decimal UnitCost,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
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
