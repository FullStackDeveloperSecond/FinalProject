using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Tests;

/// <summary>
/// 後台優惠券請求驗證。分頁規則來自 API共通規範第 88 行，
/// 折扣規則來自 API DTO與Schema契約第 124 行。
/// </summary>
public sealed class AdminCouponQueryValidatorTests
{
    [Fact]
    public void ATypicalQueryIsAccepted() =>
        AdminCouponQueryValidator.RequireValid(Query());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageNumberBelowOneIsRejected(int pageNumber)
    {
        // 規範明文要求回 400，不得自動修正 —— 靜默改成第 1 頁會讓呼叫端
        // 以為自己拿到的是要求的那一頁。
        var exception = Assert.Throws<DomainProblemException>(
            () => AdminCouponQueryValidator.RequireValid(Query(pageNumber: pageNumber)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void APageSizeOutsideTheAllowedRangeIsRejected(int pageSize)
    {
        var exception = Assert.Throws<DomainProblemException>(
            () => AdminCouponQueryValidator.RequireValid(Query(pageSize: pageSize)));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void TheMaximumPageSizeItselfIsAccepted() =>
        AdminCouponQueryValidator.RequireValid(
            Query(pageSize: AdminCouponQueryValidator.MaximumPageSize));

    [Fact]
    public void AnUnknownSortOptionIsRejected() =>
        Assert.Throws<DomainProblemException>(
            () => AdminCouponQueryValidator.RequireValid(Query(sort: "priceAsc")));

    [Fact]
    public void EverySortOptionIsAccepted()
    {
        foreach (var sort in AdminCouponSortOptions.All)
        {
            AdminCouponQueryValidator.RequireValid(Query(sort: sort));
        }
    }

    [Fact]
    public void AnUndefinedStatusIsRejected() =>
        Assert.Throws<DomainProblemException>(
            () => AdminCouponQueryValidator.RequireValid(
                Query(statuses: [(CouponStatus)999])));

    [Fact]
    public void OnlyTheThreeDocumentedActionsAreAllowed()
    {
        Assert.True(AdminCouponActions.IsAllowed("activate"));
        Assert.True(AdminCouponActions.IsAllowed("pause"));
        Assert.True(AdminCouponActions.IsAllowed("disable"));

        Assert.False(AdminCouponActions.IsAllowed("delete"));
        Assert.False(AdminCouponActions.IsAllowed("expire"));
        Assert.False(AdminCouponActions.IsAllowed("schedule"));
        Assert.False(AdminCouponActions.IsAllowed(null));
        Assert.False(AdminCouponActions.IsAllowed(""));
    }

    [Fact]
    public void TheActionWhitelistIsCaseSensitive() =>
        // 路由值原樣比對；接受 "Activate" 會讓白名單多出未經裁定的別名。
        Assert.False(AdminCouponActions.IsAllowed("Activate"));

    [Fact]
    public void APercentageCouponWithoutAMaximumDiscountIsRejected()
    {
        // 契約第 124 行明文「百分比必填最大折抵」。Entity 只把它當成
        // 「不可啟用」，因此這條必須在這一層擋。
        var exception = Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.Percentage,
                discountValue: 0.1m,
                maximumDiscount: null));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void APercentageCouponWithAZeroMaximumDiscountIsRejected() =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.Percentage,
                discountValue: 0.1m,
                maximumDiscount: 0m));

    [Fact]
    public void APercentageCouponWithACapIsAccepted() =>
        RequireValidRule(CouponDiscountType.Percentage, 0.1m, 2000m);

    [Theory]
    [InlineData(CouponDiscountType.FixedAmount)]
    [InlineData(CouponDiscountType.Percentage)]
    public void ADiscountingCouponNeedsAPositiveValue(CouponDiscountType discountType) =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(discountType, discountValue: 0m, maximumDiscount: 2000m));

    [Theory]
    [InlineData(CouponDiscountType.FreeShipping)]
    [InlineData(CouponDiscountType.AssemblyFreeShipping)]
    public void AFreeShippingCouponNeedsNoDiscountValue(CouponDiscountType discountType) =>
        RequireValidRule(discountType, discountValue: null, maximumDiscount: null);

