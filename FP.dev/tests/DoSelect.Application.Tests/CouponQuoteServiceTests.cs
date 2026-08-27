using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Tests;

public sealed class CouponQuoteServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid LineA = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task TheUsageCountAndThePeriodCheckShareOneEvaluationInstant()
    {
        // 使用量的「Reserved 是否過期」與計算器的期間判斷若用不同時鐘，
        // 兩者可能對同一張券得出矛盾的結論。服務只能取一次時間。
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var service = CreateService(reader);

        await service.QuoteAsync(Request("WELCOME300"));

        Assert.Equal(NowUtc, reader.RequestedEvaluatedAtUtc);
    }

    [Fact]
    public async Task QuoteAsync_NormalizesTheCouponCodeBeforeLookup()
    {
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var service = CreateService(reader);

        var result = await service.QuoteAsync(Request("  welcome300  "));

        Assert.Equal("WELCOME300", reader.RequestedCode);
        Assert.True(result.IsSuccess);
        Assert.Equal(300m, result.DiscountAmount);
    }

    [Fact]
    public async Task QuoteAsync_ReturnsCouponInvalidForBlankCode()
    {
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var service = CreateService(reader);

        var result = await service.QuoteAsync(Request("   "));

        Assert.Equal(CouponCalculationErrorCodes.CouponInvalid, result.ErrorCode);
        Assert.Null(reader.RequestedCode);
    }

    [Fact]
    public async Task QuoteAsync_ReturnsCouponInvalidWhenTheCodeIsUnknown()
    {
        var service = CreateService(new FakeCouponRuleReader(snapshot: null));

        var result = await service.QuoteAsync(Request("NOPE"));

        Assert.Equal(CouponCalculationErrorCodes.CouponInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_PassesTheMemberIdentityToTheUsageLookup()
    {
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var service = CreateService(reader);

        await service.QuoteAsync(Request("WELCOME300", memberUserId: "member-1"));

        Assert.Equal("member-1", reader.RequestedMemberUserId);
        Assert.Null(reader.RequestedGuestUsageKeyHash);
    }

    [Fact]
    public async Task QuoteAsync_PassesTheGuestUsageKeyToTheUsageLookup()
    {
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var service = CreateService(reader);
        var guestKey = new byte[32];

        await service.QuoteAsync(Request("WELCOME300", guestUsageKeyHash: guestKey));

        Assert.Null(reader.RequestedMemberUserId);
        Assert.Same(guestKey, reader.RequestedGuestUsageKeyHash);
    }

    [Fact]
    public async Task QuoteAsync_TreatsAMissingMemberIdAsAGuest()
    {
        var reader = new FakeCouponRuleReader(ActiveCoupon(memberOnly: true));
        var service = CreateService(reader);

        var result = await service.QuoteAsync(Request("WELCOME300"));

        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_ForwardsTheUsageCountsToTheCalculator()
    {
        var reader = new FakeCouponRuleReader(
            ActiveCoupon(totalUsageLimit: 100),
            usage: new CouponUsageState(TotalRedeemedCount: 100, MemberRedeemedCount: 0));
        var service = CreateService(reader);

        var result = await service.QuoteAsync(Request("WELCOME300", memberUserId: "member-1"));

        Assert.Equal(CouponCalculationErrorCodes.CouponUsageExhausted, result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_EvaluatesAgainstTheInjectedClock()
    {
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var expired = new FakeTimeProvider(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero));
        var service = new CouponQuoteService(reader, expired);

        var result = await service.QuoteAsync(Request("WELCOME300", memberUserId: "member-1"));

        Assert.Equal(CouponCalculationErrorCodes.CouponNotActive, result.ErrorCode);
    }

    private static CouponQuoteService CreateService(ICouponRuleReader reader) =>
        new(reader, new FakeTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private static CouponQuoteRequest Request(
        string couponCode,
        string? memberUserId = null,
        byte[]? guestUsageKeyHash = null) =>
        new(
            couponCode,
            [new CouponCalculationLine(LineA, 1L, [], 1, 5000m, IsOnSale: false)],
            memberUserId,
            guestUsageKeyHash,
            IsAssemblyDelivery: false);

    private static CouponRuleSnapshot ActiveCoupon(
        bool memberOnly = false,
        int? totalUsageLimit = null) =>
        new(
            CouponId: 1L,
            new CouponRule(
                "WELCOME300",
                CouponDiscountType.FixedAmount,
                DiscountValue: 300m,
                MinimumSpend: 3000m,
                MaximumDiscount: null,
                StartsAtUtc: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                EndsAtUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                TotalUsageLimit: totalUsageLimit,
                PerMemberLimit: 1,
                memberOnly,
                ExcludeSaleItems: false,
                CouponScopeType.All,
                CouponStatus.Active,
                RuleVersion: 1),
            CouponScopeRules.SiteWide);

    private sealed class FakeCouponRuleReader : ICouponRuleReader
    {
        private readonly CouponRuleSnapshot? _snapshot;
        private readonly CouponUsageState _usage;

        public FakeCouponRuleReader(CouponRuleSnapshot? snapshot, CouponUsageState? usage = null)
        {
            _snapshot = snapshot;
            _usage = usage ?? CouponUsageState.Unused;
        }

        public string? RequestedCode { get; private set; }

        public string? RequestedMemberUserId { get; private set; }

        public byte[]? RequestedGuestUsageKeyHash { get; private set; }

        public Task<CouponRuleSnapshot?> FindByCodeAsync(
            string normalizedCode,
            CancellationToken cancellationToken = default)
        {
            RequestedCode = normalizedCode;
            return Task.FromResult(_snapshot);
        }

        public DateTime? RequestedEvaluatedAtUtc { get; private set; }

        public Task<CouponUsageState> GetUsageAsync(
            long couponId,
            string? memberUserId,
            byte[]? guestUsageKeyHash,
            DateTime evaluatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            RequestedEvaluatedAtUtc = evaluatedAtUtc;
            RequestedMemberUserId = memberUserId;
            RequestedGuestUsageKeyHash = guestUsageKeyHash;
            return Task.FromResult(_usage);
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
