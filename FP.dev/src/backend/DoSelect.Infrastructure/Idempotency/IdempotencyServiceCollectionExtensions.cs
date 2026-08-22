using DoSelect.Application.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Infrastructure.Idempotency;

public static class IdempotencyServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectIdempotency(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<IdempotencyOptions>(
            configuration.GetSection(IdempotencyOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IIdempotencyExecutor, EfIdempotencyExecutor>();
        return services;
    }
}
