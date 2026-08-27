using DoSelect.Application.Returns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Infrastructure.Persistence.Returns;

public static class ReturnsInfrastructureServiceCollectionExtensions
{
    /// <summary>Requires AddDoSelectPersistence (DoSelectDbContext) and AddDoSelectFileStorage
    /// (IPrivateFileStorage) to have run first.</summary>
    public static IServiceCollection AddDoSelectReturnsServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IReturnStore, ReturnStore>();
        services.AddScoped<IReturnOrderEligibilityPort, ReturnOrderEligibilityLookup>();
        services.AddScoped<IGuestOrderAccessValidator, GuestOrderAccessValidator>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IAdminReturnService, AdminReturnService>();
        services.AddScoped<ICancelOverdueReturnShipmentsUseCase, CancelOverdueReturnShipmentsUseCase>();

        return services;
    }
}
