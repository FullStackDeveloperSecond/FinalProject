using DoSelect.Application.Security;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Security;

public static class AdminAuthServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectAdminAuth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AddDefaultTokenProviders()（TOTP／Recovery Code 驗證所需）只在 ASP.NET Core 共用框架
        // 組件內，刻意不放在 AddDoSelectPersistence（純單元測試專案也會呼叫那個方法）。
        // 這裡用同一個 IServiceCollection 重新包一個 IdentityBuilder 補註冊，
        // 不會重覆 AddIdentityCore／AddEntityFrameworkStores。
        new IdentityBuilder(typeof(ApplicationUser), typeof(IdentityRole), services)
            .AddDefaultTokenProviders();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAdminAuthGateway, IdentityAdminAuthGateway>();
        services.AddSingleton<ITotpQrCodeGenerator, QrCodeGenerator>();
        services.AddScoped<AdminLoginUseCase>();
        services.AddScoped<AdminTwoFactorUseCase>();

        return services;
    }
}
