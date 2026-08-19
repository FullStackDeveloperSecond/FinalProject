using DoSelect.Application.Catalog;
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

        return services;
    }
}
