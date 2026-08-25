using DoSelect.Application.Imports;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Imports;

public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IProductImportService, EfProductImportService>();

        return services;
    }
}
