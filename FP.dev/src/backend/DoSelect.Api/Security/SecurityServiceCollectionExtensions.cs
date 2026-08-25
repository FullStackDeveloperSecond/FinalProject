using DoSelect.Api.Configuration;
using DoSelect.Application.Common;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace DoSelect.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    public const string FrontendCorsPolicy = "DoSelect.Frontends";
    private const string MemberAbsoluteExpiryProperty = "doselect:member_absolute_expires_utc";
    private static readonly TimeSpan MemberIdleTimeout = TimeSpan.FromHours(8);
    private static readonly TimeSpan MemberAbsoluteLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan AdminAbsoluteLifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// GuestOrderAccess Cookie 效期，DEC-P264「30 分鐘內可多次使用」。從核發起算的
    /// 固定視窗（<c>SlidingExpiration = false</c>），不因操作而展延——待覆核：
    /// 決議文字沒有明講是否要 Sliding，這裡採比較保守（較短有效期）的解讀。
    /// </summary>
    private static readonly TimeSpan GuestOrderAccessLifetime = TimeSpan.FromMinutes(30);

    public static IServiceCollection AddDoSelectSecurity(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddAuthentication()
            .AddCookie(DoSelectAuthenticationSchemes.Member, options =>
                ConfigureMemberCookie(options, environment))
            .AddCookie(DoSelectAuthenticationSchemes.Admin, options =>
                ConfigureAdminCookie(options, environment))
            .AddCookie(DoSelectAuthenticationSchemes.GuestOrderAccess, options =>
                ConfigureGuestOrderAccessCookie(options, environment));

        services.AddAuthorization(options => ConfigurePolicies(options));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = ".DoSelect.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        services.AddCors(options =>
        {
            var allowedOrigins = configuration
                .GetSection($"{CorsOptions.SectionName}:AllowedOrigins")
                .Get<string[]>() ?? [];
            allowedOrigins = allowedOrigins
                .Select(NormalizeOrigin)
                .ToArray();
            options.AddPolicy(FrontendCorsPolicy, policy =>
            {
                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // V1 展示版限流門檻，經 Alex 裁定定版（2026-08-24 review，方案 A1）— see RateLimitOptions.
        var rateLimits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
            ?? new RateLimitOptions();
        var perIpWindow = TimeSpan.FromHours(rateLimits.PerIpWindowHours);
        var loginPerIpWindow = TimeSpan.FromHours(rateLimits.LoginPerIpWindowHours);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddPerIpFixedWindowPolicy(
                options, RateLimitPolicies.AuthRegister, rateLimits.PerIpPermitLimit, perIpWindow);
            AddPerIpFixedWindowPolicy(
                options, RateLimitPolicies.AuthResendVerification, rateLimits.PerIpPermitLimit, perIpWindow);
            AddPerIpFixedWindowPolicy(
                options, RateLimitPolicies.AuthForgotPassword, rateLimits.PerIpPermitLimit, perIpWindow);
            // Login is legitimately called far more often than the endpoints above (a real user
            // can easily mistype a password a few times), so it gets its own, higher budget —
            // high enough to not annoy a genuine user, low enough to make a password-spray sweep
            // across many accounts from one source impractical. Identity's per-account Lockout
            // (MemberLoginGateway) is the other half of this defense; this limiter is the per-IP
            // half.
            AddPerIpFixedWindowPolicy(
                options, RateLimitPolicies.AuthLogin, rateLimits.LoginPerIpPermitLimit, loginPerIpWindow);
        });

        return services;
    }

    private static void AddPerIpFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        int permitLimit,
        TimeSpan window) =>
        options.AddPolicy(policyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIpAddress(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    private static string GetClientIpAddress(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string NormalizeOrigin(string rawOrigin) =>
        Uri.TryCreate(rawOrigin, UriKind.Absolute, out var origin)
            ? origin.GetLeftPart(UriPartial.Authority)
            : rawOrigin;

    private static void ConfigureMemberCookie(
        CookieAuthenticationOptions options,
        IHostEnvironment environment)
    {
        ConfigureCookieDefaults(options, environment, ".DoSelect.Member");
        options.ExpireTimeSpan = MemberIdleTimeout;
        options.SlidingExpiration = true;
        options.Events.OnSigningIn = context =>
        {
            var timeProvider = context.Options.TimeProvider ?? TimeProvider.System;
            var absoluteExpiry = timeProvider.GetUtcNow().Add(MemberAbsoluteLifetime);
            context.Properties.Items[MemberAbsoluteExpiryProperty] = absoluteExpiry.ToString("O");
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            if (!context.Properties.Items.TryGetValue(MemberAbsoluteExpiryProperty, out var rawExpiry) ||
                !DateTimeOffset.TryParse(
                    rawExpiry,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var absoluteExpiry) ||
                (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow() >= absoluteExpiry)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.Member);
                return;
            }

            // Password changes (reset or self-service) rotate Identity's SecurityStamp; comparing
            // it here invalidates every other outstanding session cookie without a server-side
            // session store (會員、驗證與通知.md: 密碼變更後使既有工作階段失效). Every cookie issued
            // by the real login flow carries this claim, so a missing claim only ever means the
            // principal was never issued that way (e.g. test-only sign-in helpers) and is left
            // to the rest of the pipeline to authorize or reject on its own terms.
            var stampClaim = context.Principal?.FindFirstValue(DoSelectClaimTypes.SecurityStamp);
            if (stampClaim is null)
            {
                return;
            }

            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.GetUserAsync(context.Principal!);
            if (user is null || !string.Equals(user.SecurityStamp, stampClaim, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.Member);
            }
        };
    }

    private static void ConfigureAdminCookie(
        CookieAuthenticationOptions options,
        IHostEnvironment environment)
    {
        ConfigureCookieDefaults(options, environment, ".DoSelect.Admin");
        options.ExpireTimeSpan = AdminAbsoluteLifetime;
        options.SlidingExpiration = false;
    }

    /// <summary>
    /// 訪客查單驗證成功後的限單存取憑證。只帶一個不透明權杖明文 Claim
    /// （<c>GuestOrderAccessClaimTypes.TokenValue</c>），不帶訂單識別碼——是哪一筆訂單、
    /// 是否已過期或撤銷一律由 <c>GuestOrderAccessScopeAuthorizer</c> 查 DB 決定,Cookie
    /// 過期時間只是傳輸層的第一道防線,不是唯一依據。端點用
    /// <c>AuthenticationSchemes = GuestOrderAccess</c> 個別授權，不透過 <see cref="DoSelectPolicies"/>。
    /// </summary>
    private static void ConfigureGuestOrderAccessCookie(
        CookieAuthenticationOptions options,
        IHostEnvironment environment)
    {
        ConfigureCookieDefaults(options, environment, ".DoSelect.GuestOrderAccess");
        options.ExpireTimeSpan = GuestOrderAccessLifetime;
        options.SlidingExpiration = false;
    }

    private static void ConfigureCookieDefaults(
        CookieAuthenticationOptions options,
        IHostEnvironment environment,
        string cookieName)
    {
        options.Cookie.Name = cookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Events.OnRedirectToLogin = static context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = static context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void ConfigurePolicies(AuthorizationOptions options)
    {
        options.AddPolicy(DoSelectPolicies.Member, policy =>
        {
            policy.AddAuthenticationSchemes(DoSelectAuthenticationSchemes.Member);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member);
        });

        AddAdminPolicy(options, DoSelectPolicies.Admin);
        AddAdminPolicy(options, DoSelectPolicies.CatalogManager,
            DoSelectRoles.CatalogManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.ReturnApprove,
            DoSelectRoles.OrderManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.RefundExecute,
            DoSelectRoles.FinanceManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.InvoiceManage,
            DoSelectRoles.FinanceManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.CouponManage,
            DoSelectRoles.FinanceManager, DoSelectRoles.MarketingAnalyst, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.ReportHighRiskReview,
            DoSelectRoles.CustomerServiceSupervisor, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.RoleAssignmentManage,
            DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.PersonalDataViewFull,
            DoSelectRoles.PrivacyAdmin, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.PersonalDataExport,
            DoSelectRoles.PrivacyAdmin, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.AuditViewSecurity,
            DoSelectRoles.SecurityAdmin, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.AuditViewPrivacy,
            DoSelectRoles.PrivacyAdmin, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.AuditExport,
            DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.SupportTicketHandle,
            DoSelectRoles.CustomerService, DoSelectRoles.CustomerServiceSupervisor);
        AddAdminPolicy(options, DoSelectPolicies.SupportTicketSupervise,
            DoSelectRoles.CustomerServiceSupervisor, DoSelectRoles.SuperAdmin);
    }

    private static void AddAdminPolicy(
        AuthorizationOptions options,
        string policyName,
        params string[] roles)
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.AddAuthenticationSchemes(DoSelectAuthenticationSchemes.Admin);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin);
            policy.RequireClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor);
            if (roles.Length > 0)
            {
                policy.RequireRole(roles);
            }
        });
    }
}
