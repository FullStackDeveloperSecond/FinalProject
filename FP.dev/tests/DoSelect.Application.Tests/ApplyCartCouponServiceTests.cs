using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Promotions;
using DoSelect.Application.Idempotency;

namespace DoSelect.Application.Tests;

public sealed class ApplyCartCouponServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CartPublicId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LineA = new("11111111-1111-1111-1111-111111111111");
    private static readonly byte[] CartRowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task ApplyAsync_PutsTheDiscountOnTheCartAndDeductsItFromTheTotal()
    {
        var service = CreateService();

        var cart = await service.ApplyAsync(GuestIdentity, Request("WELCOME300"));

        Assert.Equal("WELCOME300", cart.Coupon!.Code);
        Assert.Equal(300m, cart.Coupon.DiscountAmount);
        Assert.Equal(300m, cart.Amounts.CouponDiscount);

        // 總額必須跟著折扣走。只改 couponDiscount 卻不動 totalEstimate，
        // 畫面會顯示一個沒有反映在應付金額上的折扣。
        Assert.Equal(5000m - 300m, cart.Amounts.TotalEstimate);
    }

    [Fact]
    public async Task ApplyAsync_NormalizesTheCodeItEchoesBack()
    {
        var service = CreateService();

        var cart = await service.ApplyAsync(GuestIdentity, Request("  welcome300  "));

        Assert.Equal("WELCOME300", cart.Coupon!.Code);
    }

    [Fact]
    public async Task ApplyAsync_LeavesEveryOtherAmountToTheCartModule()
    {
        // 這一層只疊上優惠券，不重算小計、運費或組裝費 —— 那些是 Cart 模組的職責，
        // 兩邊各算一次必然會漂移。
        var service = CreateService();

        var cart = await service.ApplyAsync(GuestIdentity, Request("WELCOME300"));

        Assert.Equal(5000m, cart.Amounts.Subtotal);
        Assert.Equal(60m, cart.Amounts.ShippingEstimate);
        Assert.Equal(0m, cart.Amounts.AssemblyFee);
    }

    [Fact]
    public async Task ApplyAsync_RejectsAStaleCartRowVersionAsAConcurrencyConflict()
    {
        // 使用者輸入優惠碼的期間購物車被改過時，試算結果對應的已經不是他看到的購物車。
        // 回一個對不上、永遠結不了帳的折扣，比直接拒絕更難查。
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.ApplyAsync(
                GuestIdentity,
                new ApplyCartCouponRequest("WELCOME300", [9, 9, 9, 9, 9, 9, 9, 9])));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("concurrency_conflict", exception.Code);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsNotFoundWhenTheCallerHasNoCart()
    {
        var service = CreateService(withoutCart: true);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.ApplyAsync(GuestIdentity, Request("WELCOME300")));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task ApplyAsync_MapsAnInvalidCodeTo400AndTheRestTo409()
    {
        // coupon_invalid 是「這個碼本身不合法」，屬呼叫端輸入問題；
        // 其餘三種是「券存在但目前不適用」，屬狀態衝突。兩者的 HTTP 語意不同。
        var service = CreateService();

        var invalid = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.ApplyAsync(GuestIdentity, Request("   ")));
        Assert.Equal(400, invalid.StatusCode);
        Assert.Equal(CouponCalculationErrorCodes.CouponInvalid, invalid.Code);

        var notApplicable = CreateService(snapshot: ActiveCoupon(memberOnly: true));
        var conflict = await Assert.ThrowsAsync<DomainProblemException>(
            () => notApplicable.ApplyAsync(GuestIdentity, Request("WELCOME300")));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(CouponCalculationErrorCodes.CouponNotApplicable, conflict.Code);
    }

    [Fact]
    public async Task ApplyAsync_CountsAGuestByTheSameHashCheckoutUses()
    {
        // 預覽與結帳的雜湊方式必須一致，否則同一位訪客會被算成兩個人，
        // 剩餘名額在兩個畫面上對不起來。
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var service = CreateService(ruleReader: reader);

        await service.ApplyAsync(GuestIdentity, Request("WELCOME300"));

        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes("guest-key-1")),
            reader.RequestedGuestUsageKeyHash);
        Assert.Null(reader.RequestedMemberUserId);
    }

    [Fact]
    public async Task ApplyAsync_CountsAMemberByIdentityNotByHash()
    {
        var reader = new FakeCouponRuleReader(ActiveCoupon());
        var service = CreateService(ruleReader: reader);

        await service.ApplyAsync(new CartIdentity("member-1", null), Request("WELCOME300"));

        Assert.Equal("member-1", reader.RequestedMemberUserId);
        Assert.Null(reader.RequestedGuestUsageKeyHash);
    }

    [Fact]
    public async Task RemoveAsync_ReturnsTheCartWithoutACoupon()
    {
        // 購物車本來就沒有保存優惠碼，所以沒有東西可刪；這支回的是不帶優惠券的當下購物車。
        var service = CreateService();

        var cart = await service.RemoveAsync(GuestIdentity);

        Assert.Null(cart.Coupon);
        Assert.Equal(0m, cart.Amounts.CouponDiscount);
        Assert.Equal(5000m, cart.Amounts.TotalEstimate);
    }

    [Fact]
    public async Task NeitherActionWritesAnything()
    {
        // 優惠券規則第 99～100 行：購物車不保存優惠碼，試算不建立 CouponRedemption。
        // 這一層完全唯讀，因此連交易都不需要。
        var cartService = new FakeCartService();
        var service = CreateService(cartService: cartService);

        await service.ApplyAsync(GuestIdentity, Request("WELCOME300"));
        await service.RemoveAsync(GuestIdentity);

        Assert.Equal(0, cartService.Writes);
    }

    private static CartIdentity GuestIdentity => new(null, "guest-key-1");

    private static ApplyCartCouponRequest Request(string code) => new(code, CartRowVersion);

    private static ApplyCartCouponService CreateService(
        CouponRuleSnapshot? snapshot = null,
        ICouponRuleReader? ruleReader = null,
        FakeCartService? cartService = null,
        bool withoutCart = false) =>
        new(
            cartService ?? new FakeCartService(),
            new FakeCartCouponLineReader(withoutCart ? null : DefaultLines),
            new CouponQuoteService(
                ruleReader ?? new FakeCouponRuleReader(snapshot ?? ActiveCoupon()),
                new FakeTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero))));

    private static CartCouponLines DefaultLines => new(
        CartPublicId,
        CartRowVersion,
        [new CouponCalculationLine(LineA, 1L, [], 1, 5000m, IsOnSale: false)],
        IsAssemblyDelivery: false);

    private static CouponRuleSnapshot ActiveCoupon(bool memberOnly = false) =>
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
                TotalUsageLimit: null,
                PerMemberLimit: 1,
                memberOnly,
                ExcludeSaleItems: false,
                CouponScopeType.All,
                CouponStatus.Active,
                RuleVersion: 1),
            CouponScopeRules.SiteWide);

    private sealed class FakeCartCouponLineReader : ICartCouponLineReader
    {
        private readonly CartCouponLines? _lines;

        public FakeCartCouponLineReader(CartCouponLines? lines) => _lines = lines;

        public Task<CartCouponLines?> FindAsync(
            CartIdentity identity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_lines);
    }

    /// <summary>只回一個固定的購物車，並記錄有沒有被寫入。</summary>
    private sealed class FakeCartService : ICartService
    {
        public int Writes { get; private set; }

        public Task<CartDto> GetCartAsync(CartIdentity identity, CancellationToken cancellationToken) =>
            Task.FromResult(new CartDto(
                CartPublicId,
                [],
                Coupon: null,
                new CartAmountsDto(5000m, 0m, 0m, 60m, 0m, 5000m, "TWD"),
                [],
                CartRowVersion));

        public Task<CartDto> AddItemAsync(
            CartIdentity identity, AddCartItemRequest request, CancellationToken cancellationToken)
        {
            Writes++;
            return GetCartAsync(identity, cancellationToken);
        }

        public Task<CartDto> UpdateItemQuantityAsync(
            CartIdentity identity,
            Guid itemPublicId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken)
        {
            Writes++;
            return GetCartAsync(identity, cancellationToken);
        }

        public Task<CartDto> RemoveItemAsync(
            CartIdentity identity,
            Guid itemPublicId,
            byte[] itemRowVersion,
            CancellationToken cancellationToken)
        {
            Writes++;
            return GetCartAsync(identity, cancellationToken);
        }

        public Task<CartDto> RemoveAssemblyGroupAsync(
            CartIdentity identity,
            Guid assemblyGroupKey,
            byte[] cartRowVersion,
            CancellationToken cancellationToken)
        {
            Writes++;
            return GetCartAsync(identity, cancellationToken);
        }

        public Task<CartDto> AddAssemblyGroupsAsync(
            CartIdentity identity,
            IReadOnlyList<AssemblyGroupItemInput> perUnitItems,
            int unitCount,
            CancellationToken cancellationToken)
        {
            Writes++;
            return GetCartAsync(identity, cancellationToken);
        }

        public Task<CartValidationDto> RevalidateAsync(
            CartIdentity identity, CancellationToken cancellationToken) =>
            throw new NotSupportedException("套券試算不會走重驗。");

        public Task<IdempotencyExecutionResult<CartMergeResultDto>> MergeAsync(
            string memberUserId,
            CartMergeRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("套券試算不會走合併。");
    }

    /// <summary>固定時鐘，與 CouponQuoteServiceTests 同一種寫法。</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class FakeCouponRuleReader : ICouponRuleReader
    {
        private readonly CouponRuleSnapshot? _snapshot;

        public FakeCouponRuleReader(CouponRuleSnapshot? snapshot) => _snapshot = snapshot;

        public string? RequestedMemberUserId { get; private set; }

        public byte[]? RequestedGuestUsageKeyHash { get; private set; }

        public Task<CouponRuleSnapshot?> FindByCodeAsync(
            string normalizedCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task<CouponUsageState> GetUsageAsync(
            long couponId,
            string? memberUserId,
            byte[]? guestUsageKeyHash,
            DateTime evaluatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            RequestedMemberUserId = memberUserId;
            RequestedGuestUsageKeyHash = guestUsageKeyHash;
            return Task.FromResult(new CouponUsageState(0, 0));
        }
    }
}
