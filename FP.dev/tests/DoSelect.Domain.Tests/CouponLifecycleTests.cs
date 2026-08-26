using DoSelect.Domain.Promotions;

namespace DoSelect.Domain.Tests;

/// <summary>
/// 優惠券生命週期（DEC-BATCH-014 第 2 項）。
/// 除了狀態名稱，每個動作還必須守住轉移表列出的必要條件。
/// </summary>
public sealed class CouponLifecycleTests
{
    private static readonly DateTime StartsAtUtc = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndsAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforeStart = StartsAtUtc.AddDays(-1);
    private static readonly DateTime InsidePeriod = StartsAtUtc.AddDays(1);
    private static readonly DateTime AfterEnd = EndsAtUtc.AddMinutes(1);

    private static readonly CouponUsageState Unused = CouponUsageState.Unused;

    [Fact]
    public void ANewCouponStartsAsDraft() =>
        Assert.Equal(CouponStatus.Draft, CreateCoupon().Status);

    [Fact]
    public void SchedulingBeforeTheStartTimeIsAllowed()
    {
        var coupon = CreateCoupon();

        coupon.ScheduleForLaterStart(BeforeStart);

        Assert.Equal(CouponStatus.Scheduled, coupon.Status);
    }

    [Fact]
    public void SchedulingAfterTheStartTimeIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            CreateCoupon().ScheduleForLaterStart(InsidePeriod));

    [Fact]
    public void SchedulingAnIncompletePercentageRuleIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            CreatePercentageCouponWithoutCap().ScheduleForLaterStart(BeforeStart));

    [Fact]
    public void ActivatingInsideThePeriodIsAllowed()
    {
        var coupon = CreateCoupon();

        coupon.ActivateNow(Unused, InsidePeriod);

        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void ActivatingBeforeTheStartTimeIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            CreateCoupon().ActivateNow(Unused, BeforeStart));

    [Fact]
    public void ActivatingAfterTheEndTimeIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            CreateCoupon().ActivateNow(Unused, AfterEnd));

    [Fact]
    public void ActivatingWithoutRemainingQuotaIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            CreateCoupon().ActivateNow(new CouponUsageState(100, 0), InsidePeriod));

    [Fact]
    public void ActivatingAnIncompletePercentageRuleIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            CreatePercentageCouponWithoutCap().ActivateNow(Unused, InsidePeriod));

    [Fact]
    public void AScheduledCouponActivatesOnceInsideThePeriod()
    {
        var coupon = CreateCoupon();
        coupon.ScheduleForLaterStart(BeforeStart);

        coupon.ActivateScheduled(Unused, InsidePeriod);

        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void TheAdministratorActivateCommandDoesNotConsumeScheduledEvents()
    {
        var coupon = CreateCoupon();
        coupon.ScheduleForLaterStart(BeforeStart);

        Assert.Throws<InvalidOperationException>(() => coupon.ActivateNow(Unused, InsidePeriod));
    }

    [Fact]
    public void ActiveCanBePausedAndResumed()
    {
        var coupon = CreateActiveCoupon();

        coupon.Pause(InsidePeriod.AddHours(1));
        Assert.Equal(CouponStatus.Paused, coupon.Status);

        coupon.ActivateNow(Unused, InsidePeriod.AddHours(2));
        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void APausedCouponCannotResumeAfterItsEndTime()
    {
        var coupon = CreateActiveCoupon();
        coupon.Pause(InsidePeriod.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => coupon.ActivateNow(Unused, AfterEnd));
    }

    [Fact]
    public void DraftAndScheduledCannotBePaused()
    {
        Assert.Throws<InvalidOperationException>(() => CreateCoupon().Pause(InsidePeriod));

        var scheduled = CreateCoupon();
        scheduled.ScheduleForLaterStart(BeforeStart);
        Assert.Throws<InvalidOperationException>(() => scheduled.Pause(InsidePeriod));
    }

    [Fact]
    public void ExhaustionRequiresTheUsageLimitToBeReached()
    {
        var coupon = CreateActiveCoupon();

        Assert.Throws<InvalidOperationException>(() =>
            coupon.MarkExhausted(new CouponUsageState(99, 0), InsidePeriod.AddHours(1)));

        coupon.MarkExhausted(new CouponUsageState(100, 0), InsidePeriod.AddHours(1));
        Assert.Equal(CouponStatus.Exhausted, coupon.Status);
    }

    [Fact]
    public void ACouponWithoutATotalLimitIsNeverExhausted()
    {
        var coupon = CreateActiveCoupon(totalUsageLimit: null);

        Assert.Throws<InvalidOperationException>(() =>
            coupon.MarkExhausted(new CouponUsageState(9999, 0), InsidePeriod.AddHours(1)));
    }

    [Fact]
    public void ExhaustedReturnsToActiveOnlyWhenQuotaIsGivenBackInsideThePeriod()
    {
        var coupon = CreateActiveCoupon();
        coupon.MarkExhausted(new CouponUsageState(100, 0), InsidePeriod.AddHours(1));

        Assert.Throws<InvalidOperationException>(() =>
            coupon.ReactivateAfterQuotaRelease(
                new CouponUsageState(100, 0),
                InsidePeriod.AddHours(2)));

        coupon.ReactivateAfterQuotaRelease(new CouponUsageState(99, 0), InsidePeriod.AddHours(3));
        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void TheAdministratorActivateCommandDoesNotConsumeQuotaReleaseEvents()
    {
        var coupon = CreateActiveCoupon();
        coupon.MarkExhausted(new CouponUsageState(100, 0), InsidePeriod.AddHours(1));

        Assert.Throws<InvalidOperationException>(() =>
            coupon.ActivateNow(new CouponUsageState(99, 0), InsidePeriod.AddHours(2)));
    }

    [Fact]
    public void AnExhaustedCouponCannotBeRevivedAfterItsEndTime()
    {
        var coupon = CreateActiveCoupon();
        coupon.MarkExhausted(new CouponUsageState(100, 0), InsidePeriod.AddHours(1));

        Assert.Throws<InvalidOperationException>(() =>
            coupon.ReactivateAfterQuotaRelease(new CouponUsageState(0, 0), AfterEnd));
    }

    [Fact]
    public void ExpiryRequiresTheEndTimeToHavePassed()
    {
        var coupon = CreateActiveCoupon();

        Assert.Throws<InvalidOperationException>(() => coupon.MarkExpired(InsidePeriod.AddHours(1)));

        coupon.MarkExpired(AfterEnd);
        Assert.Equal(CouponStatus.Expired, coupon.Status);
    }

    [Fact]
    public void AScheduledCouponThatPassesItsEndTimeExpiresDirectly()
    {
        var coupon = CreateCoupon();
        coupon.ScheduleForLaterStart(BeforeStart);

        coupon.MarkExpired(AfterEnd);

        Assert.Equal(CouponStatus.Expired, coupon.Status);
    }

    [Fact]
    public void ExpiryCanBeRetriedWithoutChangingTheCompletedTransition()
    {
        var coupon = CreateActiveCoupon();
        coupon.MarkExpired(AfterEnd);
        var firstUpdatedAtUtc = coupon.UpdatedAtUtc;

        coupon.MarkExpired(AfterEnd.AddMinutes(1));

        Assert.Equal(CouponStatus.Expired, coupon.Status);
        Assert.Equal(firstUpdatedAtUtc, coupon.UpdatedAtUtc);
    }

    [Fact]
    public void ExpiredIsTerminalEvenAfterQuotaIsGivenBack()
    {
        var coupon = CreateActiveCoupon();
        coupon.MarkExhausted(new CouponUsageState(100, 0), InsidePeriod.AddHours(1));
        coupon.MarkExpired(AfterEnd);

        Assert.Throws<InvalidOperationException>(() =>
            coupon.ActivateNow(Unused, AfterEnd.AddMinutes(1)));
    }

    [Fact]
    public void DisabledIsTerminalAndCannotBeReactivated()
    {
        var coupon = CreateActiveCoupon();

        coupon.Disable(InsidePeriod.AddHours(1));

        Assert.Equal(CouponStatus.Disabled, coupon.Status);
        Assert.Throws<InvalidOperationException>(() =>
            coupon.ActivateNow(Unused, InsidePeriod.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => coupon.Pause(InsidePeriod.AddHours(2)));
    }

    [Fact]
    public void EveryNonTerminalStatusCanBeDisabled()
    {
        var draft = CreateCoupon();
        draft.Disable(InsidePeriod);
        Assert.Equal(CouponStatus.Disabled, draft.Status);

        var scheduled = CreateCoupon();
        scheduled.ScheduleForLaterStart(BeforeStart);
        scheduled.Disable(InsidePeriod);
        Assert.Equal(CouponStatus.Disabled, scheduled.Status);

        var paused = CreateActiveCoupon();
        paused.Pause(InsidePeriod.AddHours(1));
        paused.Disable(InsidePeriod.AddHours(2));
        Assert.Equal(CouponStatus.Disabled, paused.Status);

        var exhausted = CreateActiveCoupon();
        exhausted.MarkExhausted(new CouponUsageState(100, 0), InsidePeriod.AddHours(1));
        exhausted.Disable(InsidePeriod.AddHours(2));
        Assert.Equal(CouponStatus.Disabled, exhausted.Status);
    }

    [Fact]
    public void ATransitionRequiresUtc() =>
        Assert.Throws<ArgumentException>(() => CreateCoupon()
            .ActivateNow(Unused, DateTime.SpecifyKind(InsidePeriod, DateTimeKind.Local)));

    [Fact]
    public void TheRuleSnapshotFollowsTheEntityStatus()
    {
        var coupon = CreateActiveCoupon();

        Assert.Equal(CouponStatus.Active, CouponRule.From(coupon).Status);

        coupon.Pause(InsidePeriod.AddHours(1));

        Assert.Equal(CouponStatus.Paused, CouponRule.From(coupon).Status);
    }

    // OrderCoupon.MinimumSpendAmount 的 Entity、Configuration、Migration、ModelSnapshot
    // 與 Provider-backed 驗證已由獨立 DES-21 Migration PR 交付；本測試類別只驗證
    // Coupon 生命週期（DEC-P300）。

    private static Coupon CreateActiveCoupon(int? totalUsageLimit = 100)
    {
        var coupon = CreateCoupon(totalUsageLimit);
        coupon.ActivateNow(Unused, InsidePeriod);
        return coupon;
    }

    private static Coupon CreateCoupon(int? totalUsageLimit = 100) =>
        new(Guid.NewGuid(), new CouponCreation(
            "WELCOME300", "新會員", CouponDiscountType.FixedAmount, 300m, 3000m, null,
            StartsAtUtc, EndsAtUtc, totalUsageLimit, 1, false, false,
            CouponScopeType.All), StartsAtUtc.AddDays(-10));

    private static Coupon CreatePercentageCouponWithoutCap() =>
        new(Guid.NewGuid(), new CouponCreation(
            "CREATOR10", "創作者", CouponDiscountType.Percentage, 0.1m, 20000m, null,
            StartsAtUtc, EndsAtUtc, 100, 1, false, false,
            CouponScopeType.All), StartsAtUtc.AddDays(-10));
}
