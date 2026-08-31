namespace DoSelect.Application.Favorites;

/// <summary>
/// S-01 會員收藏（02-領域需求/04-客服與售後/評價收藏檢舉與模擬發票規格.md「收藏」）。
/// 沒有獨立稽核價值的 Join Row：新增／移除都是冪等，不走 RowVersion 樂觀併發。
/// </summary>
public static class FavoriteAvailabilityCodes
{
    /// <summary>商品已上架、預設 SKU 已上架且有可售庫存。</summary>
    public const string Available = "available";

    /// <summary>商品已上架，但目前無可售庫存（缺貨）——保留收藏，只是不能加入購物車。</summary>
    public const string OutOfStock = "outOfStock";

    /// <summary>商品已下架／已停售，或沒有已上架的預設 SKU——保留收藏但不可購買，不允許由收藏頁加入購物車。</summary>
    public const string Unlisted = "unlisted";
}

public sealed record FavoriteProductDto(
    Guid ProductPublicId,
    string ProductCode,
    string Name,
    decimal ListPrice,
    decimal? SalePrice,
    string Currency,
    string Availability);

public sealed record FavoriteDto(FavoriteProductDto Product, DateTime CreatedAtUtc);

public interface IFavoriteService
{
    Task<IReadOnlyList<FavoriteDto>> ListMineAsync(string memberUserId, CancellationToken cancellationToken);

    /// <summary>重複加入視為成功，且不建立第二筆（<c>Favorites</c> 複合 PK 本來就禁止重複）。</summary>
    Task<FavoriteDto> AddAsync(string memberUserId, Guid productPublicId, CancellationToken cancellationToken);

    /// <summary>冪等刪除：收藏不存在（或商品 PublicId 從未存在）都視為成功，不視為錯誤。</summary>
    Task RemoveAsync(string memberUserId, Guid productPublicId, CancellationToken cancellationToken);
}

public sealed class FavoriteWriteException : Exception
{
    public FavoriteWriteException(string code, string message) : base(message) => Code = code;

    public string Code { get; }

    public static class ErrorCodes
    {
        public const string ProductNotFound = "favorite_product_not_found";
    }
}
