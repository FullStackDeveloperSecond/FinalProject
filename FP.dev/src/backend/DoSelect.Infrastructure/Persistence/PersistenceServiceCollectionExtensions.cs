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

    // Password reset needs its own named token provider because Identity's default provider
    // (used for Email confirmation) shares one TokenLifespan; 密碼重設 must expire in 1 hour
    // while Email 驗證 keeps the framework default 24-hour span (會員、驗證與通知.md).
    private const string PasswordResetTokenProviderName = "PasswordReset";

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

                options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<DoSelectDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(PasswordResetTokenProviderName)
            .AddSignInManager();

        services.Configure<DataProtectionTokenProviderOptions>(
            PasswordResetTokenProviderName,
            options => options.TokenLifespan = TimeSpan.FromHours(1));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IMemberRegistrationGateway, MemberRegistrationGateway>();
        services.AddScoped<IMemberLoginGateway, MemberLoginGateway>();
        services.AddScoped<IMemberPasswordResetGateway, MemberPasswordResetGateway>();
        services.AddScoped<MinimalDevelopmentDataSeeder>();

        return services;
    }
}
