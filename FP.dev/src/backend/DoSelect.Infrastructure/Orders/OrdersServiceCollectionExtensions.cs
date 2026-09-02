using DoSelect.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Orders;

public static class OrdersServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectOrderServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IOrderService, EfOrderService>();
        services.AddScoped<IOrderTimeoutCancellationService, EfOrderTimeoutCancellationService>();

        return services;
    }
}
