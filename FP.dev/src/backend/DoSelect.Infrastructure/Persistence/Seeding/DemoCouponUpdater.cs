using System.Net;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Members;
using DoSelect.Domain.Promotions;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Seeding;

/// <summary>明確執行的本機 Demo 維護，不在啟動或一般 seed 時自動修改既有活動。</summary>
public sealed class DemoCouponUpdater(DoSelectDbContext context, IAdminCouponService coupons)
{
    public const string SchoolCode = "SCHOOL2026";
    public const string WelcomeCode = "MEMBER100";
    private static readonly DateTime SeptemberStart = new(2026, 8, 31, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeptemberEnd = new(2026, 9, 30, 16, 0, 0, DateTimeKind.Utc);

    public async Task UpdateAsync(bool disableLegacyCoupons, CancellationToken cancellationToken = default)
    {
        DemoDatabaseSafety.EnsureAllowedIsolatedLocalDatabase(context);
        if (!await context.Brands.AnyAsync(item => item.Code == DemoSeedManifest.MarkerBrandCode, cancellationToken))
            throw new InvalidOperationException("Only the existing v3 isolated Demo seed can be updated.");
        if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new InvalidOperationException("Apply the reviewed migration before updating Demo coupons.");

        var admin = await context.Users.AsNoTracking().SingleAsync(user =>
            user.Id == "demo-admin-0001" && user.Email == DemoAccountActivator.AdminEmail &&
            user.AccountType == AccountType.Admin && user.AccountStatus == AccountStatus.Active, cancellationToken);
        var actor = new AdminCouponActorContext(admin.Id, "demo-coupon-update", Guid.NewGuid().ToString("N"), IPAddress.Loopback);

        var creator = await FindAsync("CREATOR10", cancellationToken)
            ?? throw new InvalidOperationException("Expected CREATOR10 is missing. No replacement record will be fabricated.");
        if (creator.Status is not (CouponStatus.Disabled or CouponStatus.Expired))
        {
            creator = await coupons.UpdateAsync(creator.PublicId, new UpdateCouponRequest(
                creator.Code, "創作者指定分類九折（8月已結束）", creator.DiscountType,
                creator.DiscountValue, creator.MinimumSpend, creator.MaximumDiscount,
                new DateTime(2026, 7, 31, 16, 0, 0, DateTimeKind.Utc), SeptemberStart,
                creator.Usage.TotalUsageLimit, creator.Usage.PerMemberLimit, creator.MemberOnly,
                creator.ExcludeSaleItems, creator.Scope.ScopeType, creator.Scope.CategoryPublicIds,
                creator.Scope.ProductPublicIds, creator.Scope.ExcludedProductPublicIds, creator.RowVersion), actor, cancellationToken);
            await DisableAsync(creator, actor, cancellationToken);
        }
        else if (creator.EndsAtUtc != SeptemberStart)
        {
            throw new InvalidOperationException("CREATOR10 is already terminal with a different period; manual review is required.");
        }

        if (disableLegacyCoupons)
        {
            for (var index = 1; index <= 29; index++)
            {
                var legacy = await FindAsync($"DEMO{index:D3}", cancellationToken);
                if (legacy is not null && legacy.Status is not (CouponStatus.Disabled or CouponStatus.Expired))
                    await DisableAsync(legacy, actor, cancellationToken);
            }
        }

        await CreateOrVerifyAsync(new CreateCouponRequest(SchoolCode, "9月開學季優惠", CouponDiscountType.Percentage,
            .05m, null, null, SeptemberStart, SeptemberEnd, null, 1, false, false,
            CouponScopeType.All, [], [], [], MultiItemDiscountValue: .10m), actor, cancellationToken);
        // 沿用既有活動起訖契約，以可表示的遠端上界表示持續入會禮；真正個人期限為原始入會日起 12 個月。
        // 不將此日期顯示為會員券的個人到期日，也不修改會員 CreatedAtUtc。
        await CreateOrVerifyAsync(new CreateCouponRequest(WelcomeCode, "會員入會禮 $100", CouponDiscountType.FixedAmount,
            100m, 1000m, null, SeptemberStart, new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null, 1, true, false, CouponScopeType.All, [], [], [], MemberValidityMonths: 12), actor, cancellationToken);
    }

    private async Task<CouponDto?> FindAsync(string code, CancellationToken cancellationToken)
    {
        var id = await context.Coupons.AsNoTracking().Where(item => item.Code == code)
            .Select(item => (Guid?)item.PublicId).SingleOrDefaultAsync(cancellationToken);
        return id is { } publicId ? await coupons.FindByPublicIdAsync(publicId, cancellationToken) : null;
    }

    private Task<CouponDto> DisableAsync(CouponDto coupon, AdminCouponActorContext actor, CancellationToken cancellationToken) =>
        coupons.ExecuteActionAsync(coupon.PublicId, AdminCouponActions.Disable,
            new CouponActionRequest("demo_campaign_retired", "Local Demo campaign maintenance; history retained.", coupon.RowVersion),
            actor, cancellationToken);

    private async Task CreateOrVerifyAsync(CreateCouponRequest rule, AdminCouponActorContext actor, CancellationToken cancellationToken)
    {
        var coupon = await FindAsync(rule.Code, cancellationToken);
        if (coupon is null)
            coupon = await coupons.CreateAsync(rule, actor, cancellationToken);
        else if (coupon.NameZhTw != rule.NameZhTw || coupon.DiscountType != rule.DiscountType ||
            coupon.DiscountValue != rule.DiscountValue || coupon.MinimumSpend != rule.MinimumSpend ||
            coupon.MaximumDiscount != rule.MaximumDiscount || coupon.MultiItemDiscountValue != rule.MultiItemDiscountValue ||
            coupon.MemberValidityMonths != rule.MemberValidityMonths || coupon.StartsAtUtc != rule.StartsAtUtc || coupon.EndsAtUtc != rule.EndsAtUtc ||
            coupon.Usage.TotalUsageLimit != rule.TotalUsageLimit || coupon.Usage.PerMemberLimit != rule.PerMemberLimit ||
            coupon.MemberOnly != rule.MemberOnly || coupon.ExcludeSaleItems != rule.ExcludeSaleItems ||
            coupon.Scope.ScopeType != CouponScopeType.All || coupon.Scope.CategoryPublicIds.Count != 0 ||
            coupon.Scope.ProductPublicIds.Count != 0 || coupon.Scope.ExcludedProductPublicIds.Count != 0)
            throw new InvalidOperationException($"{rule.Code} already exists with different rules; no overwrite was performed.");

        if (coupon.Status == CouponStatus.Draft)
            await coupons.ExecuteActionAsync(coupon.PublicId, AdminCouponActions.Activate,
                new CouponActionRequest("demo_campaign_start", "Local Demo campaign maintenance.", coupon.RowVersion), actor, cancellationToken);
        else if (coupon.Status is not (CouponStatus.Active or CouponStatus.Scheduled))
            throw new InvalidOperationException($"{rule.Code} is not active or scheduled; its lifecycle will not be overridden.");
    }
}