    [Fact]
    public void ARestrictedCouponWithNoCategoryOrProductIsRejected()
    {
        // 限定範圍卻沒有任何適用項目，等於一張永遠算不出折扣的券。
        var exception = Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.Restricted));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void ARestrictedCouponWithOnlyAnExclusionIsStillRejected() =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.Restricted,
                excludedProductPublicIds: [Guid.NewGuid()]));

    [Fact]
    public void ARestrictedCouponWithACategoryIsAccepted() =>
        RequireValidRule(
            CouponDiscountType.FixedAmount,
            300m,
            null,
            CouponScopeType.Restricted,
            categoryPublicIds: [Guid.NewGuid()]);

    [Fact]
    public void AnAllScopeCouponCannotCarryIncludedCategories()
    {
        // 最終Schema「範圍規則」：ScopeType=All 不建立包含範圍。
        // CouponCalculator 在 All 模式直接視為全部適用，完全不看包含集合 ——
        // 存進去等於讓 API 回一份實際上不生效的設定。
        var exception = Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.All,
                categoryPublicIds: [Guid.NewGuid()]));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void AnAllScopeCouponCannotCarryIncludedProducts() =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.All,
                productPublicIds: [Guid.NewGuid()]));

    [Fact]
    public void AProductCannotBeBothIncludedAndExcluded()
    {
        // 最終Schema「範圍規則」：同商品不得同時存在 CouponProducts 與
        // CouponExcludedProducts。另一條規則說「排除商品優先」，所以這不會壞掉 ——
        // 但那兩個設定表達相反的意圖，靜默讓排除勝出等於幫管理員選了一邊。
        var product = Guid.NewGuid();

        var exception = Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.Restricted,
                productPublicIds: [product],
                excludedProductPublicIds: [product]));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void AnOverlapIsDetectedEvenWhenOtherEntriesDiffer() =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.Restricted,
                productPublicIds: [Guid.NewGuid(), SharedProduct, Guid.NewGuid()],
                excludedProductPublicIds: [Guid.NewGuid(), SharedProduct]));

    [Fact]
    public void DisjointIncludeAndExcludeListsAreAccepted() =>
        RequireValidRule(
            CouponDiscountType.FixedAmount,
            300m,
            null,
            CouponScopeType.Restricted,
            productPublicIds: [Guid.NewGuid()],
            excludedProductPublicIds: [Guid.NewGuid()]);

    private static readonly Guid SharedProduct = Guid.NewGuid();

    [Fact]
    public void AnAllScopeCouponMayStillCarryExclusions() =>
        // 全站券也可以排除特定商品，這是既有 Reader 的行為。
        RequireValidRule(
            CouponDiscountType.FixedAmount,
            300m,
            null,
            CouponScopeType.All,
            excludedProductPublicIds: [Guid.NewGuid()]);

    [Fact]
    public void AnEmptyPublicIdInTheScopeIsRejected() =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.Restricted,
                categoryPublicIds: [Guid.Empty]));

    [Fact]
    public void ADuplicatePublicIdInTheScopeIsRejected()
    {
        var duplicated = Guid.NewGuid();

        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.Restricted,
                productPublicIds: [duplicated, duplicated]));
    }

    [Fact]
    public void AnOversizedScopeListIsRejected()
    {
        var tooMany = Enumerable
            .Range(0, AdminCouponQueryValidator.MaximumScopeEntries + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                CouponScopeType.Restricted,
                productPublicIds: tooMany));
    }

    [Fact]
    public void AnUndefinedDiscountTypeIsRejected() =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule((CouponDiscountType)999, 300m, null));

    [Fact]
    public void AnUndefinedScopeTypeIsRejected() =>
        Assert.Throws<DomainProblemException>(
            () => RequireValidRule(
                CouponDiscountType.FixedAmount,
                300m,
                null,
                (CouponScopeType)999));

    private static void RequireValidRule(
        CouponDiscountType discountType,
        decimal? discountValue,
        decimal? maximumDiscount,
        CouponScopeType scopeType = CouponScopeType.All,
        IReadOnlyList<Guid>? categoryPublicIds = null,
        IReadOnlyList<Guid>? productPublicIds = null,
        IReadOnlyList<Guid>? excludedProductPublicIds = null) =>
        AdminCouponQueryValidator.RequireValidRule(
            discountType,
            discountValue,
            maximumDiscount,
            scopeType,
            categoryPublicIds,
            productPublicIds,
            excludedProductPublicIds);

    private static AdminCouponQuery Query(
        string? q = null,
        IReadOnlyList<CouponStatus>? statuses = null,
        string? sort = null,
        int pageNumber = 1,
        int pageSize = 20) =>
        new(q, statuses, sort, pageNumber, pageSize);
}
