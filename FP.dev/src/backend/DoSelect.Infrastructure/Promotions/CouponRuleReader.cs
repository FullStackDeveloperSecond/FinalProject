using System.Linq.Expressions;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Promotions;

/// <summary>
/// 以 <see cref="DoSelectDbContext"/> 讀出優惠券規則、適用範圍與已完成使用量。
/// 只做讀取，不建立 <see cref="CouponRedemption"/>，也不保留使用名額；
/// 名額的原子保留屬於 Checkout 建單交易。
/// </summary>
public sealed class CouponRuleReader : ICouponRuleReader
{
    private readonly DoSelectDbContext _context;

    public CouponRuleReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <summary>
    /// 佔用使用名額的 Redemption 狀態。`Reserved` 已在結帳交易中占位，`Consumed` 已完成；
    /// `Released` 與 `Expired` 依取消與返券規則歸還名額，因此不計入。
    /// </summary>
    public static Expression<Func<CouponRedemption, bool>> OccupiesUsageSeat { get; } =
        redemption =>
            redemption.Status == CouponRedemptionStatus.Reserved ||
            redemption.Status == CouponRedemptionStatus.Consumed;

    public async Task<CouponRuleSnapshot?> FindByCodeAsync(
        string normalizedCode,
        CancellationToken cancellationToken = default)
    {
        if (!CouponCode.TryNormalize(normalizedCode, out var code))
        {
            return null;
        }

        var coupon = await _context.Coupons
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);

        if (coupon is null)
        {
            return null;
        }

        // 排除商品在全站券也可以設定，因此三張關聯表一律讀取。
        var includedCategoryIds = await _context.CouponCategories
            .AsNoTracking()
            .Where(link => link.CouponId == coupon.Id)
            .Select(link => link.CategoryId)
            .ToArrayAsync(cancellationToken);

        var includedProductIds = await _context.CouponProducts
            .AsNoTracking()
            .Where(link => link.CouponId == coupon.Id)
            .Select(link => link.ProductId)
            .ToArrayAsync(cancellationToken);

        var excludedProductIds = await _context.CouponExcludedProducts
            .AsNoTracking()
            .Where(link => link.CouponId == coupon.Id)
            .Select(link => link.ProductId)
            .ToArrayAsync(cancellationToken);

        return new CouponRuleSnapshot(
            coupon.Id,
            CouponRule.From(coupon),
            new CouponScopeRules(
                coupon.ScopeType,
                includedCategoryIds,
                includedProductIds,
                excludedProductIds));
    }

    public async Task<CouponUsageState> GetUsageAsync(
        long couponId,
        string? memberUserId,
        byte[]? guestUsageKeyHash,
        CancellationToken cancellationToken = default)
    {
        var occupied = _context.CouponRedemptions
            .AsNoTracking()
            .Where(redemption => redemption.CouponId == couponId)
            .Where(OccupiesUsageSeat);

        var totalRedeemedCount = await occupied.CountAsync(cancellationToken);

        var ownerRedeemedCount = 0;
        if (!string.IsNullOrWhiteSpace(memberUserId))
        {
            var owner = memberUserId.Trim();
            ownerRedeemedCount = await occupied
                .CountAsync(redemption => redemption.MemberUserId == owner, cancellationToken);
        }
        else if (guestUsageKeyHash is not null)
        {
            ownerRedeemedCount = await occupied
                .CountAsync(
                    redemption => redemption.GuestUsageKeyHash == guestUsageKeyHash,
                    cancellationToken);
        }

        return new CouponUsageState(totalRedeemedCount, ownerRedeemedCount);
    }
}
