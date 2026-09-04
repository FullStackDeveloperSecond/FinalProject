using DoSelect.Application.Orders;
using DoSelect.Application.Refunds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Infrastructure.Orders;

public static class OrdersServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectOrderServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IOrderService, EfOrderService>();
        services.AddScoped<IOrderTimeoutCancellationService, EfOrderTimeoutCancellationService>();
        services.TryAddScoped<IRefundOrderProjectionPort, EfRefundOrderProjectionPort>();

        return services;
    }
}
