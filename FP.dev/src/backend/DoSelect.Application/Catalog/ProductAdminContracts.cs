using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;

namespace DoSelect.Application.Catalog;

public sealed record AdminProductQuery(
    string? Q,
    IReadOnlyList<string>? BrandCodes,
    IReadOnlyList<string>? CategoryCodes,
    IReadOnlyList<string>? Statuses,
    string? StockState,
    string? Sort,
    int PageNumber,
    int PageSize);

public static class AdminProductSortOptions
{
    public const string UpdatedDesc = "updatedDesc";
    public const string UpdatedAsc = "updatedAsc";
    public const string CodeAsc = "codeAsc";
    public const string CodeDesc = "codeDesc";

    public static readonly IReadOnlyCollection<string> All =
    [
        UpdatedDesc,
        UpdatedAsc,
        CodeAsc,
        CodeDesc,
    ];
}

public static class AdminStockStates
{
    public const string Any = "any";
    public const string InStock = "inStock";
    public const string OutOfStock = "outOfStock";

    public static readonly IReadOnlyCollection<string> All = [Any, InStock, OutOfStock];
}

public sealed record AdminProductSummaryDto(
    Guid PublicId,
    string ProductCode,
    string NameZhTw,
    ProductBrandRef Brand,
    ProductCategoryRef Category,
    string Status,
    int SkuCount,
    decimal MinPrice,
    decimal MaxPrice,
    int TotalOnHandQuantity,
    ProductImageSummary? PrimaryImage,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

/// <summary>
/// Length limits mirror the EF configuration in CatalogConfigurations.cs (Product.ProductCode
/// nvarchar(64), NameZhTw nvarchar(160), DescriptionZhTw nvarchar(4000)) — enforced here so an
/// over-long value gets a stable 400 validation_failed at the API boundary instead of riding
/// through to a SQL Server truncation DbUpdateException (500).
/// </summary>
public sealed record CreateProductRequest(
    [Required, StringLength(64, MinimumLength = 1)] string ProductCode,
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    Guid BrandPublicId,
    Guid CategoryPublicId,
    [StringLength(4000)] string? DescriptionZhTw,
    int? WarrantyMonths,
    IReadOnlyList<Guid> TagPublicIds,
    [Required] string Status,
    [Required] CreateSkuRequest DefaultSku);

public sealed record UpdateProductRequest(
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    Guid BrandPublicId,
    Guid CategoryPublicId,
    [StringLength(4000)] string? DescriptionZhTw,
    int? WarrantyMonths,
    IReadOnlyList<Guid> TagPublicIds,
    [Required] string Status,
    byte[] RowVersion);

public sealed record AdminProductDetailDto(
    Guid PublicId,
    string ProductCode,
    string NameZhTw,
    ProductBrandRef Brand,
    ProductCategoryRef Category,
    string? DescriptionZhTw,
    int? WarrantyMonths,
    string Status,
    bool IsFeatured,
    IReadOnlyList<TagRef> Tags,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<SkuDto> Skus,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

public interface IProductAdminService
{
    Task<PageResult<AdminProductSummaryDto>> ListAsync(
        AdminProductQuery query,
        CancellationToken cancellationToken);

    Task<AdminProductDetailDto?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    Task<AdminProductDetailDto> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken);

    Task<AdminProductDetailDto> UpdateAsync(
        Guid publicId,
        UpdateProductRequest request,
        CancellationToken cancellationToken);
}
