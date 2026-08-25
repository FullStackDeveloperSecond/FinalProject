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

        // Scoped, not Singleton——建構子驗證 Pepper 長度會丟例外，比照
        // EfIdempotencyExecutor 的取捨：讓還沒設定 Pepper 的 Fresh Clone 能正常啟動，
        // 只在第一次真的呼叫訪客查單端點時才失敗，而不是整個 API 啟動失敗。
        services.AddScoped<IGuestOrderAccessHasher, GuestOrderAccessHasher>();
        services.AddScoped<IGuestOrderAccessGateway, EfGuestOrderAccessGateway>();
        services.AddHostedService<GuestOrderAccessCleanupBackgroundService>();

        return services;
    }
}
