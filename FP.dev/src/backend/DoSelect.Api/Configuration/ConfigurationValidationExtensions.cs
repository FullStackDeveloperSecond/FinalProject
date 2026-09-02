using System.Net.Mail;
using System.Text;
using DoSelect.Application.Common;
using DoSelect.Application.Storage;
using DoSelect.Application.Checkout;
using DoSelect.Infrastructure.Email;
using DoSelect.Infrastructure.Ai;
using DoSelect.Infrastructure.Promotions;
using DoSelect.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.Configuration;

public static class ConfigurationValidationExtensions
{
    public static IServiceCollection AddValidatedConfiguration(this IServiceCollection services)
    {
        services
            .AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<FeatureOptions>()
            .BindConfiguration(FeatureOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<OpenAiResponsesOptions>()
            .BindConfiguration(OpenAiResponsesOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<SmtpEmailOptions>()
            .BindConfiguration(SmtpEmailOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<DemoOptions>()
            .BindConfiguration(DemoOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<ObservabilityOptions>()
            .BindConfiguration(ObservabilityOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<CorsOptions>()
            .BindConfiguration(CorsOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<FrontendLinkOptions>()
            .BindConfiguration(FrontendLinkOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<GuestOrderAccessOptions>()
            .BindConfiguration(GuestOrderAccessOptions.SectionName)
            .ValidateOnStart();
        services
            .AddOptions<CheckoutPolicyOptions>()
            .BindConfiguration(CheckoutPolicyOptions.SectionName)
            .ValidateOnStart();
        // 這把 Secret 只在訪客實際使用優惠券時才是必需；不得讓會員或未使用優惠券的
        // 訪客 Checkout 因選用設定缺少而無法啟動。長度由 CouponGuestUsageHasher
        // 在使用點 fail closed。
        services
            .AddOptions<CouponGuestUsageOptions>()
            .BindConfiguration(CouponGuestUsageOptions.SectionName);

        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services.AddSingleton<IValidateOptions<OpenAiResponsesOptions>, OpenAiOptionsValidator>();
        services.AddSingleton<IValidateOptions<SmtpEmailOptions>, EmailOptionsValidator>();
        services.AddSingleton<IValidateOptions<DemoOptions>, DemoOptionsValidator>();
        services.AddSingleton<IValidateOptions<CorsOptions>, CorsOptionsValidator>();
        services.AddSingleton<IValidateOptions<FrontendLinkOptions>, FrontendLinkOptionsValidator>();
        services.AddSingleton<IValidateOptions<RateLimitOptions>, RateLimitOptionsValidator>();
        services.AddSingleton<IValidateOptions<GuestOrderAccessOptions>, GuestOrderAccessOptionsValidator>();
        services.AddSingleton<IValidateOptions<CheckoutPolicyOptions>, CheckoutPolicyOptionsValidator>();

        return services;
    }
}

internal sealed class CheckoutPolicyOptionsValidator : IValidateOptions<CheckoutPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, CheckoutPolicyOptions options)
    {
        var failures = new List<string>();
        AddPositiveFailure(failures, options.TermsVersion, "CheckoutPolicy:TermsVersion");
        AddPositiveFailure(failures, options.ReturnVersion, "CheckoutPolicy:ReturnVersion");
        AddPositiveFailure(failures, options.PrivacyVersion, "CheckoutPolicy:PrivacyVersion");
        AddPositiveFailure(
            failures,
            options.ShippingConstraintVersion,
            "CheckoutPolicy:ShippingConstraintVersion");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddPositiveFailure(
        ICollection<string> failures,
        int value,
        string configurationKey)
    {
        if (value <= 0)
        {
            failures.Add($"Configuration key '{configurationKey}' must be greater than zero.");
        }
    }
}

internal sealed class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        if (options.AllowedOrigins.Length == 0)
        {
            return ValidateOptionsResult.Fail(
                "Configuration key 'Cors:AllowedOrigins' must contain at least one origin.");
        }

        var normalizedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawOrigin in options.AllowedOrigins)
        {
            if (!Uri.TryCreate(rawOrigin, UriKind.Absolute, out var origin) ||
                origin.Scheme is not ("http" or "https") ||
                !string.IsNullOrEmpty(origin.UserInfo) ||
                origin.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(origin.Query) ||
                !string.IsNullOrEmpty(origin.Fragment))
            {
                return ValidateOptionsResult.Fail(
                    "Each value in 'Cors:AllowedOrigins' must be an HTTP or HTTPS origin without a path, query, fragment, or credentials.");
            }

            var normalizedOrigin = origin.GetLeftPart(UriPartial.Authority);
            if (!normalizedOrigins.Add(normalizedOrigin))
            {
                return ValidateOptionsResult.Fail(
                    "Configuration key 'Cors:AllowedOrigins' must not contain duplicate origins.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DataRoot))
        {
            return ValidateOptionsResult.Fail(
                "Configuration key 'Storage:DataRoot' is required.");
        }

        try
        {
            if (!Path.IsPathFullyQualified(options.DataRoot))
            {
                return ValidateOptionsResult.Fail(
                    "Configuration key 'Storage:DataRoot' must be an absolute path.");
            }

            var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.DataRoot));
            var rootPath = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalizedPath)!);
            if (string.Equals(normalizedPath, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return ValidateOptionsResult.Fail(
                    "Configuration key 'Storage:DataRoot' must not reference a filesystem root.");
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValidateOptionsResult.Fail(
                "Configuration key 'Storage:DataRoot' is not a valid absolute path.");
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class OpenAiOptionsValidator : IValidateOptions<OpenAiResponsesOptions>
{
    private readonly IOptions<FeatureOptions> _features;

    public OpenAiOptionsValidator(IOptions<FeatureOptions> features)
    {
        _features = features;
    }

    public ValidateOptionsResult Validate(string? name, OpenAiResponsesOptions options)
    {
        if (!_features.Value.AiEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                "Configuration key 'OpenAI:ApiKey' is required when 'Features:AiEnabled' is true.");
        }

        if (string.IsNullOrWhiteSpace(options.SupportModel))
        {
            failures.Add(
                "Configuration key 'OpenAI:SupportModel' is required when 'Features:AiEnabled' is true.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductSearchModel))
        {
            failures.Add(
                "Configuration key 'OpenAI:ProductSearchModel' is required when 'Features:AiEnabled' is true.");
        }

        if (options.SupportTimeoutMilliseconds is < 1_000 or > 60_000)
        {
            failures.Add(
                "Configuration key 'OpenAI:SupportTimeoutMilliseconds' must be between 1000 and 60000.");
        }

        if (options.ProductSearchTimeoutMilliseconds is < 1_000 or > 60_000)
        {
            failures.Add(
                "Configuration key 'OpenAI:ProductSearchTimeoutMilliseconds' must be between 1000 and 60000.");
        }


        if (options.SupportInputCostPerMillionTokens < 0)
        {
            failures.Add(
                "Configuration key 'OpenAI:SupportInputCostPerMillionTokens' must be zero or greater.");
        }

        if (options.SupportOutputCostPerMillionTokens < 0)
        {
            failures.Add(
                "Configuration key 'OpenAI:SupportOutputCostPerMillionTokens' must be zero or greater.");
        }

        if (options.ProductSearchInputCostPerMillionTokens < 0)
        {
            failures.Add(
                "Configuration key 'OpenAI:ProductSearchInputCostPerMillionTokens' must be zero or greater.");
        }

        if (options.ProductSearchOutputCostPerMillionTokens < 0)
        {
            failures.Add(
                "Configuration key 'OpenAI:ProductSearchOutputCostPerMillionTokens' must be zero or greater.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(options.AnonymousIdentityPepper ?? string.Empty) < 32)
        {
            failures.Add(
                "Configuration key 'OpenAI:AnonymousIdentityPepper' must contain at least 32 UTF-8 bytes.");
        }

        if (!options.BudgetAlertRecipientAdminPublicId.HasValue ||
            options.BudgetAlertRecipientAdminPublicId.Value == Guid.Empty)
        {
            failures.Add(
                "Configuration key 'OpenAI:BudgetAlertRecipientAdminPublicId' must contain a non-empty ApplicationUser PublicId.");
        }

        if (options.DemoMemberPublicIds.Length > 2 ||
            options.DemoMemberPublicIds.Any(publicId => publicId == Guid.Empty) ||
            options.DemoMemberPublicIds.Distinct().Count() != options.DemoMemberPublicIds.Length)
        {
            failures.Add(
                "Configuration key 'OpenAI:DemoMemberPublicIds' must contain at most two distinct non-empty member PublicIds.");
        }

        if (options.DemoBrowserIds.Length > 1 ||
            options.DemoBrowserIds.Any(publicId => publicId == Guid.Empty))
        {
            failures.Add(
                "Configuration key 'OpenAI:DemoBrowserIds' must contain at most one non-empty browser ID.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class EmailOptionsValidator : IValidateOptions<SmtpEmailOptions>
{
    private readonly IOptions<FeatureOptions> _features;

    public EmailOptionsValidator(IOptions<FeatureOptions> features)
    {
        _features = features;
    }

    public ValidateOptionsResult Validate(string? name, SmtpEmailOptions options)
    {
        if (!_features.Value.EmailEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        AddRequiredFailure(failures, options.SmtpHost, "Email:SmtpHost");
        AddRequiredFailure(failures, options.UserName, "Email:UserName");
        AddRequiredFailure(failures, options.Password, "Email:Password");
        AddRequiredFailure(failures, options.SenderName, "Email:SenderName");
        AddRequiredFailure(failures, options.SenderAddress, "Email:SenderAddress");

        if (options.SmtpPort is < 1 or > 65535)
        {
            failures.Add(
                "Configuration key 'Email:SmtpPort' must be between 1 and 65535.");
        }

        if (options.TimeoutMilliseconds is < 1000 or > 60000)
        {
            failures.Add(
                "Configuration key 'Email:TimeoutMilliseconds' must be between 1000 and 60000.");
        }

        if (!string.IsNullOrWhiteSpace(options.SenderAddress) &&
            !MailAddress.TryCreate(options.SenderAddress, out _))
        {
            failures.Add(
                "Configuration key 'Email:SenderAddress' must be a valid Email address.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRequiredFailure(
        ICollection<string> failures,
        string? value,
        string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(
                $"Configuration key '{configurationKey}' is required when 'Features:EmailEnabled' is true.");
        }
    }
}

internal sealed class FrontendLinkOptionsValidator : IValidateOptions<FrontendLinkOptions>
{
    public ValidateOptionsResult Validate(string? name, FrontendLinkOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ValidateOptionsResult.Fail(
                "Configuration key 'Frontend:BaseUrl' must be an absolute HTTP or HTTPS URL.");
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitOptions options)
    {
        var failures = new List<string>();
        AddPositiveFailure(failures, options.EmailPurposePermitLimit, "RateLimiting:EmailPurposePermitLimit");
        AddPositiveFailure(failures, options.EmailPurposeWindowHours, "RateLimiting:EmailPurposeWindowHours");
        AddPositiveFailure(failures, options.PerIpPermitLimit, "RateLimiting:PerIpPermitLimit");
        AddPositiveFailure(failures, options.PerIpWindowHours, "RateLimiting:PerIpWindowHours");
        AddPositiveFailure(failures, options.LoginPerIpPermitLimit, "RateLimiting:LoginPerIpPermitLimit");
        AddPositiveFailure(failures, options.LoginPerIpWindowHours, "RateLimiting:LoginPerIpWindowHours");
        AddPositiveFailure(
            failures,
            options.GuestOrderAccessIpPermitLimit,
            "RateLimiting:GuestOrderAccessIpPermitLimit");
        AddPositiveFailure(
            failures,
            options.GuestOrderAccessEmailPermitLimit,
            "RateLimiting:GuestOrderAccessEmailPermitLimit");
        AddPositiveFailure(
            failures,
            options.GuestOrderAccessOrderLookupPermitLimit,
            "RateLimiting:GuestOrderAccessOrderLookupPermitLimit");
        AddPositiveFailure(
            failures,
            options.GuestOrderAccessWindowMinutes,
            "RateLimiting:GuestOrderAccessWindowMinutes");
        AddPositiveFailure(
            failures,
            options.AdminChallengePermitLimit,
            "RateLimiting:AdminChallengePermitLimit");
        AddPositiveFailure(
            failures,
            options.AdminChallengeWindowMinutes,
            "RateLimiting:AdminChallengeWindowMinutes");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddPositiveFailure(ICollection<string> failures, int value, string configurationKey)
    {
        if (value <= 0)
        {
            failures.Add($"Configuration key '{configurationKey}' must be greater than zero.");
        }
    }
}

internal sealed class GuestOrderAccessOptionsValidator : IValidateOptions<GuestOrderAccessOptions>
{
    public ValidateOptionsResult Validate(string? name, GuestOrderAccessOptions options)
    {
        if (Encoding.UTF8.GetByteCount(options.Pepper) < 32)
        {
            return ValidateOptionsResult.Fail(
                "Configuration key 'GuestOrderAccess:Pepper' must contain at least 32 UTF-8 bytes.");
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class DemoOptionsValidator : IValidateOptions<DemoOptions>
{
    private readonly IHostEnvironment _environment;

    public DemoOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, DemoOptions options)
    {
        // alex 裁定：E2E 環境需要真的打到模擬付款端點才能證明 Outbox 會寫入 Invoice 請求，
        // 所以旗標的允許名單從單純的 Demo 放寬到 Demo 或 E2E，其餘環境（含 Development、
        // Staging、Production）維持原本「打開就拒絕啟動」的行為不變。
        if (options.SimulationEndpointsEnabled &&
            !_environment.IsEnvironment("Demo") &&
            !_environment.IsEnvironment("E2E"))
        {
            return ValidateOptionsResult.Fail(
                "Configuration key 'Demo:SimulationEndpointsEnabled' may only be true in the Demo or E2E environment.");
        }

        return ValidateOptionsResult.Success;
    }
}
