using DoSelect.Application.Shopping;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Shopping;

public static class ShoppingServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectShoppingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICartService, EfCartService>();

        return services;
    }
}
