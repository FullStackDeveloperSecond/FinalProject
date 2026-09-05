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
        // 冪等協調器與服務同一個 scope，才會共用同一個 DbContext 與連線。
        services.AddScoped<BatchShipmentIdempotency>();
        services.AddScoped<IBatchShipmentService, EfBatchShipmentService>();
        services.AddScoped<IShipmentStatusService, EfShipmentStatusService>();


        return services;
    }
}
