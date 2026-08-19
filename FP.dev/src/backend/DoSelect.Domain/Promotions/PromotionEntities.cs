using DoSelect.Domain.Common;

namespace DoSelect.Domain.Promotions;

public sealed record CouponCreation(
    string Code,
    string NameZhTw,
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
    CouponScopeType ScopeType);

public sealed class Coupon : MutablePublicEntity
{
    private Coupon() { }

    public Coupon(Guid publicId, CouponCreation creation, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(creation);
        if (creation.EndsAtUtc <= creation.StartsAtUtc ||
            creation.TotalUsageLimit is <= 0 || creation.PerMemberLimit is <= 0 ||
            creation.MinimumSpend is < 0 || creation.MaximumDiscount is < 0 ||
            creation.DiscountValue is < 0 ||
            creation.DiscountType == CouponDiscountType.Percentage &&
            creation.DiscountValue is not (>= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(creation));
        }

        Code = CouponCode.Normalize(RequireText(creation.Code, nameof(creation.Code)));
        NameZhTw = RequireText(creation.NameZhTw, nameof(creation.NameZhTw));
        DiscountType = creation.DiscountType;
        DiscountValue = creation.DiscountValue;
        MinimumSpend = creation.MinimumSpend;
        MaximumDiscount = creation.MaximumDiscount;
        StartsAtUtc = RequireUtc(creation.StartsAtUtc, nameof(creation.StartsAtUtc));
        EndsAtUtc = RequireUtc(creation.EndsAtUtc, nameof(creation.EndsAtUtc));
        TotalUsageLimit = creation.TotalUsageLimit;
        PerMemberLimit = creation.PerMemberLimit;
        MemberOnly = creation.MemberOnly;
        ExcludeSaleItems = creation.ExcludeSaleItems;
        ScopeType = creation.ScopeType;
        Status = CouponStatus.Draft;
        RuleVersion = 1;
    }

    public string Code { get; private set; } = string.Empty;
    public string NameZhTw { get; private set; } = string.Empty;
    public CouponDiscountType DiscountType { get; private set; }
    public decimal? DiscountValue { get; private set; }
    public decimal? MinimumSpend { get; private set; }
    public decimal? MaximumDiscount { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public int? TotalUsageLimit { get; private set; }
    public int? PerMemberLimit { get; private set; }
    public bool MemberOnly { get; private set; }
    public bool ExcludeSaleItems { get; private set; }
    public CouponScopeType ScopeType { get; private set; }
    public CouponStatus Status { get; private set; }
    public int RuleVersion { get; private set; }
}

public sealed class CouponRedemption : MutablePublicEntity
{
    private CouponRedemption() { }

    public CouponRedemption(
        Guid publicId,
        long couponId,
        long orderId,
        string? memberUserId,
        byte[]? guestUsageKeyHash,
        DateTime reservedAtUtc,
        DateTime? expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (couponId <= 0 || orderId <= 0 ||
            (string.IsNullOrWhiteSpace(memberUserId) == (guestUsageKeyHash is null)) ||
            guestUsageKeyHash is not null && guestUsageKeyHash.Length != 32)
        {
            throw new ArgumentException("The coupon redemption owner or relation is invalid.");
        }

        CouponId = couponId;
        OrderId = orderId;
        MemberUserId = string.IsNullOrWhiteSpace(memberUserId) ? null : memberUserId.Trim();
        GuestUsageKeyHash = guestUsageKeyHash?.ToArray();
        Status = CouponRedemptionStatus.Reserved;
        ReservedAtUtc = RequireUtc(reservedAtUtc, nameof(reservedAtUtc));
        ExpiresAtUtc = expiresAtUtc.HasValue
            ? RequireUtc(expiresAtUtc.Value, nameof(expiresAtUtc))
            : null;
    }

    public long CouponId { get; private set; }
    public long OrderId { get; private set; }
    public string? MemberUserId { get; private set; }
    public byte[]? GuestUsageKeyHash { get; private set; }
    public CouponRedemptionStatus Status { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }

