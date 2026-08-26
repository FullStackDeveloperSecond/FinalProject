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

    /// <summary>
    /// 優惠券生命週期的正式轉移表（DEC-BATCH-014 第 2 項）。
    /// `Expired` 與 `Disabled` 為終態。Entity 是狀態的唯一真實來源；
    /// <see cref="CouponRule"/> 只承載查詢當下的快照，不得用來改狀態。
    /// </summary>
    private static readonly IReadOnlyDictionary<CouponStatus, CouponStatus[]> AllowedTransitions =
        new Dictionary<CouponStatus, CouponStatus[]>
        {
            [CouponStatus.Draft] = [CouponStatus.Scheduled, CouponStatus.Active, CouponStatus.Disabled],
            [CouponStatus.Scheduled] = [CouponStatus.Active, CouponStatus.Expired, CouponStatus.Disabled],
            [CouponStatus.Active] = [CouponStatus.Paused, CouponStatus.Exhausted, CouponStatus.Expired, CouponStatus.Disabled],
            [CouponStatus.Paused] = [CouponStatus.Active, CouponStatus.Expired, CouponStatus.Disabled],
            [CouponStatus.Exhausted] = [CouponStatus.Active, CouponStatus.Expired, CouponStatus.Disabled],
            [CouponStatus.Expired] = [],
            [CouponStatus.Disabled] = [],
        };

    /// <summary>
    /// 折扣規則本身是否完整。百分比券必須同時有折扣率與最高折抵；定額券必須有折扣金額。
    /// 適用範圍是否完整由 <see cref="CouponCalculator"/> 判定並回 `coupon_invalid`，
    /// 因為範圍資料不在本 Entity 上。
    /// </summary>
    public bool HasCompleteDiscountRule => DiscountType switch
    {
        CouponDiscountType.FixedAmount => DiscountValue is > 0,
        CouponDiscountType.Percentage => DiscountValue is > 0 and <= 1 && MaximumDiscount is > 0,
        CouponDiscountType.FreeShipping or CouponDiscountType.AssemblyFreeShipping => true,
        _ => false,
    };

    /// <summary>指定時點是否落在有效期間內。</summary>
    public bool IsWithinUsagePeriod(DateTime occurredAtUtc) =>
        occurredAtUtc >= StartsAtUtc && occurredAtUtc < EndsAtUtc;

    /// <summary>總名額是否仍有剩餘。</summary>
    public bool HasRemainingQuota(CouponUsageState usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return TotalUsageLimit is not { } limit || usage.TotalRedeemedCount < limit;
    }

    // ── 管理員操作 ──────────────────────────────────────────────

    /// <summary>
    /// 排定未來生效。要求開始時間晚於目前時間，且折扣規則完整。
    /// </summary>
    public void ScheduleForLaterStart(DateTime occurredAtUtc)
    {
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (StartsAtUtc <= occurredAtUtc || !HasCompleteDiscountRule)
        {
            throw new InvalidOperationException(
                "A coupon can only be scheduled before its start time and with a complete rule.");
        }

        Transition(CouponStatus.Scheduled, occurredAtUtc);
    }

    /// <summary>
    /// 管理員立即啟用。只接受 `Draft` 或 `Paused`，並要求已進入有效期間、
    /// 折扣規則完整，且總名額仍有剩餘。
    /// </summary>
    public void ActivateNow(CouponUsageState usage, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(usage);
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (Status is not (CouponStatus.Draft or CouponStatus.Paused))
        {
            throw new InvalidOperationException("Only a draft or paused coupon can be activated by an administrator.");
        }

        EnsureCanBecomeActive(usage, occurredAtUtc);
        Transition(CouponStatus.Active, occurredAtUtc);
    }

    /// <summary>
    /// 排程到達開始時間。只接受 `Scheduled`，且當下仍須重新驗證期間、規則與名額。
    /// </summary>
    public void ActivateScheduled(CouponUsageState usage, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(usage);
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (Status != CouponStatus.Scheduled)
        {
            throw new InvalidOperationException("Only a scheduled coupon can be activated by the scheduler.");
        }

        EnsureCanBecomeActive(usage, occurredAtUtc);
        Transition(CouponStatus.Active, occurredAtUtc);
    }

    /// <summary>
    /// 名額返還後恢復使用。只接受 `Exhausted`，且使用量必須重新低於既有總量上限。
    /// </summary>
    public void ReactivateAfterQuotaRelease(CouponUsageState usage, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(usage);
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (Status != CouponStatus.Exhausted ||
            TotalUsageLimit is not { } limit ||
            usage.TotalRedeemedCount >= limit)
        {
            throw new InvalidOperationException(
                "Only an exhausted coupon with returned quota can become active again.");
        }

        EnsureCanBecomeActive(usage, occurredAtUtc);
        Transition(CouponStatus.Active, occurredAtUtc);
    }

    /// <summary>暫時停止使用，不改變有效期間。只有 `Active` 能暫停。</summary>
    public void Pause(DateTime occurredAtUtc) => Transition(CouponStatus.Paused, occurredAtUtc);

    /// <summary>永久停用。終態，不可重新啟用。</summary>
    public void Disable(DateTime occurredAtUtc) => Transition(CouponStatus.Disabled, occurredAtUtc);

    // ── 排程與名額事件 ──────────────────────────────────────────

    /// <summary>
    /// 名額耗盡。要求有設定總名額且使用量已達上限，避免無上限的券被標成耗盡。
    /// </summary>
    public void MarkExhausted(CouponUsageState usage, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(usage);
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (TotalUsageLimit is not { } limit || usage.TotalRedeemedCount < limit)
        {
            throw new InvalidOperationException(
                "A coupon is only exhausted once its total usage limit is reached.");
        }

        Transition(CouponStatus.Exhausted, occurredAtUtc);
    }

    /// <summary>
    /// 到期。要求已到達結束時間。終態，返還名額不會恢復可用。背景工作可冪等呼叫。
    /// </summary>
    public void MarkExpired(DateTime occurredAtUtc)
    {
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (occurredAtUtc < EndsAtUtc)
        {
            throw new InvalidOperationException(
                "A coupon can only expire once its end time has passed.");
        }

        if (Status == CouponStatus.Expired)
        {
            return;
        }

        Transition(CouponStatus.Expired, occurredAtUtc);
    }

    private void EnsureCanBecomeActive(CouponUsageState usage, DateTime occurredAtUtc)
    {
        if (!IsWithinUsagePeriod(occurredAtUtc) ||
            !HasCompleteDiscountRule ||
            !HasRemainingQuota(usage))
        {
            throw new InvalidOperationException(
                "A coupon can only be activated inside its period, with a complete rule and remaining quota.");
        }
    }

    /// <summary>
    /// 非法轉移丟 <see cref="InvalidOperationException"/>，由 API 層映射為
    /// <see cref="CouponCalculationErrorCodes.CouponStateConflict"/>。
    /// </summary>
    private void Transition(CouponStatus next, DateTime occurredAtUtc)
    {
        if (!AllowedTransitions[Status].Contains(next))
        {
            throw new InvalidOperationException($"Coupon cannot move from {Status} to {next}.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Status = next;
        MarkUpdated(occurredAtUtc);
    }
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
