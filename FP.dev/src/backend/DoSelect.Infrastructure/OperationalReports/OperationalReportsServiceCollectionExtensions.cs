using DoSelect.Application.OperationalReports;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.OperationalReports;

public static class OperationalReportsServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectOperationalReports(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IOperationalReportQueryService, EfOperationalReportQueryService>();
        services.AddScoped<IOperationalReportCsvExporter, OperationalReportCsvExporter>();
        services.AddScoped<IOperationalReportXlsxExporter, OperationalReportXlsxExporter>();
        return services;
    }
}
