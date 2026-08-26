using DoSelect.Application.Orders;
using DoSelect.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DoSelect.Infrastructure.Persistence.Orders;

public static class GuestOrderAccessServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectGuestOrderAccess(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<GuestOrderAccessOptions>(
            configuration.GetSection(GuestOrderAccessOptions.SectionName));

        // Scoped，不是 Singleton——跟其餘走 Scoped DbContext 的埠一致。Pepper 長度不足時
        // 應用程式現在會在啟動當下就 fail fast（見 ConfigurationValidationExtensions 的
        // GuestOrderAccessOptionsValidator／ValidateOnStart），不再是只在第一次真的呼叫
        // 訪客查單端點時才失敗。
        services.AddScoped<IGuestOrderAccessHasher, GuestOrderAccessHasher>();
        services.AddScoped<IGuestOrderAccessGateway, EfGuestOrderAccessGateway>();
        services.AddHostedService<GuestOrderAccessCleanupBackgroundService>();

        return services;
    }
}
