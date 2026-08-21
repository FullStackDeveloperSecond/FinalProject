using DoSelect.Api.Configuration;
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

    // Per-IP request budget for auth endpoints that accept an email address (register,
    // resend-verification, forgot-password). Placeholder default pending a product decision —
    // see PR discussion. Complements the per-email IEmailRequestThrottle in the Application
    // layer, which protects a single target account from being hit from many different IPs.
    private const int PerIpPermitLimit = 5;
    private static readonly TimeSpan PerIpWindow = TimeSpan.FromHours(1);

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
                ConfigureAdminCookie(options, environment));

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

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddPerIpFixedWindowPolicy(options, RateLimitPolicies.AuthRegister);
            AddPerIpFixedWindowPolicy(options, RateLimitPolicies.AuthResendVerification);
            AddPerIpFixedWindowPolicy(options, RateLimitPolicies.AuthForgotPassword);
        });

        return services;
    }

    private static void AddPerIpFixedWindowPolicy(RateLimiterOptions options, string policyName) =>
        options.AddPolicy(policyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIpAddress(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PerIpPermitLimit,
                Window = PerIpWindow,
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
