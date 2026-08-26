namespace DoSelect.Domain.Promotions;

/// <summary>
/// 優惠券計算失敗時回傳的錯誤碼，值必須與 API錯誤碼目錄 一致。
/// </summary>
public static class CouponCalculationErrorCodes
{
    public const string CouponInvalid = "coupon_invalid";
    public const string CouponNotActive = "coupon_not_active";
    public const string CouponNotApplicable = "coupon_not_applicable";
    public const string CouponUsageExhausted = "coupon_usage_exhausted";
    public const string CouponStateConflict = "coupon_state_conflict";
}

/// <summary>
/// 優惠碼正規化：Trim 後轉為大寫，與 <see cref="Coupon.Code"/> 的保存格式相同。
/// </summary>
public static class CouponCode
{
    public static string Normalize(string code) =>
        string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("The coupon code is required.", nameof(code))
            : code.Trim().Normalize().ToUpperInvariant();

    public static bool TryNormalize(string? code, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = code.Trim().Normalize().ToUpperInvariant();
        return true;
    }
}

/// <summary>
/// 計算當下的優惠券規則快照。計算器只吃這份不可變資料，不直接依賴 <see cref="Coupon"/> 的可變狀態，
/// 因此規則版本、狀態與期間都必須由呼叫端在同一交易內取出後傳入。
/// </summary>
public sealed record CouponRule(
    string Code,
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
    CouponStatus Status,
    int RuleVersion)
{
    public static CouponRule From(Coupon coupon)
    {
        ArgumentNullException.ThrowIfNull(coupon);

        return new CouponRule(
            coupon.Code,
            coupon.DiscountType,
            coupon.DiscountValue,
            coupon.MinimumSpend,
            coupon.MaximumDiscount,
            coupon.StartsAtUtc,
            coupon.EndsAtUtc,
            coupon.TotalUsageLimit,
            coupon.PerMemberLimit,
            coupon.MemberOnly,
            coupon.ExcludeSaleItems,
            coupon.ScopeType,
            coupon.Status,
            coupon.RuleVersion);
    }
}

/// <summary>
/// 參與折扣計算的單一品項。金額已套用特價，組裝費與運費不在此列。
/// <paramref name="LineId"/> 由呼叫端提供（CartItem／OrderItem 的 PublicId），用於回寫分攤金額。
/// </summary>
public sealed record CouponCalculationLine(
    Guid LineId,
    long ProductId,
    IReadOnlyCollection<long> CategoryIds,
    int Quantity,
    decimal FinalUnitPrice,
    bool IsOnSale)
{
    public decimal LineSubtotal => Quantity * FinalUnitPrice;
}

/// <summary>
/// 優惠券適用範圍。<see cref="CouponScopeType.All"/> 時三個集合皆為空。
/// </summary>
public sealed record CouponScopeRules(
    CouponScopeType ScopeType,
    IReadOnlyCollection<long> IncludedCategoryIds,
    IReadOnlyCollection<long> IncludedProductIds,
    IReadOnlyCollection<long> ExcludedProductIds)
{
    public static CouponScopeRules SiteWide { get; } =
        new(CouponScopeType.All, [], [], []);

    public static CouponScopeRules SiteWideExcluding(IReadOnlyCollection<long> excludedProductIds) =>
        new(CouponScopeType.All, [], [], excludedProductIds);
}

/// <summary>
/// 已完成的使用量。由呼叫端於同一交易內查得後傳入，計算器本身不查資料庫。
/// <paramref name="MemberRedeemedCount"/> 對會員為 MemberUserId 的次數，對訪客為 GuestUsageKeyHash 的次數。
/// </summary>
public sealed record CouponUsageState(int TotalRedeemedCount, int MemberRedeemedCount)
{
    public static CouponUsageState Unused { get; } = new(0, 0);
}

/// <summary>
/// 一次優惠券試算的完整輸入。計算器為純函式，不含任何 I/O。
/// </summary>
/// <summary>
/// 一次優惠券試算的完整輸入。
/// <paramref name="IsAssemblyDelivery"/> 由配送方式對應而來：
/// <c>IsAssemblyDelivery = (ShippingMethod.Kind == "HomeDeliveryAssembly")</c>。
/// 目前的三個 Kind 值為 <c>HomeDeliveryStandard</c>、<c>HomeDeliveryAssembly</c>、
/// <c>ConvenienceStorePickup</c>，常數將由配送模組定義。
/// </summary>
public sealed record CouponCalculationRequest(
    CouponRule Rule,
    CouponScopeRules Scope,
    CouponUsageState Usage,
    IReadOnlyList<CouponCalculationLine> Lines,
    bool IsAuthenticatedMember,
    bool IsAssemblyDelivery,
    DateTime EvaluatedAtUtc);

/// <summary>
/// 訂單級折扣分攤到單一品項的結果。
/// </summary>
public sealed record CouponDiscountAllocation(Guid LineId, decimal Amount);

/// <summary>
/// 優惠券試算結果。失敗時只帶錯誤碼，不丟例外。
/// </summary>
public sealed class CouponCalculationResult
{
    private CouponCalculationResult(
        string? errorCode,
        decimal discountAmount,
        decimal eligibleSubtotal,
        bool isFreeShipping,
        bool isAssemblyFreeShipping,
        IReadOnlyList<CouponDiscountAllocation> allocations)
    {
        ErrorCode = errorCode;
        DiscountAmount = discountAmount;
        EligibleSubtotal = eligibleSubtotal;
        IsFreeShipping = isFreeShipping;
        IsAssemblyFreeShipping = isAssemblyFreeShipping;
        Allocations = allocations;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>訂單級商品折扣金額。免運券固定為 0。</summary>
    public decimal DiscountAmount { get; }

    /// <summary>符合優惠券範圍的商品小計，供免運門檻與快照使用。</summary>
    public decimal EligibleSubtotal { get; }

    /// <summary>一般宅配或超商取貨免運。實際運費金額由配送模組決定。</summary>
    public bool IsFreeShipping { get; }

    /// <summary>組裝電腦宅配免運。</summary>
    public bool IsAssemblyFreeShipping { get; }

    /// <summary>各品項分攤金額，合計精確等於 <see cref="DiscountAmount"/>。</summary>
    public IReadOnlyList<CouponDiscountAllocation> Allocations { get; }

    public static CouponCalculationResult Failure(string errorCode) =>
        new(errorCode, 0m, 0m, false, false, []);

    public static CouponCalculationResult Success(
        decimal discountAmount,
        decimal eligibleSubtotal,
        bool isFreeShipping,
        bool isAssemblyFreeShipping,
        IReadOnlyList<CouponDiscountAllocation> allocations) =>
        new(null, discountAmount, eligibleSubtotal, isFreeShipping, isAssemblyFreeShipping, allocations);
}
