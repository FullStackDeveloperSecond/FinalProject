using DoSelect.Application.Checkout;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Checkout;

public static class CheckoutServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectCheckout(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICheckoutPolicyProvider, CheckoutPolicyProvider>();
        services.AddScoped<IOrderNumberGenerator, SqlOrderNumberGenerator>();
        services.AddScoped<ICheckoutTransactionGateway, EfCheckoutTransactionGateway>();
        services.AddScoped<CheckoutService>();
        return services;
    }
}
