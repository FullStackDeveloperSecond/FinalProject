using DoSelect.Application.Security;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Security;

public static class AdminAuthServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectAdminAuth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AddDefaultTokenProviders()（TOTP／Recovery Code 驗證所需）現在已由
        // PersistenceServiceCollectionExtensions.AddDoSelectPersistence 註冊（PR #27 後併入），
        // 這裡不必再補一個 IdentityBuilder 重複呼叫。

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAdminAuthGateway, IdentityAdminAuthGateway>();
        services.AddSingleton<ITotpQrCodeGenerator, QrCodeGenerator>();
        services.AddSingleton<IAdminSecurityAuditWriter, LoggingAdminSecurityAuditWriter>();
        services.AddSingleton<IAdminChallengeRateLimiter, AdminChallengeRateLimiter>();
        services.AddScoped<AdminLoginUseCase>();
        services.AddScoped<AdminTwoFactorUseCase>();

        return services;
    }
}
