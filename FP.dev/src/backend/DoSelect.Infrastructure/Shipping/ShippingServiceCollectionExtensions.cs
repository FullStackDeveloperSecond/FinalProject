using DoSelect.Application.Shipping;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Shipping;

public static class ShippingServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectShippingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IShippingOptionsService, EfShippingOptionsService>();
        services.AddScoped<IConvenienceStoreQueryService, EfConvenienceStoreQueryService>();
        services.AddScoped<IPackageLimitService, EfPackageLimitService>();
        services.AddScoped<IConvenienceStoreAdminService, EfConvenienceStoreAdminService>();
        services.AddScoped<IBatchShipmentService, EfBatchShipmentService>();


        return services;
    }
}
