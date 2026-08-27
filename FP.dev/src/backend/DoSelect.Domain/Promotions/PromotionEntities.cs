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

/// <summary>
/// 管理員一次送出的完整規則修改。欄位與 <see cref="CouponCreation"/> 相同，語意不同：
/// 建立時每個欄位都是新值；修改時每個欄位都要與既有值比對，決定是否推進
/// <see cref="Coupon.RuleVersion"/>，且已產生 <see cref="CouponRedemption"/> 之後
/// <see cref="Code"/> 不得改變。
/// </summary>
public sealed record CouponRuleRevision(
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

/// <summary>
/// 一次規則修改實際變動的欄位。<see cref="ChangedFields"/> 只帶欄位名稱、不帶值，
/// 供中央 Audit 的 changedFields 使用。
/// </summary>
public sealed record CouponRuleChange(
    IReadOnlyList<string> ChangedFields,
    bool RuleVersionAdvanced)
{
    public bool HasChanges => ChangedFields.Count > 0;
}

public sealed class Coupon : MutablePublicEntity
{
    /// <summary>
    /// 三個範圍集合在 <see cref="CouponRuleChange.ChangedFields"/> 中的欄位名稱。
    /// 分類、商品與排除商品合併為一個名稱：它們共同構成「適用範圍」這一個概念，
    /// 而且中央 Audit 的 changedFields 只需要知道範圍變了。
    /// </summary>
    public const string ScopeFieldName = "Scope";

    private Coupon() { }

    public Coupon(Guid publicId, CouponCreation creation, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(creation);
        RequireWellFormedRule(
            creation.DiscountType,
            creation.DiscountValue,
            creation.MinimumSpend,
            creation.MaximumDiscount,
            creation.StartsAtUtc,
            creation.EndsAtUtc,
            creation.TotalUsageLimit,
            creation.PerMemberLimit,
            nameof(creation));

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
    /// 套用一次完整的規則修改，回傳實際變動的欄位。
    /// </summary>
    /// <param name="revision">管理員送出的完整規則。</param>
    /// <param name="hasRedemptions">
    /// 這張券是否已經產生任何 <see cref="CouponRedemption"/>。由呼叫端在同一交易內查出；
    /// 本 Entity 不持有集合導覽屬性，無法自行判斷。
    /// </param>
    /// <param name="scopeChanged">
    /// 適用分類、適用商品或排除商品三個集合是否有任何變動（以集合語意比較，與順序無關）。
    /// 與 <paramref name="hasRedemptions"/> 同理由呼叫端算出：那三個集合在
    /// <c>CouponCategories</c>／<c>CouponProducts</c>／<c>CouponExcludedProducts</c>
    /// 三張關聯表上，本 Entity 看不到。
    /// <para>
    /// 這個參數不能省。只改範圍、<see cref="ScopeType"/> 不變時，本方法會判定
    /// 「沒有任何欄位變動」而完全不改動這個 Entity —— 於是 EF 不會對 Coupons 發出
    /// UPDATE，呼叫端設定的 <c>RowVersion</c> 原始值也就**從未被比對**，
    /// 拿過期版本做純範圍修改會直接覆蓋別人的變更。
    /// </para>
    /// </param>
    /// <param name="occurredAtUtc">修改時間。</param>
    /// <remarks>
    /// <para>
    /// `Expired` 與 `Disabled` 是終態，不接受修改。已產生 Redemption 後 <see cref="Code"/>
    /// 凍結 —— 優惠碼已經被寫進 <c>OrderCoupon</c> 快照，改掉會讓歷史訂單指向一個
    /// 再也對不上的代碼。
    /// </para>
    /// <para>
    /// 既有訂單不受本次修改影響：金額、名稱、門檻與規則版本都在下單當時抄進
    /// <c>OrderCoupon</c>（優惠券規則「訂單快照與折扣分攤」）。因此進行中的券也能改，
    /// 只是必須推進 <see cref="RuleVersion"/>，讓兩張訂單能分辨自己套用的是哪一版。
    /// </para>
    /// <para>
    /// <see cref="NameZhTw"/> 單獨改變**不**推進 <see cref="RuleVersion"/>：名稱不參與任何
    /// 計算，且已另行抄進 <c>OrderCoupon.CouponName</c>。推進版本只會讓稽核難以分辨
    /// 真正的規則異動。
    /// </para>
    /// </remarks>
    public CouponRuleChange UpdateRules(
        CouponRuleRevision revision,
        bool hasRedemptions,
        bool scopeChanged,
        DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(revision);
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (Status is CouponStatus.Expired or CouponStatus.Disabled)
        {
            throw new InvalidOperationException(
                $"A {Status} coupon can no longer be modified.");
        }

        RequireWellFormedRule(
            revision.DiscountType,
            revision.DiscountValue,
            revision.MinimumSpend,
            revision.MaximumDiscount,
            revision.StartsAtUtc,
            revision.EndsAtUtc,
            revision.TotalUsageLimit,
            revision.PerMemberLimit,
            nameof(revision));

        var code = CouponCode.Normalize(RequireText(revision.Code, nameof(revision.Code)));
        var name = RequireText(revision.NameZhTw, nameof(revision.NameZhTw));
        var startsAtUtc = RequireUtc(revision.StartsAtUtc, nameof(revision.StartsAtUtc));
        var endsAtUtc = RequireUtc(revision.EndsAtUtc, nameof(revision.EndsAtUtc));

        if (hasRedemptions && !string.Equals(code, Code, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The coupon code is frozen once a redemption exists.");
        }

        var changedFields = new List<string>();
        var ruleChanged = false;

        // 名稱與規則分開累計：名稱改變要記進 Audit，但不推進 RuleVersion。
        if (!string.Equals(name, NameZhTw, StringComparison.Ordinal))
        {
            changedFields.Add(nameof(NameZhTw));
        }

        void Rule(string field, bool changed)
        {
            if (!changed)
            {
                return;
            }

            changedFields.Add(field);
            ruleChanged = true;
        }

        Rule(nameof(Code), !string.Equals(code, Code, StringComparison.Ordinal));
        Rule(nameof(DiscountType), revision.DiscountType != DiscountType);
        Rule(nameof(DiscountValue), revision.DiscountValue != DiscountValue);
        Rule(nameof(MinimumSpend), revision.MinimumSpend != MinimumSpend);
        Rule(nameof(MaximumDiscount), revision.MaximumDiscount != MaximumDiscount);
        Rule(nameof(StartsAtUtc), startsAtUtc != StartsAtUtc);
        Rule(nameof(EndsAtUtc), endsAtUtc != EndsAtUtc);
        Rule(nameof(TotalUsageLimit), revision.TotalUsageLimit != TotalUsageLimit);
        Rule(nameof(PerMemberLimit), revision.PerMemberLimit != PerMemberLimit);
        Rule(nameof(MemberOnly), revision.MemberOnly != MemberOnly);
        Rule(nameof(ExcludeSaleItems), revision.ExcludeSaleItems != ExcludeSaleItems);
        Rule(nameof(ScopeType), revision.ScopeType != ScopeType);
        Rule(ScopeFieldName, scopeChanged);

        if (changedFields.Count == 0)
        {
            return new CouponRuleChange([], RuleVersionAdvanced: false);
        }

        Code = code;
        NameZhTw = name;
        DiscountType = revision.DiscountType;
        DiscountValue = revision.DiscountValue;
        MinimumSpend = revision.MinimumSpend;
        MaximumDiscount = revision.MaximumDiscount;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        TotalUsageLimit = revision.TotalUsageLimit;
        PerMemberLimit = revision.PerMemberLimit;
        MemberOnly = revision.MemberOnly;
        ExcludeSaleItems = revision.ExcludeSaleItems;
        ScopeType = revision.ScopeType;

        if (ruleChanged)
        {
            RuleVersion++;
        }

        MarkUpdated(occurredAtUtc);
        return new CouponRuleChange(changedFields, ruleChanged);
    }

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
    /// 建立與修改共用的規則檢查。兩條路徑必須用同一份判斷，否則會出現
    /// 「建立時擋下、修改時放行」的缺口。與資料庫的 `CK_Coupons_Period`、
    /// `CK_Coupons_UsageLimits`、`CK_Coupons_Amounts`、`CK_Coupons_Percentage` 對應。
    /// </summary>
    private static void RequireWellFormedRule(
        CouponDiscountType discountType,
        decimal? discountValue,
        decimal? minimumSpend,
        decimal? maximumDiscount,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        int? totalUsageLimit,
        int? perMemberLimit,
        string parameterName)
    {
        if (endsAtUtc <= startsAtUtc ||
            totalUsageLimit is <= 0 || perMemberLimit is <= 0 ||
            minimumSpend is < 0 || maximumDiscount is < 0 ||
            discountValue is < 0 ||
            discountType == CouponDiscountType.Percentage &&
            discountValue is not (>= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(parameterName);
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
