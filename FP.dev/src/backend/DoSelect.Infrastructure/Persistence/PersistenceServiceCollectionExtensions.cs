using DoSelect.Application.Members;
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

        services.AddDataProtection();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // RegisterRequest.password contract bounds length to 12..128; no composition
                // rule is documented, so composition requirements are disabled rather than
                // guessed. See FP.sheet/.../API DTO與Schema契約.md.
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireDigit = false;

                // 會員連續登入失敗 5 次鎖定 15 分鐘 (會員、驗證與通知.md). Admin's differentiated
                // 30-minute window is out of scope until admin login (M-01B) is implemented — a
                // single global window cannot express both, so this is member-only for now.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<DoSelectDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IMemberRegistrationGateway, MemberRegistrationGateway>();
        services.AddScoped<IMemberLoginGateway, MemberLoginGateway>();
        services.AddScoped<MinimalDevelopmentDataSeeder>();

        return services;
    }
}
