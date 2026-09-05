using DoSelect.Application.Auditing;
using DoSelect.Application.Files;

namespace DoSelect.Application.Catalog;

/// <summary>
/// M-03 商品圖片後台（API Endpoint 目錄「M 商品圖片」列；契約細節依
/// 檔案與圖片儲存設計.md「API 與錯誤契約」）。儲存、掃描、三種 WebP 衍生圖與清理都沿用既有的
/// <see cref="IImageStorage"/>／StorageMaintenanceJob——這裡只做「一張圖片的中繼資料與狀態」。
/// </summary>
public interface IProductImageAdminService
{
    /// <summary>
    /// 上傳一張原圖並建立三種衍生圖。成功後圖片為 Ready（未發布，公開路由讀不到）。儲存層拒絕的
    /// 檔案以 <see cref="DoSelect.Application.Common.DomainProblemException"/> 帶檔案錯誤碼回報
    /// （413／415／422／503），資料庫不留任何列；資料交易失敗時已存的檔案立即刪除。
    /// </summary>
    Task<AdminProductImageDto> UploadAsync(
        Guid productPublicId,
        ProductImageUpload upload,
        UploadProductImageMetadata metadata,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// 後台預覽 original／320／800／1600。圖片不存在、已刪除、變體名稱不合法或實體檔不在都回
    /// null——呼叫端一律 404，不揭露檔案是否存在。
    /// </summary>
    Task<ProductImagePreview?> OpenPreviewAsync(
        Guid imagePublicId,
        string variant,
        CancellationToken cancellationToken);

    /// <summary>
    /// 更新 Alt、排序與來源／授權中繼資料；RowVersion 不符回 concurrency_conflict。已發布的圖片不能
    /// 被改成中繼資料不完整（組長 PR #101 item 1）：那會回 <c>image_metadata_incomplete</c>。
    /// 來源／授權網址只接受 absolute HTTP／HTTPS（裁定 D）。
    /// </summary>
    Task<AdminProductImageDto> UpdateAsync(
        Guid imagePublicId,
        UpdateProductImageCommand command,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// 核准並讓公開內容雜湊 URL 生效。第一版要求 Alt、來源與授權齊備（API錯誤碼目錄
    /// `image_metadata_incomplete`）；三種衍生圖雜湊在上傳時已記錄，缺任何一個不得發布。
    /// 已發布的圖片再發布一次是 no-op（RowVersion 仍須相符）。
    /// </summary>
    Task<AdminProductImageDto> PublishAsync(
        Guid imagePublicId,
        byte[] rowVersion,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// 解除引用：圖片轉 Deleted，公開路由與後台預覽立即讀不到；實體檔由 maintenance Queue 的
    /// StorageMaintenanceJob 依「30 天」生命週期清理，這裡不直接刪檔（替換不能先刪造成破圖）。
    /// </summary>
    Task DeleteAsync(
        Guid imagePublicId,
        byte[] rowVersion,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);
}

/// <summary>
/// 商品圖片四個動作的中央 Audit 欄位（組長 PR #101 裁定 B：記圖片／商品識別、狀態前後、排序與
/// 中繼資料完整性等安全欄位，不記完整 URL 或檔案內容）。
/// </summary>
public static class ProductImageAuditFields
{
    public const string ProductPublicId = "productPublicId";
    public const string Status = "status";
    public const string SortOrder = "sortOrder";
    public const string HasCompleteMetadata = "hasCompleteMetadata";
    /// <summary>Alt／來源／授權有改（值不進稽核，只記「改過」）。</summary>
    public const string Metadata = "metadata";
}

public static class ProductImageAuditReasons
{
    public const string AdminUpload = "admin_upload";
    public const string AdminEdit = "admin_edit";
    public const string AdminPublish = "admin_publish";
    public const string AdminDelete = "admin_delete";
}

/// <summary>Multipart 的文字欄位（檔案與圖片儲存設計：altText 160、sourceUrl 1000、licenseName 100、licenseUrl 1000）。</summary>
public sealed record UploadProductImageMetadata(
    string? AltText,
    string? SourceUrl,
    string? LicenseName,
    string? LicenseUrl);

public sealed record UpdateProductImageCommand(
    string AltText,
    int SortOrder,
    string? SourceUrl,
    string? LicenseName,
    string? LicenseUrl,
    byte[] RowVersion);

/// <summary>後台預覽串流；呼叫端負責 Dispose。</summary>
public sealed record ProductImagePreview(Stream Content, string ContentType);

/// <summary>一種衍生圖的尺寸與（已發布時的）公開 URL。</summary>
public sealed record AdminProductImageVariantDto(
    string Variant,
    int Width,
    int Height,
    string? PublicUrl);

/// <summary>
/// `AdminProductImageDto`（API DTO與Schema契約）。<see cref="PreviewPathBase"/> 加上
/// `/original`／`/320`／`/800`／`/1600` 就是後台預覽路由；<see cref="Variants"/> 的
/// <see cref="AdminProductImageVariantDto.PublicUrl"/> 只有 Published 才有值。
/// </summary>
public sealed record AdminProductImageDto(
    Guid PublicId,
    Guid ProductPublicId,
    string Status,
    string AltText,
    int SortOrder,
    bool IsPrimary,
    string? SourceUrl,
    string? LicenseName,
    string? LicenseUrl,
    bool HasCompleteMetadata,
    string OriginalFileName,
    string MediaType,
    long FileSizeBytes,
    int Width,
    int Height,
    string PreviewPathBase,
    IReadOnlyList<AdminProductImageVariantDto> Variants,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    byte[] RowVersion);

public static class ProductImageMetadataLimits
{
    public const int AltTextMaxLength = 160;
    public const int SourceUrlMaxLength = 1000;
    public const int LicenseNameMaxLength = 100;
    public const int LicenseUrlMaxLength = 1000;
    public const int SortOrderMax = 9_999;
}

/// <summary>
/// 路由上的變體名稱：公開路由只接受 320／800／1600（SH-06），後台預覽另接受 original。
/// 名稱與長邊在這裡只定義一次，Api、Infrastructure 與前端 URL 都對同一份。
/// </summary>
public static class ProductImageVariantNames
{
    public const string Original = "original";
    public const string Small = "320";
    public const string Medium = "800";
    public const string Large = "1600";

    public static bool TryParse(string? value, out ProductImageVariant variant)
    {
        variant = value switch
        {
            Small => ProductImageVariant.Small320,
            Medium => ProductImageVariant.Medium800,
            Large => ProductImageVariant.Large1600,
            Original => ProductImageVariant.Original,
            _ => (ProductImageVariant)(-1),
        };
        return Enum.IsDefined(variant);
    }

    public static string Name(ProductImageVariant variant) => variant switch
    {
        ProductImageVariant.Small320 => Small,
        ProductImageVariant.Medium800 => Medium,
        ProductImageVariant.Large1600 => Large,
        ProductImageVariant.Original => Original,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    public static int LongEdge(ProductImageVariant variant) => variant switch
    {
        ProductImageVariant.Small320 => 320,
        ProductImageVariant.Medium800 => 800,
        ProductImageVariant.Large1600 => 1600,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    public static readonly IReadOnlyList<ProductImageVariant> Public =
        [ProductImageVariant.Small320, ProductImageVariant.Medium800, ProductImageVariant.Large1600];
}

/// <summary>
/// 衍生圖尺寸：與 LocalImageStorage 產圖時的規則相同——保持比例縮到長邊為目標值、不放大。
/// 資料庫只存原圖尺寸，衍生圖的寬高由這裡算，前端 &lt;img width/height&gt; 才不會撒謊。
/// </summary>
public static class ProductImageVariantSizing
{
    public static (int Width, int Height) Fit(int width, int height, int targetLongEdge)
    {
        var longEdge = Math.Max(width, height);
        if (longEdge <= targetLongEdge)
        {
            return (width, height);
        }

        var scale = targetLongEdge / (double)longEdge;
        return (
            Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
    }
}

/// <summary>公開路由 `/media/products/{publicId}/{variant}/{contentHash}.webp`（SH-06）。</summary>
public static class ProductImagePublicUrls
{
    public static string Build(Guid imagePublicId, ProductImageVariant variant, byte[] variantSha256)
    {
        ArgumentNullException.ThrowIfNull(variantSha256);
        return $"/media/products/{imagePublicId:D}/{ProductImageVariantNames.Name(variant)}/{Convert.ToHexString(variantSha256).ToLowerInvariant()}.webp";
    }

    public static string PreviewPathBase(Guid imagePublicId) =>
        $"/api/v1/admin/product-images/{imagePublicId:D}/preview";
}
