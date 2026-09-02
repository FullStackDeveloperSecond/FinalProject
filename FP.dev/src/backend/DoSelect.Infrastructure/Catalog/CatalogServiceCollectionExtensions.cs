using DoSelect.Application.Catalog;
using DoSelect.Application.Builds;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Catalog;

public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectCatalogServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IProductSearchService, EfProductSearchService>();
        services.AddScoped<IProductDetailService, EfProductDetailService>();
        services.AddScoped<ICatalogFilterOptionsService, EfCatalogFilterOptionsService>();
        services.AddScoped<IBrandAdminService, EfBrandAdminService>();
        services.AddScoped<ICategoryAdminService, EfCategoryAdminService>();
        services.AddScoped<ISpecificationDefinitionAdminService, EfSpecificationDefinitionAdminService>();
        services.AddScoped<ITagAdminService, EfTagAdminService>();
        services.AddScoped<IProductAdminService, EfProductAdminService>();
        services.AddScoped<ISkuAdminService, EfSkuAdminService>();
        services.AddScoped<ICompatibilityCatalogReader, EfCompatibilityCatalogReader>();
        // 優惠券挑選器的唯讀目錄參考（PR #64 P2#3）。契約與實作屬 Catalog，
        // 端點掛在 /api/v1/admin/coupons 底下並以 Coupon.Manage 保護。
        services.AddScoped<ICouponCatalogOptionsReader, CouponCatalogOptionsReader>();

        return services;
    }
}
