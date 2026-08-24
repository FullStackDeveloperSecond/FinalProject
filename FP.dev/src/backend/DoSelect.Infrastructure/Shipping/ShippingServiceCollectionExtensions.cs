using DoSelect.Application.Shipping;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Shipping;

public static class ShippingServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectShipping(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IShippingOptionsQueryService, EfShippingOptionsQueryService>();
        services.AddScoped<IConvenienceStoreAdminService, EfConvenienceStoreAdminService>();
        services.AddScoped<IPackageLimitVersionAdminService, EfPackageLimitVersionAdminService>();
        services.AddScoped<IBatchShipmentService, EfBatchShipmentService>();

        return services;
    }
}
