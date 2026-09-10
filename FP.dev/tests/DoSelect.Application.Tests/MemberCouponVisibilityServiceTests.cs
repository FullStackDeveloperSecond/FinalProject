using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Tests;

public sealed class MemberCouponVisibilityServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ShouldDisplayAsync_ReturnsTrue_ForActiveUnusedMemberCouponWithinJoinYear()
    {
        var reader = new FakeReader(CreateSnapshot(), CouponUsageState.Unused, NowUtc.AddMonths(-6));

        var result = await CreateService(reader).ShouldDisplayAsync("member-1", "member100");

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldDisplayAsync_ReturnsFalse_WhenMemberUsageLimitWasReached()
    {
        var reader = new FakeReader(
            CreateSnapshot(),
            new CouponUsageState(TotalRedeemedCount: 1, MemberRedeemedCount: 1),
            NowUtc.AddMonths(-6));

        var result = await CreateService(reader).ShouldDisplayAsync("member-1", "MEMBER100");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldDisplayAsync_ReturnsFalse_WhenMembershipValidityExpired()
    {
        var reader = new FakeReader(CreateSnapshot(), CouponUsageState.Unused, NowUtc.AddYears(-1));

        var result = await CreateService(reader).ShouldDisplayAsync("member-1", "MEMBER100");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldDisplayAsync_ReturnsFalse_ForInactiveOrMissingCoupon()
    {
        var inactive = new FakeReader(
            CreateSnapshot(CouponStatus.Disabled),
            CouponUsageState.Unused,
            NowUtc.AddMonths(-6));
        var missing = new FakeReader(null, CouponUsageState.Unused, NowUtc.AddMonths(-6));

        Assert.False(await CreateService(inactive).ShouldDisplayAsync("member-1", "MEMBER100"));
        Assert.False(await CreateService(missing).ShouldDisplayAsync("member-1", "MEMBER100"));
    }

    private static MemberCouponVisibilityService CreateService(ICouponRuleReader reader) =>
        new(reader, new FakeTimeProvider(new DateTimeOffset(NowUtc)));

    private static CouponRuleSnapshot CreateSnapshot(CouponStatus status = CouponStatus.Active) =>
        new(
            CouponId: 42,
            new CouponRule(
                Code: "MEMBER100",
                DiscountType: CouponDiscountType.FixedAmount,
                DiscountValue: 100m,
                MinimumSpend: 1000m,
                MaximumDiscount: null,
                StartsAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndsAtUtc: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                TotalUsageLimit: null,
                PerMemberLimit: 1,
                MemberOnly: true,
                ExcludeSaleItems: false,
                ScopeType: CouponScopeType.All,
                Status: status,
                RuleVersion: 1,
                MemberValidityMonths: 12),
            CouponScopeRules.SiteWide,
            "會員入會禮");

    private sealed class FakeReader(
        CouponRuleSnapshot? snapshot,
        CouponUsageState usage,
        DateTime? memberCreatedAtUtc) : ICouponRuleReader
    {
        public Task<DateTime?> GetMemberCreatedAtUtcAsync(
            string memberUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(memberCreatedAtUtc);

        public Task<CouponRuleSnapshot?> FindByCodeAsync(
            string normalizedCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<CouponUsageState> GetUsageAsync(
            long couponId,
            string? memberUserId,
            byte[]? guestUsageKeyHash,
            DateTime evaluatedAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(usage);
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
