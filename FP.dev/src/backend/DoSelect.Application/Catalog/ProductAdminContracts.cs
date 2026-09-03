using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Auditing;
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
    // A-06 圖片區塊要的是後台形狀（狀態、RowVersion、預覽路徑、發布後的公開 URL），不是
    // 公開的 ProductImageDto；未刪除的全部列出，第一張是主圖。
    IReadOnlyList<AdminProductImageDto> Images,
    IReadOnlyList<SkuDto> Skus,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

/// <summary>
/// UC-ADM-PROD-02 批次操作的白名單（API Endpoint 目錄「M 商品批次操作」列）。動作名稱直接出現在
/// 路由上，所以這裡是唯一來源——Controller 不另外寫一份字串。
/// </summary>
public static class BulkProductActions
{
    public const string Publish = "publish";
    public const string Unpublish = "unpublish";
    public const string AdjustPrice = "adjust-price";

    public static readonly IReadOnlyList<string> All = [Publish, Unpublish, AdjustPrice];
}

/// <summary>
/// 受控調價模式。規格只寫「受控調價模式與值、原因」而沒有列舉模式，這裡的兩個模式與下方的上下限
/// 是提案值，已在 PR 中請組長裁定；若他要別的組合，改這裡與 <see cref="BulkPriceAdjustment"/>
/// 的驗證即可，其餘流程不受影響。
/// </summary>
public static class BulkPriceAdjustmentModes
{
    /// <summary>依百分比調整，例如 -10 表示打九折。</summary>
    public const string Percentage = "percentage";

    /// <summary>依固定金額增減，例如 -100 表示每個 SKU 減 100 元。</summary>
    public const string Amount = "amount";

    public static readonly IReadOnlyList<string> All = [Percentage, Amount];
}

/// <summary>
/// 一次批次調價的模式、值與原因。原因會寫進中央 Audit（UC-ADM-PROD-02 驗收要求稽核），所以是必填。
/// </summary>
public sealed record BulkPriceAdjustment(
    [Required] string Mode,
    decimal Value,
    [Required, StringLength(500, MinimumLength = 1)] string Reason);

/// <summary>批次動作中的一筆商品與它的 RowVersion。</summary>
public sealed record BulkProductActionItem(Guid ProductPublicId, byte[] RowVersion);

/// <summary>
/// `BulkProductActionRequest`（API DTO與Schema契約）：`productPublicIds:uuid[1..100]`、
/// `rowVersions:{productPublicId,rowVersion}[]`；`adjust-price` 另帶受控調價模式與值、原因。
/// 兩個清單必須指向同一組商品——契約同時要求兩者，所以不一致是 validation_failed，不是默默取其一。
/// </summary>
public sealed record BulkProductActionRequest(
    [Required] IReadOnlyList<Guid> ProductPublicIds,
    [Required] IReadOnlyList<BulkProductActionItem> RowVersions,
    BulkPriceAdjustment? PriceAdjustment);

/// <summary>
/// 批次動作結果。整批是單一交易、全成功或全回滾（商品、組裝與相容性.md「任一筆失敗時整批回滾，
/// 不允許部分成功」），所以這裡不回逐筆狀態，只回實際受影響的數量。
/// </summary>
public sealed record BulkProductActionResultDto(
    string Action,
    int AffectedProductCount,
    int AffectedSkuCount);

/// <summary>後台商品匯出結果；`Content` 已含 UTF-8 BOM，Excel 開啟中文不會亂碼。</summary>
public sealed record AdminProductExportDto(string FileName, string ContentType, byte[] Content);

/// <summary>匯出格式。XLSX 與 CSV 都是規格明列的（商品、組裝與相容性.md「匯出為 XLSX 或 CSV」）。</summary>
public static class AdminProductExportFormats
{
    public const string Csv = "csv";
    public const string Xlsx = "xlsx";

    public static readonly IReadOnlyList<string> All = [Csv, Xlsx];
}

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

    /// <summary>
    /// UC-ADM-PROD-02 批次上架／下架／調價。整批單一交易，任一筆不合法就整批拒絕。
    /// </summary>
    Task<BulkProductActionResultDto> ApplyBulkActionAsync(
        string action,
        BulkProductActionRequest request,
        AuditRequestContext auditContext,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 依目前列表的 Filter 匯出商品（Endpoint 目錄：「匯出沿用目前 Filter」）。欄位與
    /// <see cref="AdminProductSummaryDto"/> 一致，因此不含成本——列表本來就看不到成本。
    /// </summary>
    Task<AdminProductExportDto> ExportAsync(
        AdminProductQuery query,
        string format,
        CancellationToken cancellationToken);
}
