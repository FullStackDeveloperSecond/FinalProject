namespace DoSelect.Domain.Promotions;

public enum CouponDiscountType
{
    FixedAmount,
    Percentage,
    FreeShipping,
    AssemblyFreeShipping,
}

public enum CouponScopeType
{
    All,
    Restricted,
}

public enum CouponStatus
{
    Draft,
    Scheduled,
    Active,
    Paused,
    Expired,
    Exhausted,
    Disabled,
}

public enum CouponRedemptionStatus
{
    Reserved,
    Released,
    Consumed,
    Expired,
}
