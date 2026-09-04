using DoSelect.Application.Catalog;
using DoSelect.Application.Files;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

/// <summary>
/// 商品圖片投影到公開／後台 DTO 的唯一地方。公開面只看 Published（SH-06：只有已發布的圖片能由
/// 公開路由讀取），後台面看所有未刪除的圖片。之前四個服務各留一個「SH-06 未就緒」的 null／[]，
/// 現在都改走這裡。
/// </summary>
internal static class ProductImageProjection
{
    /// <summary>每個商品排序最前的 Published 圖片，做商品卡與列表的主圖（320）。</summary>
    public static async Task<Dictionary<long, ProductImageSummary>> LoadPublishedPrimaryAsync(
        DoSelectDbContext dbContext,
        IReadOnlyCollection<long> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<long, ProductImageSummary>();
        }

        var rows = await dbContext.ProductImages.AsNoTracking()
            .Where(image => productIds.Contains(image.ProductId) && image.Status == ProductImageStatus.Published &&
                image.SmallSha256 != null && image.MediumSha256 != null && image.LargeSha256 != null)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAtUtc)
            .ThenBy(image => image.Id)
            .Select(image => new PublishedImageRow(
                image.ProductId, image.PublicId, image.AltTextZhTw, image.Width, image.Height, image.SmallSha256, image.MediumSha256))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProductId)
            .ToDictionary(group => group.Key, group => ToSummary(group.First(), ProductImageVariant.Small320));
    }

    /// <summary>商品詳情的圖庫（800）與主圖。</summary>
    public static async Task<(ProductImageSummary? Primary, IReadOnlyList<ProductImageDto> Images)> LoadPublishedGalleryAsync(
        DoSelectDbContext dbContext,
        long productId,
        CancellationToken cancellationToken)
    {
        // 組長 PR #101 item 2：既有 Published 資料若缺任何一個衍生圖雜湊（Migration 之前的列），媒體
        // 端點一定 404——那種列不投影，不輸出假的 URL。
        var rows = await dbContext.ProductImages.AsNoTracking()
            .Where(image => image.ProductId == productId && image.Status == ProductImageStatus.Published &&
                image.SmallSha256 != null && image.MediumSha256 != null && image.LargeSha256 != null)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAtUtc)
            .ThenBy(image => image.Id)
            .Select(image => new PublishedImageRow(
                image.ProductId, image.PublicId, image.AltTextZhTw, image.Width, image.Height, image.SmallSha256, image.MediumSha256))
            .ToListAsync(cancellationToken);

        var images = rows
            .Select((row, index) =>
            {
                var summary = ToSummary(row, ProductImageVariant.Medium800);
                return new ProductImageDto(summary.Url, summary.Alt, summary.Width, summary.Height, index == 0);
            })
            .ToList();
        return (rows.Count == 0 ? null : ToSummary(rows[0], ProductImageVariant.Medium800), images);
    }

    /// <summary>後台列表主圖：未刪除、排序最前的一張，走授權預覽路由（未發布的圖也看得到）。</summary>
    public static async Task<Dictionary<long, ProductImageSummary>> LoadAdminPrimaryAsync(
        DoSelectDbContext dbContext,
        IReadOnlyCollection<long> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<long, ProductImageSummary>();
        }

        var rows = await dbContext.ProductImages.AsNoTracking()
            .Where(image => productIds.Contains(image.ProductId) &&
                image.Status != ProductImageStatus.Deleted &&
                image.Status != ProductImageStatus.PendingDelete)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAtUtc)
            .ThenBy(image => image.Id)
            .Select(image => new { image.ProductId, image.PublicId, image.AltTextZhTw, image.Width, image.Height })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProductId)
            .ToDictionary(group => group.Key, group =>
            {
                var first = group.First();
                var (width, height) = ProductImageVariantSizing.Fit(first.Width, first.Height, ProductImageVariantNames.LongEdge(ProductImageVariant.Small320));
                return new ProductImageSummary(
                    $"{ProductImagePublicUrls.PreviewPathBase(first.PublicId)}/{ProductImageVariantNames.Small}",
                    first.AltTextZhTw,
                    width,
                    height);
            });
    }

    /// <summary>後台商品詳情：未刪除的全部圖片，依排序；第一張是主圖。</summary>
    public static async Task<IReadOnlyList<AdminProductImageDto>> LoadAdminImagesAsync(
        DoSelectDbContext dbContext,
        long productId,
        Guid productPublicId,
        CancellationToken cancellationToken)
    {
        var images = await dbContext.ProductImages.AsNoTracking()
            .Where(image => image.ProductId == productId &&
                image.Status != ProductImageStatus.Deleted &&
                image.Status != ProductImageStatus.PendingDelete)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAtUtc)
            .ThenBy(image => image.Id)
            .ToListAsync(cancellationToken);
        return images
            .Select((image, index) => ToAdminDto(image, productPublicId, isPrimary: index == 0))
            .ToList();
    }

    /// <summary>
    /// 後台看得到的狀態。只給記憶體裡的判斷用——LINQ to SQL 翻不了方法呼叫，查詢裡要把同一個
    /// 條件內聯（上面三個查詢都是）。
    /// </summary>
    public static bool IsVisibleToAdmin(ProductImageStatus status) =>
        status != ProductImageStatus.Deleted && status != ProductImageStatus.PendingDelete;

    public static AdminProductImageDto ToAdminDto(ProductImage image, Guid productPublicId, bool isPrimary)
    {
        var published = image.Status == ProductImageStatus.Published;
        var variants = ProductImageVariantNames.Public
            .Select(variant =>
            {
                var (width, height) = ProductImageVariantSizing.Fit(image.Width, image.Height, ProductImageVariantNames.LongEdge(variant));
                var hash = variant switch
                {
                    ProductImageVariant.Small320 => image.SmallSha256,
                    ProductImageVariant.Medium800 => image.MediumSha256,
                    _ => image.LargeSha256,
                };
                return new AdminProductImageVariantDto(
                    ProductImageVariantNames.Name(variant),
                    width,
                    height,
                    published && hash is { Length: 32 } ? ProductImagePublicUrls.Build(image.PublicId, variant, hash) : null);
            })
            .ToList();

        return new AdminProductImageDto(
            image.PublicId,
            productPublicId,
            image.Status.ToString(),
            image.AltTextZhTw,
            image.SortOrder,
            isPrimary,
            image.SourceUrl,
            image.LicenseName,
            image.LicenseUrl,
            image.HasCompleteMetadata,
            image.OriginalFileName,
            image.MediaType,
            image.FileSizeBytes,
            image.Width,
            image.Height,
            ProductImagePublicUrls.PreviewPathBase(image.PublicId),
            variants,
            image.CreatedAtUtc,
            image.UpdatedAtUtc,
            image.PublishedAtUtc,
            image.RowVersion);
    }

    private static ProductImageSummary ToSummary(PublishedImageRow row, ProductImageVariant variant)
    {
        // 查詢已排除缺雜湊的列，這裡不會是 null；若真的是，寧可炸也不要輸出一個一定 404 的網址。
        var hash = (variant == ProductImageVariant.Small320 ? row.SmallSha256 : row.MediumSha256)
            ?? throw new InvalidOperationException("A published image projection requires its variant hashes.");
        var (width, height) = ProductImageVariantSizing.Fit(row.Width, row.Height, ProductImageVariantNames.LongEdge(variant));
        return new ProductImageSummary(
            ProductImagePublicUrls.Build(row.PublicId, variant, hash),
            row.AltText,
            width,
            height);
    }

    private sealed record PublishedImageRow(
        long ProductId,
        Guid PublicId,
        string AltText,
        int Width,
        int Height,
        byte[]? SmallSha256,
        byte[]? MediumSha256);
}
