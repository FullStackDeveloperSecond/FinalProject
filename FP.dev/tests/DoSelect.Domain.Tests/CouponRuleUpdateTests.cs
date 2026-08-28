using DoSelect.Domain.Promotions;

namespace DoSelect.Domain.Tests;

/// <summary>
/// 管理員修改優惠券規則（`PUT /api/v1/admin/coupons/{id}`）。
/// 重點是三件事：既有訂單不受影響、優惠碼在有 Redemption 後凍結、
/// 以及 <see cref="Coupon.RuleVersion"/> 只在規則真的變動時推進。
/// </summary>
public sealed class CouponRuleUpdateTests
{
    private static readonly DateTime StartsAtUtc = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndsAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InsidePeriod = StartsAtUtc.AddDays(1);
    private static readonly DateTime AfterEnd = EndsAtUtc.AddMinutes(1);

    private static readonly CouponUsageState Unused = CouponUsageState.Unused;

    [Fact]
    public void ChangingNothingReportsNoChangeAndDoesNotAdvanceTheRuleVersion()
    {
        var coupon = CreateCoupon();
        var before = coupon.RuleVersion;

        var change = coupon.UpdateRules(RevisionOf(coupon), hasRedemptions: false, scopeChanged: false, InsidePeriod);

        Assert.False(change.HasChanges);
        Assert.False(change.RuleVersionAdvanced);
        Assert.Empty(change.ChangedFields);
        Assert.Equal(before, coupon.RuleVersion);
    }

    [Fact]
    public void ChangingADiscountRuleAdvancesTheRuleVersion()
    {
        // 版本推進是「既有訂單不受影響」的憑據：OrderCoupon 抄下的 RuleVersion
        // 必須能分辨自己套用的是修改前還是修改後的規則。
        var coupon = CreateCoupon();
        var before = coupon.RuleVersion;

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { DiscountValue = 500m },
            hasRedemptions: false,
            scopeChanged: false,
            InsidePeriod);

