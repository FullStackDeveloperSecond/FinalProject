using DoSelect.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        return services;
    }
}
