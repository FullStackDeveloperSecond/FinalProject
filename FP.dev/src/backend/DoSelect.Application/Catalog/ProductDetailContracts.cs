using DoSelect.Domain.Members;

namespace DoSelect.Application.Catalog;

public sealed record TagRef(string Code, string Name);

public sealed record ProductImageDto(string Url, string Alt, int Width, int Height, bool IsPrimary);

public sealed record SkuDimensionsSummary(decimal? WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);

public sealed record SkuSpecificationSummary(string SemanticKey, string Label, string? Unit, string Value);

public sealed record PublicSkuDto(
    Guid PublicId,
    string SkuCode,
    string Name,
    ProductPrice Price,
    string Availability,
    int MaxPurchasableQuantity,
    IReadOnlyList<SkuSpecificationSummary> Specifications,
    SkuDimensionsSummary Dimensions,
    bool IsDefault);

public sealed record SpecificationGroupValue(Guid SkuPublicId, string Value);

public sealed record SpecificationGroupDto(
    string SemanticKey,
    string Label,
    string? Unit,
    IReadOnlyList<SpecificationGroupValue> Values);

public sealed record ShippingRestrictionDto(string Method, bool Allowed, string? ReasonCode);

public sealed record ProductDetailDto(
    Guid ProductPublicId,
    Guid DefaultSkuPublicId,
    string ProductCode,
    string SkuCode,
    string Name,
    ProductBrandRef Brand,
    ProductCategoryRef Category,
    ProductPrice Price,
    string Availability,
    ProductImageSummary? PrimaryImage,
    IReadOnlyList<string> Badges,
    string? Description,
    IReadOnlyList<TagRef> Tags,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<PublicSkuDto> Skus,
    IReadOnlyList<SpecificationGroupDto> SpecificationGroups,
    IReadOnlyList<ShippingRestrictionDto> ShippingRestrictions,
    int? WarrantyMonths);

public interface IProductDetailService
{
    Task<ProductDetailDto?> GetByPublicIdAsync(
        Guid productPublicId,
        SupportedLocale locale,
        CancellationToken cancellationToken);
}
