using DoSelect.Application.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Inventory;

public static class InventoryServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectInventory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IInventoryAdminQueryService, EfInventoryAdminQueryService>();
        services.AddScoped<IInventoryReservationService, EfInventoryReservationService>();
        services.AddScoped<IInventoryReconciliationService, EfInventoryReconciliationService>();

        return services;
    }
}
