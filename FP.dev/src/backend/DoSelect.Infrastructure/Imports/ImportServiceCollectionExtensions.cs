using DoSelect.Application.Imports;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Imports;

public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IProductImportService, EfProductImportService>();
        services.AddScoped<IInventoryImportService, EfInventoryImportService>();
        // 模板只是把固定的標題列組成檔案，沒有任何請求範圍的狀態——註冊成 Singleton。
        services.AddSingleton<IImportTemplateService, ImportTemplateService>();

        return services;
    }
}
