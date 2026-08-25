using DoSelect.Application.Auditing;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Auditing;

public static class AuditingServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectAuditing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        return services;
    }
}
