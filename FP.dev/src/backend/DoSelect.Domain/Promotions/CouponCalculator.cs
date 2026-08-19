namespace DoSelect.Domain.Promotions;

/// <summary>
/// 優惠券折扣與分攤的純計算。輸入完全由呼叫端提供，計算器不查資料庫、不看時鐘。
/// 計算順序依 02-領域需求/優惠券規則：特價後小計 → 找出符合資格商品 → 套用折扣 → 分攤。
/// 組裝費不參與折扣；運費只由免運券以旗標表示，金額由配送模組決定。
/// </summary>
public static class CouponCalculator
{
    /// <summary>所有金額固定兩位小數。</summary>
    public const int AmountScale = 2;

    public static CouponCalculationResult Calculate(CouponCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Rule);
        ArgumentNullException.ThrowIfNull(request.Scope);
        ArgumentNullException.ThrowIfNull(request.Usage);
        ArgumentNullException.ThrowIfNull(request.Lines);

        if (request.EvaluatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(request));
        }

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0 || line.FinalUnitPrice < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "A calculation line is malformed.");
            }
        }

        var coupon = request.Rule;

        if (coupon.Status != CouponStatus.Active ||
            request.EvaluatedAtUtc < coupon.StartsAtUtc ||
            request.EvaluatedAtUtc >= coupon.EndsAtUtc)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponNotActive);
        }

        if (coupon.MemberOnly && !request.IsAuthenticatedMember)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponNotApplicable);
        }

        if (coupon.TotalUsageLimit is { } totalLimit && request.Usage.TotalRedeemedCount >= totalLimit)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponUsageExhausted);
        }

        if (coupon.PerMemberLimit is { } perMemberLimit && request.Usage.MemberRedeemedCount >= perMemberLimit)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponUsageExhausted);
        }

        if (!IsShippingKindSupported(coupon.DiscountType, request.IsAssemblyDelivery))
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponNotApplicable);
        }

        if (coupon.ScopeType == CouponScopeType.Restricted &&
            request.Scope.IncludedCategoryIds.Count == 0 &&
            request.Scope.IncludedProductIds.Count == 0)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponInvalid);
        }

        var eligibleLines = request.Lines
            .Where(line => IsEligible(line, coupon, request.Scope))
            .ToArray();

        if (eligibleLines.Length == 0)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponNotApplicable);
        }

        var eligibleSubtotal = Round(eligibleLines.Sum(line => line.LineSubtotal));

        // 最低消費門檻比對「符合優惠券範圍的商品小計」，不含運費、組裝費與範圍外商品。
        if (coupon.MinimumSpend is { } minimumSpend && eligibleSubtotal < minimumSpend)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponNotApplicable);
        }

        return coupon.DiscountType switch
        {
            CouponDiscountType.FreeShipping => CouponCalculationResult.Success(
                0m, eligibleSubtotal, isFreeShipping: true, isAssemblyFreeShipping: false, []),
            CouponDiscountType.AssemblyFreeShipping => CouponCalculationResult.Success(
                0m, eligibleSubtotal, isFreeShipping: false, isAssemblyFreeShipping: true, []),
            CouponDiscountType.FixedAmount => CalculateAmountDiscount(
                ResolveFixedAmount(coupon, eligibleSubtotal), eligibleSubtotal, eligibleLines),
            CouponDiscountType.Percentage => CalculateAmountDiscount(
                ResolvePercentageAmount(coupon, eligibleSubtotal), eligibleSubtotal, eligibleLines),
            _ => CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponInvalid),
        };
    }

    /// <summary>
    /// 一般免運券不適用組裝電腦宅配；組裝電腦免運券只適用組裝電腦宅配。
    /// </summary>
    private static bool IsShippingKindSupported(CouponDiscountType discountType, bool isAssemblyDelivery) =>
        discountType switch
        {
            CouponDiscountType.FreeShipping => !isAssemblyDelivery,
            CouponDiscountType.AssemblyFreeShipping => isAssemblyDelivery,
            _ => true,
        };

    /// <summary>
    /// 排除商品優先於包含分類與包含商品；ExcludeSaleItems 時特價品不參與。
    /// </summary>
    private static bool IsEligible(CouponCalculationLine line, CouponRule coupon, CouponScopeRules scope)
    {
        if (scope.ExcludedProductIds.Contains(line.ProductId))
        {
            return false;
        }

        if (coupon.ExcludeSaleItems && line.IsOnSale)
        {
            return false;
        }

        if (coupon.ScopeType == CouponScopeType.All)
        {
            return true;
        }

        return scope.IncludedProductIds.Contains(line.ProductId) ||
            line.CategoryIds.Any(scope.IncludedCategoryIds.Contains);
    }

    private static decimal? ResolveFixedAmount(CouponRule coupon, decimal eligibleSubtotal) =>
        coupon.DiscountValue is { } discountValue
            ? Math.Min(Round(discountValue), eligibleSubtotal)
            : null;

    private static decimal? ResolvePercentageAmount(CouponRule coupon, decimal eligibleSubtotal)
    {
        // 百分比折扣必須設定最高折抵，避免高價電腦產生不可控折扣。
        if (coupon.DiscountValue is not { } rate || coupon.MaximumDiscount is not { } maximumDiscount)
        {
            return null;
        }

        var discount = Round(eligibleSubtotal * rate);
        return Math.Min(Math.Min(discount, Round(maximumDiscount)), eligibleSubtotal);
    }

    private static CouponCalculationResult CalculateAmountDiscount(
        decimal? discountAmount,
        decimal eligibleSubtotal,
        IReadOnlyList<CouponCalculationLine> eligibleLines) =>
        discountAmount is { } amount
            ? CouponCalculationResult.Success(
                amount,
                eligibleSubtotal,
                isFreeShipping: false,
                isAssemblyFreeShipping: false,
                Allocate(amount, eligibleSubtotal, eligibleLines))
            : CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponInvalid);

    /// <summary>
    /// 依成交金額比例分攤訂單級折扣，每筆四捨五入至兩位小數，最後一筆合法品項吸收尾差。
    /// 非末筆的分攤額夾在剩餘未分攤金額以內，因此分攤合計精確等於折扣金額，且每筆皆不為負。
    /// </summary>
    private static IReadOnlyList<CouponDiscountAllocation> Allocate(
        decimal discountAmount,
        decimal eligibleSubtotal,
        IReadOnlyList<CouponCalculationLine> eligibleLines)
    {
        var allocations = new CouponDiscountAllocation[eligibleLines.Count];
        var allocated = 0m;

        for (var index = 0; index < eligibleLines.Count; index++)
        {
            var line = eligibleLines[index];
            var remaining = discountAmount - allocated;
            var share = index == eligibleLines.Count - 1 || eligibleSubtotal <= 0m
                ? remaining
                : Math.Min(Round(discountAmount * line.LineSubtotal / eligibleSubtotal), remaining);

            allocated += share;
            allocations[index] = new CouponDiscountAllocation(line.LineId, share);
        }

        return allocations;
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, AmountScale, MidpointRounding.AwayFromZero);
}
