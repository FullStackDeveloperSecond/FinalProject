using DoSelect.Application.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Ai;

public static class AiSafetyInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectAiSafetyInfrastructure(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // These registrations intentionally follow AddAiSupport. The last registration is the
        // production implementation, while API integration tests can still RemoveAll + replace it.
        services.AddScoped<IAiSupportAdmissionGate, EfAiSupportAdmissionGate>();
        services.AddScoped<IAiSupportContextReader, EfAiSupportContextReader>();
        return services;
    }
}
