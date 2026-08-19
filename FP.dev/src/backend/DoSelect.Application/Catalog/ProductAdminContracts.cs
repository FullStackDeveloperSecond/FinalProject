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

public sealed record CreateProductRequest(
    string ProductCode,
    string NameZhTw,
    Guid BrandPublicId,
    Guid CategoryPublicId,
    string? DescriptionZhTw,
    int? WarrantyMonths,
    IReadOnlyList<Guid> TagPublicIds,
    string Status);

public sealed record UpdateProductRequest(
    string NameZhTw,
    Guid BrandPublicId,
    Guid CategoryPublicId,
    string? DescriptionZhTw,
    int? WarrantyMonths,
    IReadOnlyList<Guid> TagPublicIds,
    string Status,
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