    public void Consume(DateTime occurredAtUtc) => Transition(CouponRedemptionStatus.Consumed, occurredAtUtc);
    public void Release(DateTime occurredAtUtc) => Transition(CouponRedemptionStatus.Released, occurredAtUtc);
    public void Expire(DateTime occurredAtUtc) => Transition(CouponRedemptionStatus.Expired, occurredAtUtc);

    private void Transition(CouponRedemptionStatus next, DateTime occurredAtUtc)
    {
        if (Status != CouponRedemptionStatus.Reserved)
        {
            throw new InvalidOperationException("Only a reserved coupon redemption can transition.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Status = next;
        ReleasedAtUtc = next == CouponRedemptionStatus.Released ? occurredAtUtc : null;
        ConsumedAtUtc = next == CouponRedemptionStatus.Consumed ? occurredAtUtc : null;
        MarkUpdated(occurredAtUtc);
    }
}

public sealed class OrderCoupon : PublicEntity
{
    private OrderCoupon() { }

    public OrderCoupon(
        Guid publicId,
        long orderId,
        long? couponId,
        long? redemptionId,
        string couponCodeSnapshot,
        string nameSnapshot,
        CouponDiscountType discountType,
        int ruleVersion,
        decimal? discountValue,
        decimal? minimumSpendAmount,
        decimal appliedAmount,
        decimal eligibleSubtotal,
        bool isFreeShipping,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId <= 0 || ruleVersion <= 0 || discountValue is < 0 ||
            minimumSpendAmount is < 0 ||
            appliedAmount < 0 || eligibleSubtotal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId));
        }

        OrderId = orderId;
        CouponId = couponId;
        RedemptionId = redemptionId;
        CouponCodeSnapshot = RequireText(couponCodeSnapshot, nameof(couponCodeSnapshot));
        NameSnapshot = RequireText(nameSnapshot, nameof(nameSnapshot));
        DiscountType = discountType;
        RuleVersion = ruleVersion;
        DiscountValue = discountValue;
        MinimumSpendAmount = minimumSpendAmount;
        AppliedAmount = appliedAmount;
        EligibleSubtotal = eligibleSubtotal;
        IsFreeShipping = isFreeShipping;
    }

    public long OrderId { get; private set; }
    public long? CouponId { get; private set; }
    public long? RedemptionId { get; private set; }
    public string CouponCodeSnapshot { get; private set; } = string.Empty;
    public string NameSnapshot { get; private set; } = string.Empty;
    public CouponDiscountType DiscountType { get; private set; }
    public int RuleVersion { get; private set; }
    public decimal? DiscountValue { get; private set; }
    public decimal? MinimumSpendAmount { get; private set; }
    public decimal AppliedAmount { get; private set; }
    public decimal EligibleSubtotal { get; private set; }
    public bool IsFreeShipping { get; private set; }
}

public sealed class CouponCategory
{
    private CouponCategory() { }
    public CouponCategory(long couponId, long categoryId, DateTime createdAtUtc)
    {
        if (couponId <= 0 || categoryId <= 0) throw new ArgumentOutOfRangeException(nameof(couponId));
        CouponId = couponId;
        CategoryId = categoryId;
        CreatedAtUtc = RequireUtc(createdAtUtc);
    }
    public long CouponId { get; private set; }
    public long CategoryId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("The value must use UTC.");
}

public sealed class CouponProduct
{
    private CouponProduct() { }
    public CouponProduct(long couponId, long productId, DateTime createdAtUtc)
    {
        if (couponId <= 0 || productId <= 0) throw new ArgumentOutOfRangeException(nameof(couponId));
        CouponId = couponId;
        ProductId = productId;
        CreatedAtUtc = RequireUtc(createdAtUtc);
    }
    public long CouponId { get; private set; }
    public long ProductId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("The value must use UTC.");
}

public sealed class CouponExcludedProduct
{
    private CouponExcludedProduct() { }
    public CouponExcludedProduct(long couponId, long productId, DateTime createdAtUtc)
    {
        if (couponId <= 0 || productId <= 0) throw new ArgumentOutOfRangeException(nameof(couponId));
        CouponId = couponId;
        ProductId = productId;
        CreatedAtUtc = RequireUtc(createdAtUtc);
    }
    public long CouponId { get; private set; }
    public long ProductId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("The value must use UTC.");
}
