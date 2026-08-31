using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DoSelect.Application.Favorites;

namespace DoSelect.Api.Contracts.Favorites;

public sealed record FavoriteProductResponse(
    Guid ProductPublicId,
    string ProductCode,
    string Name,
    decimal ListPrice,
    decimal? SalePrice,
    string Currency,
    string Availability)
{
    public static FavoriteProductResponse From(FavoriteProductDto dto) => new(
        dto.ProductPublicId,
        dto.ProductCode,
        dto.Name,
        dto.ListPrice,
        dto.SalePrice,
        dto.Currency,
        dto.Availability);
}

public sealed record FavoriteResponse(FavoriteProductResponse Product, DateTime CreatedAtUtc)
{
    public static FavoriteResponse From(FavoriteDto dto) => new(
        FavoriteProductResponse.From(dto.Product),
        dto.CreatedAtUtc);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AddFavoriteRequest
{
    [Required]
    public Guid ProductPublicId { get; init; }
}