        Assert.True(change.RuleVersionAdvanced);
        Assert.Equal(before + 1, coupon.RuleVersion);
        Assert.Equal(500m, coupon.DiscountValue);
        Assert.Contains(nameof(Coupon.DiscountValue), change.ChangedFields);
    }

    [Fact]
    public void ChangingOnlyTheNameIsRecordedButDoesNotAdvanceTheRuleVersion()
    {
        // 名稱不參與任何計算，而且已另行抄進 OrderCoupon.CouponName。
        // 推進版本只會讓稽核分不清哪一次才是真正的規則異動。
        var coupon = CreateCoupon();
        var before = coupon.RuleVersion;

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { NameZhTw = "新會員（改名）" },
            hasRedemptions: false,
            scopeChanged: false,
            InsidePeriod);

        Assert.True(change.HasChanges);
        Assert.False(change.RuleVersionAdvanced);
        Assert.Equal(before, coupon.RuleVersion);
        Assert.Equal([nameof(Coupon.NameZhTw)], change.ChangedFields);
    }

    [Fact]
    public void TheRuleVersionAdvancesOncePerUpdateNotOncePerChangedField()
    {
        var coupon = CreateCoupon();
        var before = coupon.RuleVersion;

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with
            {
                DiscountValue = 500m,
                MinimumSpend = 4000m,
                PerMemberLimit = 2,
            },
            hasRedemptions: false,
            scopeChanged: false,
            InsidePeriod);

        Assert.Equal(before + 1, coupon.RuleVersion);
        Assert.Equal(3, change.ChangedFields.Count);
    }

    [Fact]
    public void AScopeOnlyChangeAdvancesTheRuleVersion()
    {
        // 適用範圍是計算的一部分。若「只改範圍」被判定成沒有任何變動，
        // Entity 不會被修改，EF 也就不會對 Coupons 發出 UPDATE ——
        // 呼叫端的 RowVersion 因此從未被比對。
        var coupon = CreateCoupon();
        var before = coupon.RuleVersion;
        var updatedBefore = coupon.UpdatedAtUtc;

        var change = coupon.UpdateRules(
            RevisionOf(coupon),
            hasRedemptions: false,
            scopeChanged: true,
            InsidePeriod);

        Assert.True(change.HasChanges);
        Assert.True(change.RuleVersionAdvanced);
        Assert.Equal(before + 1, coupon.RuleVersion);
        Assert.Equal([Coupon.ScopeFieldName], change.ChangedFields);
        Assert.NotEqual(updatedBefore, coupon.UpdatedAtUtc);
    }

    [Fact]
    public void AScopeChangeAlongsideRuleChangesStillAdvancesTheVersionOnce()
    {
        var coupon = CreateCoupon();
        var before = coupon.RuleVersion;

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { MinimumSpend = 4000m },
            hasRedemptions: false,
            scopeChanged: true,
            InsidePeriod);

        Assert.Equal(before + 1, coupon.RuleVersion);
        Assert.Equal(2, change.ChangedFields.Count);
        Assert.Contains(nameof(Coupon.MinimumSpend), change.ChangedFields);
        Assert.Contains(Coupon.ScopeFieldName, change.ChangedFields);
    }

    [Fact]
    public void AScopeChangeIsRecordedSeparatelyFromTheScopeType()
    {
        // ScopeType 與三個集合是兩件事：All → Restricted 會同時改變兩者，
        // 但只換分類時 ScopeType 不動，changedFields 仍必須看得到範圍變了。
        var coupon = CreateCoupon();

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { ScopeType = CouponScopeType.Restricted },
            hasRedemptions: false,
            scopeChanged: true,
            InsidePeriod);

        Assert.Contains(nameof(Coupon.ScopeType), change.ChangedFields);
        Assert.Contains(Coupon.ScopeFieldName, change.ChangedFields);
    }

    [Fact]
    public void AScopeOnlyChangeIsStillBlockedOnATerminalCoupon()
    {
        var coupon = CreateCoupon();
        coupon.Disable(InsidePeriod);

        Assert.Throws<InvalidOperationException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon),
                hasRedemptions: false,
                scopeChanged: true,
                InsidePeriod));
    }

    [Fact]
    public void TheCodeIsFrozenOnceARedemptionExists()
    {
        // 優惠碼已寫進 OrderCoupon 快照；改掉會讓歷史訂單指向一個對不上的代碼。
        var coupon = CreateCoupon();

        var exception = Assert.Throws<InvalidOperationException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon) with { Code = "WELCOME500" },
                hasRedemptions: true,
                scopeChanged: false,
                InsidePeriod));

        Assert.Contains("frozen", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WELCOME300", coupon.Code);
    }

    [Fact]
    public void TheCodeCanStillChangeBeforeAnyRedemptionExists()
    {
        var coupon = CreateCoupon();

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { Code = "WELCOME500" },
            hasRedemptions: false,
            scopeChanged: false,
            InsidePeriod);

        Assert.Equal("WELCOME500", coupon.Code);
        Assert.True(change.RuleVersionAdvanced);
    }

    [Fact]
    public void ResubmittingTheSameCodeIsNotTreatedAsAChangeEvenWithRedemptions()
    {
        // 管理員只是改門檻、沒動代碼時，整份規則仍會把 Code 一起送回來。
        // 凍結檢查必須比對值，不是比對「有沒有出現在請求裡」。
        var coupon = CreateCoupon();

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { MinimumSpend = 4000m },
            hasRedemptions: true,
            scopeChanged: false,
            InsidePeriod);

        Assert.Equal("WELCOME300", coupon.Code);
        Assert.DoesNotContain(nameof(Coupon.Code), change.ChangedFields);
    }

    [Fact]
    public void TheCodeIsNormalizedBeforeTheFrozenCheck()
    {
        // 小寫、前後空白的同一個代碼不是變更，不該被凍結檢查誤擋。
        var coupon = CreateCoupon();

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { Code = "  welcome300  " },
            hasRedemptions: true,
            scopeChanged: false,
            InsidePeriod);

        Assert.False(change.HasChanges);
        Assert.Equal("WELCOME300", coupon.Code);
    }

    [Theory]
    [InlineData(CouponStatus.Expired)]
    [InlineData(CouponStatus.Disabled)]
    public void ATerminalCouponCannotBeModified(CouponStatus terminal)
    {
        var coupon = CreateCoupon();
        if (terminal == CouponStatus.Expired)
        {
            // Draft 不能直接到期；轉移表要求先進入 Scheduled 或 Active。
            coupon.ActivateNow(Unused, InsidePeriod);
            coupon.MarkExpired(AfterEnd);
        }
        else
        {
            coupon.Disable(InsidePeriod);
        }

        Assert.Equal(terminal, coupon.Status);

        Assert.Throws<InvalidOperationException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon) with { MinimumSpend = 1m },
                hasRedemptions: false,
                scopeChanged: false,
                InsidePeriod));
    }

    [Theory]
    [InlineData(CouponStatus.Draft)]
    [InlineData(CouponStatus.Scheduled)]
    [InlineData(CouponStatus.Active)]
    [InlineData(CouponStatus.Paused)]
    [InlineData(CouponStatus.Exhausted)]
    public void ANonTerminalCouponCanBeModified(CouponStatus status)
    {
        // 進行中的券也能改：既有訂單已抄下自己的快照，不受影響。
        var coupon = MoveTo(status);

        var change = coupon.UpdateRules(
            RevisionOf(coupon) with { NameZhTw = "改名" },
            hasRedemptions: false,
            scopeChanged: false,
            InsidePeriod);

        Assert.True(change.HasChanges);
        Assert.Equal(status, coupon.Status);
    }

    [Fact]
    public void ModifyingACouponNeverChangesItsStatus()
    {
        // 修改不是狀態轉移。把結束時間改到過去也不會自動到期 ——
        // 進入 Expired 只能由 MarkExpired 觸發。
        var coupon = MoveTo(CouponStatus.Active);

        coupon.UpdateRules(
            RevisionOf(coupon) with { EndsAtUtc = InsidePeriod.AddMinutes(-1) },
            hasRedemptions: false,
            scopeChanged: false,
            InsidePeriod);

        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void AnInvalidPeriodIsRejected()
    {
        var coupon = CreateCoupon();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon) with { EndsAtUtc = StartsAtUtc },
                hasRedemptions: false,
                scopeChanged: false,
                InsidePeriod));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveUsageLimitIsRejected(int limit)
    {
        var coupon = CreateCoupon();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon) with { TotalUsageLimit = limit },
                hasRedemptions: false,
                scopeChanged: false,
                InsidePeriod));
    }

    [Fact]
    public void APercentageRateAboveOneIsRejected()
    {
        // 建立時擋下的東西，修改時也必須擋下 —— 兩條路徑共用同一份檢查。
        var coupon = CreateCoupon();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon) with
                {
                    DiscountType = CouponDiscountType.Percentage,
                    DiscountValue = 10m,
                },
                hasRedemptions: false,
                scopeChanged: false,
                InsidePeriod));
    }

    [Fact]
    public void ANegativeAmountIsRejected()
    {
        var coupon = CreateCoupon();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon) with { MinimumSpend = -1m },
                hasRedemptions: false,
                scopeChanged: false,
                InsidePeriod));
    }

    [Fact]
    public void ARejectedUpdateLeavesTheCouponUntouched()
    {
        // 驗證失敗必須在任何欄位被寫入之前發生，否則會留下半套規則。
        var coupon = CreateCoupon();
        var name = coupon.NameZhTw;
        var ruleVersion = coupon.RuleVersion;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon) with { NameZhTw = "改名", MinimumSpend = -1m },
                hasRedemptions: false,
                scopeChanged: false,
                InsidePeriod));

        Assert.Equal(name, coupon.NameZhTw);
        Assert.Equal(ruleVersion, coupon.RuleVersion);
    }

    [Fact]
    public void ANonUtcTimestampIsRejected()
    {
        var coupon = CreateCoupon();

        Assert.Throws<ArgumentException>(
            () => coupon.UpdateRules(
                RevisionOf(coupon),
                hasRedemptions: false,
                scopeChanged: false,
                new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void AnUpdateThatCompletesTheRuleMakesAnIncompleteCouponActivatable()
    {
        // 百分比券少了最高折抵時 HasCompleteDiscountRule 為 false、無法啟用；
        // 補上之後同一張券就能啟用，不需要重建。
        var coupon = CreatePercentageCouponWithoutCap();
        Assert.False(coupon.HasCompleteDiscountRule);

        coupon.UpdateRules(
            RevisionOf(coupon) with { MaximumDiscount = 2000m },
            hasRedemptions: false,
            scopeChanged: false,
            InsidePeriod);

        Assert.True(coupon.HasCompleteDiscountRule);
        coupon.ActivateNow(Unused, InsidePeriod);
        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    private static Coupon MoveTo(CouponStatus status)
    {
        var coupon = CreateCoupon();
        switch (status)
        {
            case CouponStatus.Draft:
                break;
            case CouponStatus.Scheduled:
                coupon.ScheduleForLaterStart(StartsAtUtc.AddDays(-1));
                break;
            case CouponStatus.Active:
                coupon.ActivateNow(Unused, InsidePeriod);
                break;
            case CouponStatus.Paused:
                coupon.ActivateNow(Unused, InsidePeriod);
                coupon.Pause(InsidePeriod);
                break;
            case CouponStatus.Exhausted:
                coupon.ActivateNow(Unused, InsidePeriod);
                coupon.MarkExhausted(new CouponUsageState(100, 0), InsidePeriod);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return coupon;
    }

    /// <summary>
    /// 目前規則的完整複本。管理端送的是整份規則，所以「沒有變更」的請求
    /// 長得就是這樣，測試用 <c>with</c> 只改要驗證的那一個欄位。
    /// </summary>
    private static CouponRuleRevision RevisionOf(Coupon coupon) =>
        new(
            coupon.Code,
            coupon.NameZhTw,
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
            coupon.ScopeType);

    private static Coupon CreateCoupon() =>
        new(Guid.NewGuid(), new CouponCreation(
            "WELCOME300", "新會員", CouponDiscountType.FixedAmount, 300m, 3000m, null,
            StartsAtUtc, EndsAtUtc, 100, 1, false, false,
            CouponScopeType.All), StartsAtUtc.AddDays(-10));

    private static Coupon CreatePercentageCouponWithoutCap() =>
        new(Guid.NewGuid(), new CouponCreation(
            "CREATOR10", "創作者", CouponDiscountType.Percentage, 0.1m, 20000m, null,
            StartsAtUtc, EndsAtUtc, 100, 1, false, false,
            CouponScopeType.All), StartsAtUtc.AddDays(-10));
}
