using System.ComponentModel.DataAnnotations;
using System.Net;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Promotions;

/// <summary>
/// 後台優惠券列表查詢（`GET /api/v1/admin/coupons`）。
/// </summary>
/// <remarks>
/// 依 API共通規範第 88 行，`PageNumber &lt; 1`、`PageSize &lt; 1` 或 `PageSize &gt; 100`
/// 一律回 400，**不自動修正也不忽略**；驗證見 <see cref="AdminCouponQueryValidator"/>。
/// </remarks>
public sealed record AdminCouponQuery(
    string? Q,
    IReadOnlyList<CouponStatus>? Statuses,
    string? Sort,
    int PageNumber,
    int PageSize);

public static class AdminCouponSortOptions
{
    public const string UpdatedDesc = "updatedDesc";
    public const string UpdatedAsc = "updatedAsc";
    public const string CodeAsc = "codeAsc";
    public const string CodeDesc = "codeDesc";
    public const string EndsAtAsc = "endsAtAsc";

    public static readonly IReadOnlyCollection<string> All =
    [
        UpdatedDesc,
        UpdatedAsc,
        CodeAsc,
        CodeDesc,
        EndsAtAsc,
    ];
}

public static class AdminCouponActions
{
    public const string Activate = "activate";
    public const string Pause = "pause";
    public const string Disable = "disable";

    /// <summary>
    /// Action 白名單（API Endpoint目錄第 113 行）。未列出的動作回 404，
    /// 不落到任何預設分支。
    /// </summary>
    public static readonly IReadOnlyCollection<string> All = [Activate, Pause, Disable];

    public static bool IsAllowed(string? action) =>
        action is not null &&
        All.Contains(action.Trim(), StringComparer.Ordinal);
}

/// <summary>
/// 優惠券適用與排除範圍。一律以 PublicId 對外，不外洩內部主鍵。
/// </summary>
public sealed record CouponScopeDto(
    CouponScopeType ScopeType,
    IReadOnlyList<Guid> CategoryPublicIds,
    IReadOnlyList<Guid> ProductPublicIds,
    IReadOnlyList<Guid> ExcludedProductPublicIds);

/// <summary>
/// 目前使用量。
/// </summary>
/// <remarks>
/// <paramref name="TotalRedeemedCount"/> 使用與試算完全相同的名額定義
/// （<c>Consumed</c> 加上尚未過期的 <c>Reserved</c>）。後台看到的數字若與規則引擎
/// 實際採用的不同，管理員會依一個不存在的餘額做決策。
/// <paramref name="RemainingCount"/> 在無總量上限時為 <c>null</c>，代表不限量，
/// 不是 0。
/// </remarks>
public sealed record CouponUsageDto(
    int TotalRedeemedCount,
    int? TotalUsageLimit,
    int? PerMemberLimit,
    int? RemainingCount);

