using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;

namespace DoSelect.Application.Favorites;

public sealed record FavoritesListQuery
{
    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed record FavoriteItemDto(
    Guid ProductPublicId,
    string ProductCode,
    string Name,
    ProductBrandRef Brand,
    ProductCategoryRef Category,
    ProductPrice? Price,
    ProductImageSummary? PrimaryImage,
    string Availability,
    bool IsPurchasable,
    DateTime CreatedAtUtc);

/// <summary>
/// A favorited product keeps its own <see cref="ProductAvailabilityCodes"/> stock state
/// (inStock/lowStock/outOfStock) while listed, but 評價收藏檢舉與模擬發票規格.md also requires a
/// distinct "unavailable, cannot add to cart" state once the product is delisted — that fourth
/// state has no place in the catalog module's own availability codes (search/detail never
/// return delisted products at all), so it is defined here instead.
/// </summary>
public static class FavoriteAvailabilityCodes
{
    public const string Delisted = "delisted";
}

public enum AddFavoriteResult
{
    Success,
    ProductNotFound,
}

public interface IFavoriteGateway
{
    Task<AddFavoriteResult> AddAsync(
        string memberUserId,
        Guid productPublicId,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        string memberUserId,
        Guid productPublicId,
        CancellationToken cancellationToken);

    Task<PageResult<FavoriteItemDto>> ListAsync(
        string memberUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
}
