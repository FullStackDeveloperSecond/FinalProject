using DoSelect.Domain.Promotions;

namespace DoSelect.Domain.Tests;

/// <summary>
/// 優惠券生命週期的正式轉移表（DEC-BATCH-014 第 2 項）。
/// </summary>
public sealed class CouponLifecycleTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ANewCouponStartsAsDraft() =>
        Assert.Equal(CouponStatus.Draft, CreateCoupon().Status);

    [Fact]
    public void DraftCanBeScheduledThenActivated()
    {
        var coupon = CreateCoupon();

        coupon.Schedule(CreatedAtUtc.AddMinutes(1));
        Assert.Equal(CouponStatus.Scheduled, coupon.Status);

        coupon.Activate(CreatedAtUtc.AddMinutes(2));
        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void DraftCanBeActivatedDirectly()
    {
        var coupon = CreateCoupon();

        coupon.Activate(CreatedAtUtc.AddMinutes(1));

        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void ActiveCanBePausedAndResumed()
    {
        var coupon = CreateActiveCoupon();

        coupon.Pause(CreatedAtUtc.AddMinutes(2));
        Assert.Equal(CouponStatus.Paused, coupon.Status);

        coupon.Activate(CreatedAtUtc.AddMinutes(3));
        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void ExhaustedReturnsToActiveWhenSeatsAreGivenBack()
    {
        var coupon = CreateActiveCoupon();

        coupon.MarkExhausted(CreatedAtUtc.AddMinutes(2));
        Assert.Equal(CouponStatus.Exhausted, coupon.Status);

        coupon.Activate(CreatedAtUtc.AddMinutes(3));
        Assert.Equal(CouponStatus.Active, coupon.Status);
    }

    [Fact]
    public void ExpiredIsTerminalEvenAfterSeatsAreGivenBack()
    {
        var coupon = CreateActiveCoupon();
        coupon.MarkExhausted(CreatedAtUtc.AddMinutes(2));
        coupon.MarkExpired(CreatedAtUtc.AddMinutes(3));

        Assert.Equal(CouponStatus.Expired, coupon.Status);
        Assert.Throws<InvalidOperationException>(() => coupon.Activate(CreatedAtUtc.AddMinutes(4)));
    }

    [Fact]
    public void DisabledIsTerminalAndCannotBeReactivated()
    {
        var coupon = CreateActiveCoupon();

        coupon.Disable(CreatedAtUtc.AddMinutes(2));

        Assert.Equal(CouponStatus.Disabled, coupon.Status);
        Assert.Throws<InvalidOperationException>(() => coupon.Activate(CreatedAtUtc.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => coupon.Pause(CreatedAtUtc.AddMinutes(3)));
    }

    [Fact]
    public void DraftCannotBePausedOrExhaustedOrExpired()
    {
        var coupon = CreateCoupon();

        Assert.Throws<InvalidOperationException>(() => coupon.Pause(CreatedAtUtc.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => coupon.MarkExhausted(CreatedAtUtc.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => coupon.MarkExpired(CreatedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void ScheduledCannotBePausedOrExhausted()
    {
        var coupon = CreateCoupon();
        coupon.Schedule(CreatedAtUtc.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => coupon.Pause(CreatedAtUtc.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => coupon.MarkExhausted(CreatedAtUtc.AddMinutes(2)));
    }

    [Fact]
    public void PausedCannotGoStraightToExhausted()
    {
        var coupon = CreateActiveCoupon();
        coupon.Pause(CreatedAtUtc.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() =>
            coupon.MarkExhausted(CreatedAtUtc.AddMinutes(3)));
    }

    [Fact]
    public void EveryNonTerminalStatusCanBeDisabled()
    {
        var draft = CreateCoupon();
        draft.Disable(CreatedAtUtc.AddMinutes(1));
        Assert.Equal(CouponStatus.Disabled, draft.Status);

        var scheduled = CreateCoupon();
        scheduled.Schedule(CreatedAtUtc.AddMinutes(1));
        scheduled.Disable(CreatedAtUtc.AddMinutes(2));
        Assert.Equal(CouponStatus.Disabled, scheduled.Status);

        var paused = CreateActiveCoupon();
        paused.Pause(CreatedAtUtc.AddMinutes(2));
        paused.Disable(CreatedAtUtc.AddMinutes(3));
        Assert.Equal(CouponStatus.Disabled, paused.Status);

        var exhausted = CreateActiveCoupon();
        exhausted.MarkExhausted(CreatedAtUtc.AddMinutes(2));
        exhausted.Disable(CreatedAtUtc.AddMinutes(3));
        Assert.Equal(CouponStatus.Disabled, exhausted.Status);
    }

    [Fact]
    public void ATransitionRequiresUtc() =>
        Assert.Throws<ArgumentException>(() => CreateCoupon()
            .Activate(new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Local)));

    [Fact]
    public void TheRuleSnapshotFollowsTheEntityStatus()
    {
        var coupon = CreateActiveCoupon();

        Assert.Equal(CouponStatus.Active, CouponRule.From(coupon).Status);

        coupon.Pause(CreatedAtUtc.AddMinutes(2));

        Assert.Equal(CouponStatus.Paused, CouponRule.From(coupon).Status);
    }

    [Fact]
    public void OrderCouponSnapshotsTheMinimumSpendAtCheckout()
    {
        var snapshot = new OrderCoupon(Guid.NewGuid(), 1, 1, 1, "WELCOME300", "新會員",
            CouponDiscountType.FixedAmount, 1, 300m, minimumSpendAmount: 3000m,
            appliedAmount: 300m, eligibleSubtotal: 4000m, isFreeShipping: false, CreatedAtUtc);

        Assert.Equal(3000m, snapshot.MinimumSpendAmount);
    }

    [Fact]
    public void OrderCouponAcceptsNoMinimumSpend()
    {
        var snapshot = new OrderCoupon(Guid.NewGuid(), 1, 1, 1, "FREESHIP", "免運",
            CouponDiscountType.FreeShipping, 1, null, minimumSpendAmount: null,
            appliedAmount: 0m, eligibleSubtotal: 4000m, isFreeShipping: true, CreatedAtUtc);

        Assert.Null(snapshot.MinimumSpendAmount);
    }

    [Fact]
    public void OrderCouponRejectsANegativeMinimumSpend() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderCoupon(
            Guid.NewGuid(), 1, 1, 1, "BAD", "錯誤", CouponDiscountType.FixedAmount, 1, 300m,
            minimumSpendAmount: -1m, appliedAmount: 300m, eligibleSubtotal: 4000m,
            isFreeShipping: false, CreatedAtUtc));

    private static Coupon CreateActiveCoupon()
    {
        var coupon = CreateCoupon();
        coupon.Activate(CreatedAtUtc.AddMinutes(1));
        return coupon;
    }

    private static Coupon CreateCoupon() =>
        new(Guid.NewGuid(), new CouponCreation(
            "WELCOME300", "新會員", CouponDiscountType.FixedAmount, 300m, 3000m, null,
            CreatedAtUtc, CreatedAtUtc.AddDays(30), 100, 1, false, false,
            CouponScopeType.All), CreatedAtUtc);
}
