using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Catalog;

/// <summary>
/// 優惠券適用範圍挑選器需要的目錄參考資料。
/// </summary>
/// <remarks>
/// <para>
/// alex 2026-08-29 於 PR #64 的 P2#3 裁定：<b>不放寬 <c>CatalogManager</c></b>
/// （那會把 FinanceManager／MarketingAnalyst 的權限擴張到目錄管理邊界），
/// 也<b>不建立通用的高權限參考端點</b>；改用一個受 <c>Coupon.Manage</c> 保護、
/// 用途限定的唯讀批次端點。
/// </para>
/// <para>
/// 契約與 adapter 屬 Catalog，由 yinyin 以備援身分交付，Catalog owner 做 domain review。
/// </para>
/// <para>
/// <b>只回 picker 需要的欄位</b>：<c>PublicId</c>、code、name、status、
/// <c>IsSelectable</c>。不暴露 Catalog 的管理 DTO，也沒有任何寫入能力 ——
/// 這個端點的授權比目錄管理寬，所以它能看到的東西必須比目錄管理窄。
/// </para>
/// </remarks>
public static class CouponCatalogOptionRules
{
    /// <summary>
    /// 一次可以批次解析的 Product <c>PublicId</c> 數量上限。
    /// </summary>
    /// <remarks>
    /// 與 <c>AdminCouponRules.MaximumScopeEntries</c> 相同：一張優惠券的適用或排除
    /// 清單最多就是這麼多筆，載入既有規則時不會需要更多。上限不一致的話，
    /// 存得進去的規則會有一部分讀不回來。
    /// </remarks>
    public const int MaximumBatchSize = 200;

    /// <summary>關鍵字搜尋一次最多回幾筆。</summary>
    public const int MaximumSearchPageSize = 50;

    /// <summary>
    /// 這個商品狀態能不能<b>新增</b>到優惠券範圍裡。
    /// </summary>
    /// <remarks>
    /// alex 的 C1 裁定：<c>Draft</c>／<c>Published</c>／<c>Unpublished</c> 可以，
    /// 支援新品或重新上架前先排優惠；<c>Discontinued</c> 不可以。
    /// <para>
    /// 「不可新增」不等於「看不到」：已經寫在優惠券規則裡、之後才停售的商品，
    /// 仍然要解析得出來、顯示狀態、保留，並且讓管理員自己移除。
    /// 查不到就靜默消失，等於偷偷改掉一張已經上線的券。
    /// </para>
    /// </remarks>
    public static bool IsSelectable(ProductOptionStatus status) =>
        status != ProductOptionStatus.Discontinued;
}

/// <summary>
/// 對外表示的商品狀態。與 Domain 的 <c>ProductStatus</c> 一對一，
/// 但這是 picker 的公開契約，不隨 Catalog 內部列舉調整而變動。
/// </summary>
public enum ProductOptionStatus
{
    Draft,
    Published,
    Unpublished,
    Discontinued,
}

/// <summary>
/// 挑選器裡的一個分類。
/// </summary>
/// <param name="Path">從根到自己的名稱，供同名子分類區辨。</param>
/// <param name="IsActive">
/// 停用的分類<b>仍然可選</b>（C1 裁定），但清單必須標示狀態，
/// 否則管理員會把一張券綁在一個已經不對外的分類上而不自知。
/// </param>
public sealed record CouponCategoryOption(
    Guid PublicId,
    string Code,
    string Name,
    string Path,
    bool IsActive);

/// <summary>
/// 挑選器裡的一個商品。
/// </summary>
/// <param name="IsSelectable">
/// 能不能新增到範圍裡。<c>false</c> 的項目只會出現在「既有已選」的解析結果，
/// 不會出現在可新增的搜尋結果。
/// </param>
public sealed record CouponProductOption(
    Guid PublicId,
    string Code,
    string Name,
    ProductOptionStatus Status,
    bool IsSelectable);

public sealed record CouponProductSearchResult(
    IReadOnlyList<CouponProductOption> Items,
    int TotalCount,
    bool HasMore);

/// <summary>
/// 優惠券挑選器的目錄查詢。實作屬 Catalog 的 Infrastructure。
/// </summary>
public interface ICouponCatalogOptionsReader
{
    /// <summary>
    /// 一次取回整棵分類樹。
    /// </summary>
    /// <remarks>
    /// <b>一次</b>是契約的一部分（alex D1）：先前的做法對每個樹節點各打一次公開端點，
    /// 上限一百次，而那個端點每次還會順便算品牌、價格區間與規格篩選。
    /// </remarks>
    Task<IReadOnlyList<CouponCategoryOption>> ListCategoriesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 關鍵字搜尋可新增的商品。
    /// </summary>
    /// <remarks>
    /// 只回 <see cref="CouponCatalogOptionRules.IsSelectable"/> 為 <c>true</c> 的狀態 ——
    /// 搜尋結果是「可以加進來的東西」，停售商品不該出現在這裡。
    /// </remarks>
    Task<CouponProductSearchResult> SearchProductsAsync(
        string? keyword,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次解析一組 Product <c>PublicId</c>，用於載入既有的適用／排除範圍。
    /// </summary>
    /// <remarks>
    /// <b>不論幾筆都只打一次資料庫</b>；停售商品也要回得出來（見
    /// <see cref="CouponCatalogOptionRules.IsSelectable"/> 的說明）。
    /// 超過 <see cref="CouponCatalogOptionRules.MaximumBatchSize"/> 筆時拒絕，
    /// 不要靜默截斷 —— 截斷會讓被切掉的那幾筆看起來像不存在。
    /// </remarks>
    Task<IReadOnlyList<CouponProductOption>> ResolveProductsAsync(
        IReadOnlyCollection<Guid> publicIds,
        CancellationToken cancellationToken = default);
}

/// <summary>分類與商品搜尋一次取回的組合回應。</summary>
public sealed record CouponCatalogOptionsDto(
    IReadOnlyList<CouponCategoryOption> Categories,
    CouponProductSearchResult Products);

public sealed class CouponProductSearchRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    [Range(1, CouponCatalogOptionRules.MaximumSearchPageSize)]
    public int PageSize { get; init; } = 20;
}

public sealed class CouponProductResolveRequest
{
    /// <remarks>
    /// 上限與優惠券規則的 200 筆一致。用 <c>MaxLength</c> 讓超量在 transport 邊界
    /// 就變成 400 <c>validation_failed</c>，而不是走到 Reader 才丟例外變成 500。
    /// </remarks>
    [Required]
    [MaxLength(CouponCatalogOptionRules.MaximumBatchSize)]
    public IReadOnlyList<Guid> PublicIds { get; init; } = [];
}
