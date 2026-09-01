using DoSelect.Application.Promotions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Infrastructure.Promotions;

public static class PromotionsServiceCollectionExtensions
{
    /// <summary>
    /// 註冊優惠券試算與後台管理。需先呼叫 <c>AddDoSelectPersistence</c> 取得 <c>DoSelectDbContext</c>。
    /// </summary>
    public static IServiceCollection AddDoSelectPromotions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CouponGuestUsageHasher>();
        services.AddScoped<ICouponRuleReader, CouponRuleReader>();
        services.AddScoped<CouponQuoteService>();
        services.AddScoped<IAdminCouponService, EfAdminCouponService>();

        return services;
    }
}
