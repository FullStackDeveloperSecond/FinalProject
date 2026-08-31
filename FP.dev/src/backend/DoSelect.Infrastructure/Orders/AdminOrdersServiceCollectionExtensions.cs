using DoSelect.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Orders;

public static class AdminOrdersServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectAdminOrderServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAdminOrderService, EfAdminOrderService>();

        return services;
    }
}
