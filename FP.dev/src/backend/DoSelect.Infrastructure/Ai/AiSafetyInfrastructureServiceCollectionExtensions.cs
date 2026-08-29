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
        services.AddScoped<IAiConsentManager, EfAiConsentManager>();
        services.AddScoped<IAiMemberUsageReader, EfAiMemberUsageReader>();
        services.AddScoped<IAiAdminUsageReader, EfAiAdminUsageReader>();
        services.AddScoped<IAiSupportInteractionStore, EfAiSupportInteractionStore>();
        services.AddScoped<IAiProductSearchAdmissionGate, EfAiProductSearchAdmissionGate>();
        services.AddScoped<IAiProductSearchCatalog, EfAiProductSearchCatalog>();
        services.AddScoped<IAiProductSearchInteractionStore, EfAiProductSearchInteractionStore>();
        services
            .AddHttpClient<OpenAiResponsesClient>(client =>
                client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<IAiSupportModelClient>(services =>
            services.GetRequiredService<OpenAiResponsesClient>());
        services
            .AddHttpClient<OpenAiProductSearchClient>(client =>
                client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<IAiProductSearchModelClient>(services =>
            services.GetRequiredService<OpenAiProductSearchClient>());
        return services;
    }
}
