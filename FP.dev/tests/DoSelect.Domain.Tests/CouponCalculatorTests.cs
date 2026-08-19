using DoSelect.Domain.Promotions;

namespace DoSelect.Domain.Tests;

public sealed class CouponCalculatorTests
{
    private static readonly DateTime StartsAtUtc = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndsAtUtc = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EvaluatedAtUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

    private static readonly Guid LineA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LineB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LineC = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void FixedAmount_DiscountsAndAllocatesProportionally()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, minimumSpend: 3000m),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 1000m), Line(LineB, quantity: 1, unitPrice: 3000m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(300m, result.DiscountAmount);
        Assert.Equal(4000m, result.EligibleSubtotal);
        Assert.Equal([75m, 225m], result.Allocations.Select(allocation => allocation.Amount));
        Assert.Equal([LineA, LineB], result.Allocations.Select(allocation => allocation.LineId));
    }

    [Fact]
    public void FixedAmount_NeverDiscountsBelowZero()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 120m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(120m, result.DiscountAmount);
        Assert.Equal(120m, Assert.Single(result.Allocations).Amount);
    }

    [Fact]
    public void Percentage_IsCappedByMaximumDiscount()
    {
        var result = Calculate(
            Rule(
                CouponDiscountType.Percentage,
                discountValue: 0.1m,
                minimumSpend: 20000m,
                maximumDiscount: 2000m),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 50000m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2000m, result.DiscountAmount);
    }

    [Fact]
    public void Percentage_RoundsAwayFromZeroToTwoDecimals()
    {
        var result = Calculate(
            Rule(CouponDiscountType.Percentage, discountValue: 0.1m, maximumDiscount: 2000m),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 1000.05m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(100.01m, result.DiscountAmount);
    }

    [Fact]
    public void Percentage_WithoutMaximumDiscount_IsInvalid()
    {
        var result = Calculate(
            Rule(CouponDiscountType.Percentage, discountValue: 0.1m, maximumDiscount: null),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 5000m)]);

        Assert.Equal(CouponCalculationErrorCodes.CouponInvalid, result.ErrorCode);
    }

    [Fact]
    public void Allocation_LastEligibleLineAbsorbsTheRoundingRemainder()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 10m),
            CouponScopeRules.SiteWide,
            [
                Line(LineA, quantity: 1, unitPrice: 100m),
                Line(LineB, quantity: 1, unitPrice: 100m),
                Line(LineC, quantity: 1, unitPrice: 100m),
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal([3.33m, 3.33m, 3.34m], result.Allocations.Select(allocation => allocation.Amount));
        Assert.Equal(result.DiscountAmount, result.Allocations.Sum(allocation => allocation.Amount));
    }

    [Fact]
    public void Allocation_NeverProducesANegativeShareWhenEveryLineRoundsUp()
    {
        // 40 筆各 1 元加 1 筆 0.01 元，折 1.98 元：逐筆四捨五入為 0.05 會累計超過折扣總額，
        // 夾住剩餘金額後最後一筆才不會變成負數。
        var lines = Enumerable.Range(1, 40)
            .Select(index => Line(new Guid(index, 0, 0, new byte[8]), quantity: 1, unitPrice: 1m))
            .Append(Line(LineC, quantity: 1, unitPrice: 0.01m))
            .ToArray();

        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 1.98m),
            CouponScopeRules.SiteWide,
            lines);

        Assert.True(result.IsSuccess);
        Assert.All(result.Allocations, allocation => Assert.True(allocation.Amount >= 0m));
        Assert.Equal(1.98m, result.Allocations.Sum(allocation => allocation.Amount));
    }

    [Fact]
    public void MinimumSpend_UsesTheRulingsCreator10Example()
    {
        // DEC-BATCH-014 第 1 項：NT$18,000 顯卡（適用）加 NT$5,000 螢幕（不適用）
        // 仍不符合 NT$20,000 門檻，因為門檻只計適用範圍內的商品。
        var scope = new CouponScopeRules(CouponScopeType.Restricted, [2L], [], []);

        var result = Calculate(
            Rule(CouponDiscountType.Percentage, discountValue: 0.1m, minimumSpend: 20000m,
                maximumDiscount: 2000m, scopeType: CouponScopeType.Restricted),
            scope,
            [
                Line(LineA, quantity: 1, unitPrice: 18000m, categoryIds: [2L]),
                Line(LineB, quantity: 1, unitPrice: 5000m, categoryIds: [9L]),
            ]);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, result.ErrorCode);
    }

    [Fact]
    public void MinimumSpend_ComparesTheEligibleSubtotalOnly()
    {
        var scope = new CouponScopeRules(CouponScopeType.Restricted, [], [7L], []);

        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, minimumSpend: 3000m,
                scopeType: CouponScopeType.Restricted),
            scope,
            [
                Line(LineA, quantity: 1, unitPrice: 2000m, productId: 7L),
                Line(LineB, quantity: 1, unitPrice: 5000m, productId: 8L),
            ]);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, result.ErrorCode);
    }

    [Fact]
    public void ExcludedProduct_WinsOverIncludedCategory()
    {
        var scope = new CouponScopeRules(CouponScopeType.Restricted, [42L], [], [7L]);

        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, scopeType: CouponScopeType.Restricted),
            scope,
            [Line(LineA, quantity: 1, unitPrice: 5000m, productId: 7L, categoryIds: [42L])]);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, result.ErrorCode);
    }

    [Fact]
    public void ExcludeSaleItems_RemovesDiscountedLinesFromTheEligibleSubtotal()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, excludeSaleItems: true),
            CouponScopeRules.SiteWide,
            [
                Line(LineA, quantity: 1, unitPrice: 1000m, isOnSale: true),
                Line(LineB, quantity: 1, unitPrice: 2000m),
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2000m, result.EligibleSubtotal);
        Assert.Equal(LineB, Assert.Single(result.Allocations).LineId);
    }

    [Fact]
    public void RestrictedScope_MatchesIncludedCategories()
    {
        var scope = new CouponScopeRules(CouponScopeType.Restricted, [42L], [], []);

        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, scopeType: CouponScopeType.Restricted),
            scope,
            [
                Line(LineA, quantity: 1, unitPrice: 4000m, categoryIds: [42L, 43L]),
                Line(LineB, quantity: 1, unitPrice: 9000m, categoryIds: [99L]),
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(4000m, result.EligibleSubtotal);
        Assert.Equal(LineA, Assert.Single(result.Allocations).LineId);
    }

    [Fact]
    public void RestrictedScope_WithoutAnyInclusion_IsInvalid()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, scopeType: CouponScopeType.Restricted),
            new CouponScopeRules(CouponScopeType.Restricted, [], [], []),
            [Line(LineA, quantity: 1, unitPrice: 4000m)]);

        Assert.Equal(CouponCalculationErrorCodes.CouponInvalid, result.ErrorCode);
    }

    [Theory]
    [InlineData(CouponStatus.Draft)]
    [InlineData(CouponStatus.Scheduled)]
    [InlineData(CouponStatus.Paused)]
    [InlineData(CouponStatus.Expired)]
    [InlineData(CouponStatus.Exhausted)]
    [InlineData(CouponStatus.Disabled)]
    public void NonActiveStatus_IsRejected(CouponStatus status)
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, status: status),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 5000m)]);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotActive, result.ErrorCode);
    }

    [Fact]
    public void ActiveStatus_StillRechecksTheUsagePeriod()
    {
        var rule = Rule(CouponDiscountType.FixedAmount, discountValue: 300m);
        var lines = new[] { Line(LineA, quantity: 1, unitPrice: 5000m) };

        var beforeStart = Calculate(rule, CouponScopeRules.SiteWide, lines, evaluatedAtUtc: StartsAtUtc.AddSeconds(-1));
        var atEnd = Calculate(rule, CouponScopeRules.SiteWide, lines, evaluatedAtUtc: EndsAtUtc);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotActive, beforeStart.ErrorCode);
        Assert.Equal(CouponCalculationErrorCodes.CouponNotActive, atEnd.ErrorCode);
    }

    [Fact]
    public void MemberOnlyCoupon_RejectsGuests()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, memberOnly: true),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 5000m)],
            isAuthenticatedMember: false);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, result.ErrorCode);
    }

    [Fact]
    public void TotalUsageLimit_IsExhaustedOnTheLastSeat()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, totalUsageLimit: 100),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 5000m)],
            usage: new CouponUsageState(TotalRedeemedCount: 100, MemberRedeemedCount: 0));

        Assert.Equal(CouponCalculationErrorCodes.CouponUsageExhausted, result.ErrorCode);
    }

    [Fact]
    public void PerMemberLimit_IsExhaustedIndependentlyOfTheTotal()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m, totalUsageLimit: 100, perMemberLimit: 1),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 5000m)],
            usage: new CouponUsageState(TotalRedeemedCount: 3, MemberRedeemedCount: 1));

        Assert.Equal(CouponCalculationErrorCodes.CouponUsageExhausted, result.ErrorCode);
    }

    [Fact]
    public void FreeShipping_FlagsShippingWithoutDiscountingMerchandise()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FreeShipping, discountValue: null),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 5000m)]);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsFreeShipping);
        Assert.False(result.IsAssemblyFreeShipping);
        Assert.Equal(0m, result.DiscountAmount);
        Assert.Equal(5000m, result.EligibleSubtotal);
        Assert.Empty(result.Allocations);
    }

    [Fact]
    public void FreeShipping_DoesNotCoverAssemblyDelivery()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FreeShipping, discountValue: null),
            CouponScopeRules.SiteWide,
            [Line(LineA, quantity: 1, unitPrice: 5000m)],
            isAssemblyDelivery: true);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, result.ErrorCode);
    }

    [Fact]
    public void AssemblyFreeShipping_OnlyCoversAssemblyDelivery()
    {
        var rule = Rule(CouponDiscountType.AssemblyFreeShipping, discountValue: null);
        var lines = new[] { Line(LineA, quantity: 1, unitPrice: 5000m) };

        var assembly = Calculate(rule, CouponScopeRules.SiteWide, lines, isAssemblyDelivery: true);
        var standard = Calculate(rule, CouponScopeRules.SiteWide, lines, isAssemblyDelivery: false);

        Assert.True(assembly.IsAssemblyFreeShipping);
        Assert.False(assembly.IsFreeShipping);
        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, standard.ErrorCode);
    }

    [Fact]
    public void EmptyCart_IsNotApplicable()
    {
        var result = Calculate(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m),
            CouponScopeRules.SiteWide,
            []);

        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, result.ErrorCode);
    }

    [Fact]
    public void NonUtcEvaluationTime_IsARejectedProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => CouponCalculator.Calculate(new CouponCalculationRequest(
            Rule(CouponDiscountType.FixedAmount, discountValue: 300m),
            CouponScopeRules.SiteWide,
            CouponUsageState.Unused,
            [Line(LineA, quantity: 1, unitPrice: 5000m)],
            IsAuthenticatedMember: true,
            IsAssemblyDelivery: false,
            EvaluatedAtUtc: new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Local))));
    }

    [Fact]
    public void CouponRule_MirrorsThePersistedCoupon()
    {
        var coupon = new Coupon(Guid.NewGuid(), new CouponCreation(
            "  welcome300  ", "新會員", CouponDiscountType.FixedAmount, 300m, 3000m, null,
            StartsAtUtc, EndsAtUtc, 100, 1, false, false, CouponScopeType.All), StartsAtUtc);

        var rule = CouponRule.From(coupon);

        Assert.Equal("WELCOME300", rule.Code);
        Assert.Equal(CouponStatus.Draft, rule.Status);
        Assert.Equal(1, rule.RuleVersion);
        Assert.Equal(3000m, rule.MinimumSpend);
    }

    private static CouponCalculationResult Calculate(
        CouponRule rule,
        CouponScopeRules scope,
        IReadOnlyList<CouponCalculationLine> lines,
        CouponUsageState? usage = null,
        bool isAuthenticatedMember = true,
        bool isAssemblyDelivery = false,
        DateTime? evaluatedAtUtc = null) =>
        CouponCalculator.Calculate(new CouponCalculationRequest(
            rule,
            scope,
            usage ?? CouponUsageState.Unused,
            lines,
            isAuthenticatedMember,
            isAssemblyDelivery,
            evaluatedAtUtc ?? EvaluatedAtUtc));

    private static CouponRule Rule(
        CouponDiscountType discountType,
        decimal? discountValue,
        decimal? minimumSpend = null,
        decimal? maximumDiscount = null,
        int? totalUsageLimit = null,
        int? perMemberLimit = null,
        bool memberOnly = false,
        bool excludeSaleItems = false,
        CouponScopeType scopeType = CouponScopeType.All,
        CouponStatus status = CouponStatus.Active) =>
        new(
            "WELCOME300",
            discountType,
            discountValue,
            minimumSpend,
            maximumDiscount,
            StartsAtUtc,
            EndsAtUtc,
            totalUsageLimit,
            perMemberLimit,
            memberOnly,
            excludeSaleItems,
            scopeType,
            status,
            RuleVersion: 1);

    private static CouponCalculationLine Line(
        Guid lineId,
        int quantity,
        decimal unitPrice,
        long productId = 1L,
        IReadOnlyCollection<long>? categoryIds = null,
        bool isOnSale = false) =>
        new(lineId, productId, categoryIds ?? [], quantity, unitPrice, isOnSale);
}
