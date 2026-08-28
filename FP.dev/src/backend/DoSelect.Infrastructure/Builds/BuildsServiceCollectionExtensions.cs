using DoSelect.Application.Builds;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Builds;

public static class BuildsServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectBuildsServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<EfCompatibilityCheckService>();
        services.AddScoped<ICompatibilityCheckService>(provider =>
            provider.GetRequiredService<EfCompatibilityCheckService>());
        services.AddScoped<IBuildListService, EfBuildListService>();
        services.AddScoped<ICompatibilityRuleAdminService, EfCompatibilityRuleAdminService>();

        return services;
    }
}
