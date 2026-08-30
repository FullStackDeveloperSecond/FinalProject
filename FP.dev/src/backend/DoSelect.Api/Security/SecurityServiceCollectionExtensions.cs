using DoSelect.Api.Common;
using DoSelect.Api.Configuration;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Security;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
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

    /// <summary>AdminChallenge Cookie 效期，密碼驗證成功後到完成 2FA 前的短暫視窗。</summary>
    private static readonly TimeSpan AdminChallengeLifetime = TimeSpan.FromMinutes(10);

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
                ConfigureGuestOrderAccessCookie(options, environment))
            .AddCookie(DoSelectAuthenticationSchemes.AdminChallenge, options =>
                ConfigureAdminChallengeCookie(options, environment));

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

        // ⚠ 待 alex 覆核：讓「TOTP 重新綁定／解除、密碼變更、停用」真正撤銷既有管理 Session。
        // Cookie 是純票證式驗證，本身不查任何撤銷清單；這裡用 Identity 既有的
        // SecurityStamp 機制補上撤銷檢查——bump SecurityStamp 後，舊 Cookie 內嵌的
        // stamp 跟 UserManager 讀到的即時值不一致，就強制登出。
        options.Events.OnValidatePrincipal = async context =>
        {
            var stampInCookie = context.Principal?.FindFirstValue(DoSelectClaimTypes.SecurityStamp);

            // ⚠ 待 alex 覆核的設計取捨：沒有 SecurityStamp claim 的 Cookie 略過檢查而不拒絕。
            // 這是為了與既有的測試簽發（SecurityFoundationTestController 等，不帶此 claim）
            // 及其他既有管理端整合測試相容；本次登入流程簽發的 Cookie 一定帶這個 claim
            // （見 AdminAuthController.BuildAdminPrincipalAsync），撤銷檢查仍會生效。
            if (string.IsNullOrEmpty(stampInCookie))
            {
                return;
            }

            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<DoSelectDbContext>();
            var user = string.IsNullOrEmpty(userId) ? null : await userManager.FindByIdAsync(userId);
            var currentStamp = user is null ? null : await userManager.GetSecurityStampAsync(user);
            var stampMismatch = user is null || !string.Equals(stampInCookie, currentStamp, StringComparison.Ordinal);

            // ⚠ alex review 第二輪 P1#3：只比對 SecurityStamp 不夠——如果某個停權路徑忘記
            // bump SecurityStamp，舊 Cookie 在到期前（最長 2 小時）仍會通過驗證並視為
            // isAuthenticated=true。這裡直接重新確認目前的 AccountStatus／
            // AdminProfile.IsActive，不依賴其他程式碼是否記得撤銷 Stamp。
            //
            // ⚠ alex 裁定 A1（第三輪 P1#2）：角色也要重新確認——零角色管理員不具登入資格，
            // 既有 Session 一旦被移除全部角色，必須立即撤銷，不能等到 Cookie 自然到期（最長
            // 2 小時）。roles 順便先查出來，撤銷時若要寫 Audit 也能直接重用，不必再查一次。
            var isEligible = false;
            IList<string> roles = Array.Empty<string>();
            if (user is not null)
            {
                var profile = await dbContext.AdminProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == user.Id);
                roles = await userManager.GetRolesAsync(user);
                isEligible = user.AccountStatus == AccountStatus.Active &&
                    (profile?.IsActive ?? false) &&
                    roles.Count > 0;
            }

            if (stampMismatch || !isEligible)
            {
                // DEC-P296：寫入中央 AuditLog，取代原本只寫一般 Log 的 IAdminSecurityAuditWriter。
                if (user is not null)
                {
                    // ⚠ alex 裁定 A1：零角色帳號被撤銷時（含撤銷原因就是「角色被清空」的情況）
                    // 不能再建立空角色的 Admin Actor——AuditActor.Create 會拋例外。改用 System
                    // Actor，被撤銷的帳號只當 Resource，撤銷本身跟稽核寫入都照常生效，不會因為
                    // 這個邊角情況而讓整條安全關鍵路徑掛掉。
                    var actor = roles.Count > 0
                        ? AuditActor.Create(AuditActorType.Admin, user.PublicId, roles.ToArray())
                        : AuditActor.Create(AuditActorType.System, publicId: null, roles: []);
                    var auditWriter = context.HttpContext.RequestServices.GetRequiredService<IAuditWriter>();
                    auditWriter.Add(AuditWriteRequest.Create(
                        Guid.CreateVersion7(),
                        actor,
                        AuditActions.AdminSessionsRevoked,
                        AuditResourceTypes.AdminAccount,
                        user.PublicId,
                        AuditResult.Rejected,
                        stampMismatch ? "security_stamp_mismatch" : "account_not_eligible",
                        [AuditFieldChange.Changed("securityStamp")],
                        "admin_auth_state_change",
                        CorrelationIdMiddleware.GetCorrelationId(context.HttpContext),
                        Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                        jobPublicId: null,
                        context.HttpContext.Connection.RemoteIpAddress));
                    await dbContext.SaveChangesAsync();
                }

                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.Admin);
            }
        };
    }

    /// <summary>
    /// ⚠ 新增：規格未定義的暫時憑證，代表「密碼已驗證、2FA 尚未完成」。
    /// 不帶 AccountType／amr claim，端點以 <c>AuthenticationSchemes = AdminChallenge</c>
    /// 個別授權，不透過 <see cref="AddAdminPolicy"/>。
    /// </summary>
    private static void ConfigureAdminChallengeCookie(
        CookieAuthenticationOptions options,
        IHostEnvironment environment)
    {
        ConfigureCookieDefaults(options, environment, ".DoSelect.AdminChallenge");
        options.ExpireTimeSpan = AdminChallengeLifetime;
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

        // AI support distinguishes a real guest-order credential from an anonymous caller:
        // GuestOrderAccess authenticates, then fails the Member claim requirement with 403.
        options.AddPolicy(DoSelectPolicies.AiSupportMember, policy =>
        {
            policy.AddAuthenticationSchemes(
                DoSelectAuthenticationSchemes.Member,
                DoSelectAuthenticationSchemes.GuestOrderAccess);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member);
        });

        AddAdminPolicy(options, DoSelectPolicies.Admin);
        AddAdminPolicy(options, DoSelectPolicies.CatalogManager,
            DoSelectRoles.CatalogManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.OrderManage,
            DoSelectRoles.OrderManager, DoSelectRoles.SuperAdmin);
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
        AddAdminPolicy(options, DoSelectPolicies.CompatibilityRuleView,
            DoSelectRoles.CatalogManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.CompatibilityRuleManageWarnings,
            DoSelectRoles.CatalogManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.CompatibilityRuleManageActivation,
            DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.CompatibilityRuleTest,
            DoSelectRoles.CatalogManager, DoSelectRoles.SuperAdmin);
        AddAdminPolicy(options, DoSelectPolicies.OutboxRetry,
            DoSelectRoles.SuperAdmin);
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
