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
    /// 佔用使用名額的 Redemption：<c>Consumed</c>，加上**尚未過期**的 <c>Reserved</c>。
    /// </summary>
    /// <remarks>
    /// 正式 Schema 的名額計算是「Consumed + 尚未過期的 Reserved」。
    /// 只看狀態不看 <c>ExpiresAtUtc</c> 會有實際後果：保留已逾時、但背景工作還沒把它
    /// 轉成 <c>Expired</c> 的那段期間，該筆仍會被算進總量與每人限額，
    /// 讓優惠券提早額滿並持續擋住其他人。
    /// <c>Released</c> 與 <c>Expired</c> 依取消與返券規則歸還名額，不計入。
    /// </remarks>
    public static Expression<Func<CouponRedemption, bool>> OccupiesUsageSeatAt(
        DateTime evaluatedAtUtc) =>
        redemption =>
            redemption.Status == CouponRedemptionStatus.Consumed ||
            (redemption.Status == CouponRedemptionStatus.Reserved &&
                (redemption.ExpiresAtUtc == null || redemption.ExpiresAtUtc > evaluatedAtUtc));

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
                excludedProductIds),
            coupon.NameZhTw);
    }

    public async Task<CouponUsageState> GetUsageAsync(
        long couponId,
        string? memberUserId,
        byte[]? guestUsageKeyHash,
        DateTime evaluatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (evaluatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(evaluatedAtUtc));
        }

        // 總量與每人限額共用同一個過期基準，兩者不會對同一筆 Reserved 有不同判定。
        var occupied = _context.CouponRedemptions
            .AsNoTracking()
            .Where(redemption => redemption.CouponId == couponId)
            .Where(OccupiesUsageSeatAt(evaluatedAtUtc));

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

        // 兩者都沒有時 ownerRedeemedCount 維持 0，每人限額因此不會觸發。
        // 這是刻意的：試算只是預覽，名額的權威判定在 Checkout 建單的同一交易內
        // （最終 Schema「CouponRedemptions 只在 Checkout 建立 Order 的同一 SQL Transaction 建立」）。
        return new CouponUsageState(totalRedeemedCount, ownerRedeemedCount);
    }
}