/// <summary>
/// 後台優惠券的完整表示（API DTO與Schema契約第 123 行）。
/// </summary>
public sealed record CouponDto(
    Guid PublicId,
    string Code,
    string NameZhTw,
    CouponDiscountType DiscountType,
    CouponStatus Status,
    decimal? DiscountValue,
    decimal? MinimumSpend,
    decimal? MaximumDiscount,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    bool MemberOnly,
    bool ExcludeSaleItems,
    CouponScopeDto Scope,
    CouponUsageDto Usage,
    int RuleVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

/// <summary>
/// 建立優惠券（API DTO與Schema契約第 124 行）。
/// </summary>
/// <remarks>
/// 長度上限對應 <c>CouponConfiguration</c>：<c>Code</c> nvarchar(64)、
/// <c>NameZhTw</c> nvarchar(160)。在此擋下可得到穩定的 400 `validation_failed`，
/// 而不是讓超長值一路到 SQL Server 截斷後變成 500。
/// </remarks>
public sealed record CreateCouponRequest(
    [Required, StringLength(64, MinimumLength = 1)] string Code,
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    CouponDiscountType DiscountType,
    decimal? DiscountValue,
    decimal? MinimumSpend,
    decimal? MaximumDiscount,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? TotalUsageLimit,
    int? PerMemberLimit,
    bool MemberOnly,
    bool ExcludeSaleItems,
    CouponScopeType ScopeType,
    IReadOnlyList<Guid>? CategoryPublicIds,
    IReadOnlyList<Guid>? ProductPublicIds,
    IReadOnlyList<Guid>? ExcludedProductPublicIds);

/// <summary>
/// 修改優惠券（API DTO與Schema契約第 125 行）：建立欄位加上 <paramref name="RowVersion"/>。
/// </summary>
/// <remarks>
/// 送出的是**完整**規則，不是差異。已產生 <see cref="CouponRedemption"/> 之後
/// <paramref name="Code"/> 不得改變，但仍必須照原值送回 —— 凍結檢查比對的是值。
/// </remarks>
public sealed record UpdateCouponRequest(
    [Required, StringLength(64, MinimumLength = 1)] string Code,
    [Required, StringLength(160, MinimumLength = 1)] string NameZhTw,
    CouponDiscountType DiscountType,
    decimal? DiscountValue,
    decimal? MinimumSpend,
    decimal? MaximumDiscount,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? TotalUsageLimit,
    int? PerMemberLimit,
    bool MemberOnly,
    bool ExcludeSaleItems,
    CouponScopeType ScopeType,
    IReadOnlyList<Guid>? CategoryPublicIds,
    IReadOnlyList<Guid>? ProductPublicIds,
    IReadOnlyList<Guid>? ExcludedProductPublicIds,
    [Required] byte[] RowVersion);

/// <summary>
/// `activate`／`pause`／`disable` 的共用 Request（API DTO與Schema契約第 126 行）。
/// </summary>
/// <remarks>
/// <paramref name="ReasonCode"/> 與 <paramref name="Note"/> 只寫中央 Audit，
/// 不在 <see cref="Coupon"/> 新增欄位 —— 與退款執行同一原則（DEC-P289）。
/// </remarks>
public sealed record CouponActionRequest(
    [Required, StringLength(64, MinimumLength = 1)] string ReasonCode,
    [StringLength(1000)] string? Note,
    [Required] byte[] RowVersion);

/// <summary>
/// 一次後台寫入的可信呼叫端資訊，供中央 Audit 使用。
/// </summary>
/// <remarks>
/// 全部由 API 層從已驗證的 <c>HttpContext</c> 取得，**不接受 Request Body 帶入** ——
/// 讓呼叫端自報身分或 Trace，等於讓稽核紀錄可被偽造。
/// <paramref name="AdminUserId"/> 是 Identity 的內部 Id，只用來在同一交易內換取
/// <c>ApplicationUser.PublicId</c> 與角色快照，不會出現在任何回應或稽核欄位。
/// </remarks>
public sealed record AdminCouponActorContext(
    string AdminUserId,
    string CorrelationId,
    string TraceId,
    IPAddress? RemoteIpAddress);

/// <summary>
/// 中央 Audit 的優惠券欄位與理由碼慣例。
/// </summary>
/// <remarks>
/// <c>changedFields</c> 在本專案先前沒有任何使用者，慣例由本工程包定義：
/// 以 camelCase 欄位名依序用 <c>-</c> 串接。<see cref="AuditFieldChange"/> 的
/// safe-code 上限是 64 字元，欄位大量變動時串接必然超過；此時改記
/// <c>count:{n}</c>，明確表示「這裡是筆數不是名稱」，而不是靜默截斷成一份
/// 看起來完整、實際上少了幾個欄位的清單。
/// </remarks>
public static class CouponAuditFields
{
    public const string Status = "status";
    public const string RuleVersion = "ruleVersion";
    public const string ChangedFields = "changedFields";

    /// <summary>建立與修改沒有呼叫端提供的理由碼，使用固定的安全值。</summary>
    public const string CreateReasonCode = "coupon_create";

    public const string UpdateReasonCode = "coupon_update";

    private const int SafeCodeMaximumLength = 64;

    public static string Describe(IReadOnlyList<string> changedFields)
    {
        ArgumentNullException.ThrowIfNull(changedFields);

        var joined = string.Join(
            '-',
            changedFields.Select(field => char.ToLowerInvariant(field[0]) + field[1..]));

        return joined.Length is > 0 and <= SafeCodeMaximumLength
            ? joined
            : $"count:{changedFields.Count}";
    }
}

/// <summary>
/// 後台優惠券的查詢與寫入。實作屬 Infrastructure，本層不接觸 DbContext。
/// </summary>
public interface IAdminCouponService
{
    Task<PageResult<CouponDto>> ListAsync(
        AdminCouponQuery query,
        CancellationToken cancellationToken = default);

    Task<CouponDto?> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default);

    Task<CouponDto> CreateAsync(
        CreateCouponRequest request,
        AdminCouponActorContext actor,
        CancellationToken cancellationToken = default);

    Task<CouponDto> UpdateAsync(
        Guid publicId,
        UpdateCouponRequest request,
        AdminCouponActorContext actor,
        CancellationToken cancellationToken = default);

    Task<CouponDto> ExecuteActionAsync(
        Guid publicId,
        string action,
        CouponActionRequest request,
        AdminCouponActorContext actor,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 後台優惠券請求的共用驗證。放在 Application 層，讓 Infrastructure 與 Api
/// 用的是同一份規則。
/// </summary>
public static class AdminCouponQueryValidator
{
    public const int MaximumPageSize = 100;
    public const int MaximumScopeEntries = 200;

    /// <summary>
    /// 依 API共通規範第 88 行檢查分頁與排序。超出範圍一律拒絕，不夾擠成合法值 ——
    /// 靜默修正會讓呼叫端以為自己拿到了要求的那一頁。
    /// </summary>
    public static void RequireValid(AdminCouponQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PageNumber < 1)
        {
            throw DomainProblemException.Validation("pageNumber must be 1 or greater.");
        }

        if (query.PageSize < 1 || query.PageSize > MaximumPageSize)
        {
            throw DomainProblemException.Validation(
                $"pageSize must be between 1 and {MaximumPageSize}.");
        }

        if (query.Sort is not null &&
            !AdminCouponSortOptions.All.Contains(query.Sort, StringComparer.Ordinal))
        {
            throw DomainProblemException.Validation("sort is not a supported option.");
        }

        if (query.Statuses is not null &&
            query.Statuses.Any(status => !Enum.IsDefined(status)))
        {
            throw DomainProblemException.Validation("status is not a known coupon status.");
        }
    }

    /// <summary>
    /// 建立與修改共用的規則檢查。
    /// </summary>
    /// <remarks>
    /// 「百分比必填最大折抵」是 API DTO與Schema契約第 124 行的明文要求，而 Entity 只在
    /// <see cref="Coupon.HasCompleteDiscountRule"/> 把它當成「不可啟用」。兩者不衝突：
    /// Entity 允許不完整的 Draft 存在，契約則不允許用這個端點建立出來。
    /// </remarks>
    public static void RequireValidRule(
        CouponDiscountType discountType,
        decimal? discountValue,
        decimal? maximumDiscount,
        CouponScopeType scopeType,
        IReadOnlyList<Guid>? categoryPublicIds,
        IReadOnlyList<Guid>? productPublicIds,
        IReadOnlyList<Guid>? excludedProductPublicIds)
    {
        if (!Enum.IsDefined(discountType))
        {
            throw DomainProblemException.Validation("discountType is not a known value.");
        }

        if (!Enum.IsDefined(scopeType))
        {
            throw DomainProblemException.Validation("scopeType is not a known value.");
        }

        if (discountType == CouponDiscountType.Percentage && maximumDiscount is not > 0)
        {
            throw DomainProblemException.Validation(
                "A percentage coupon requires a positive maximumDiscount.");
        }

        if (discountType is CouponDiscountType.FixedAmount or CouponDiscountType.Percentage &&
            discountValue is not > 0)
        {
            throw DomainProblemException.Validation(
                "A fixed-amount or percentage coupon requires a positive discountValue.");
        }

        // 最終Schema「範圍規則」：`ScopeType=Restricted` 至少需一筆分類或商品。
        // 限定範圍卻沒有任何適用項目，等於一張永遠算不出折扣的券。
        if (scopeType == CouponScopeType.Restricted &&
            (categoryPublicIds is null or { Count: 0 }) &&
            (productPublicIds is null or { Count: 0 }))
        {
            throw DomainProblemException.Validation(
                "A restricted coupon requires at least one category or product.");
        }

        // 最終Schema「範圍規則」：`ScopeType=All` 不建立包含範圍。
        // 這不只是資料整潔問題 —— CouponCalculator 在 All 模式直接視為全部適用，
        // 完全不看包含集合。存進去會讓 API 回一份實際上不生效的設定。
        if (scopeType == CouponScopeType.All &&
            (categoryPublicIds is { Count: > 0 } || productPublicIds is { Count: > 0 }))
        {
            throw DomainProblemException.Validation(
                "A coupon scoped to All cannot carry included categories or products.");
        }

        RequireScopeListIsSane(categoryPublicIds, "categoryPublicIds");
        RequireScopeListIsSane(productPublicIds, "productPublicIds");
        RequireScopeListIsSane(excludedProductPublicIds, "excludedProductPublicIds");

        // 最終Schema「範圍規則」：同商品不得同時存在 CouponProducts 與
        // CouponExcludedProducts。規則另定「排除商品優先」，所以這種設定不會壞掉，
        // 但它表達的是兩個相反的意圖，管理員多半是誤設；靜默讓排除勝出等於
        // 幫他選了一邊。
        if (productPublicIds is { Count: > 0 } && excludedProductPublicIds is { Count: > 0 })
        {
            var overlapping = productPublicIds.Intersect(excludedProductPublicIds).ToArray();
            if (overlapping.Length > 0)
            {
                throw DomainProblemException.Validation(
                    "A product cannot be both included and excluded by the same coupon.");
            }
        }
    }

    private static void RequireScopeListIsSane(IReadOnlyList<Guid>? publicIds, string field)
    {
        if (publicIds is null)
        {
            return;
        }

        if (publicIds.Count > MaximumScopeEntries)
        {
            throw DomainProblemException.Validation(
                $"{field} cannot contain more than {MaximumScopeEntries} entries.");
        }

        if (publicIds.Any(publicId => publicId == Guid.Empty))
        {
            throw DomainProblemException.Validation($"{field} contains an empty PublicId.");
        }

        if (publicIds.Distinct().Count() != publicIds.Count)
        {
            throw DomainProblemException.Validation($"{field} contains a duplicate PublicId.");
        }
    }
}
