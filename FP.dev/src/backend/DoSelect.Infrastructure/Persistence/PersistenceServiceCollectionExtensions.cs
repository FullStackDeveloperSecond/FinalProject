using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddDoSelectPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configuration key 'ConnectionStrings:DefaultConnection' is required.");
        }

        services.AddDbContext<DoSelectDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(DoSelectDbContext).Assembly.FullName)));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // 失敗次數門檻共用；鎖定時長依 AccountType（Member 15 分鐘／Admin 30 分鐘）
                // 由各自 UseCase 手動控制，因為 IdentityOptions.Lockout 只有單一全域時長。
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<DoSelectDbContext>();
        // AddDefaultTokenProviders() 需要 ASP.NET Core 共用框架組件（不在 Identity.Core NuGet
        // 套件內），刻意不放在這裡——這個方法被純單元測試專案（無 Web 框架）直接呼叫。
        // 實際註冊在 DoSelect.Infrastructure.Security.AdminAuthServiceCollectionExtensions，
        // 只在 DoSelect.Api（Web SDK，執行期一定有框架組件）啟動時呼叫。

        services.AddScoped<MinimalDevelopmentDataSeeder>();

        return services;
    }
}
