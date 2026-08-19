using System.Net.Mail;
using DoSelect.Infrastructure.Email;
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
            .AddOptions<OpenAiOptions>()
            .BindConfiguration(OpenAiOptions.SectionName)
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

        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services.AddSingleton<IValidateOptions<OpenAiOptions>, OpenAiOptionsValidator>();
        services.AddSingleton<IValidateOptions<SmtpEmailOptions>, EmailOptionsValidator>();
        services.AddSingleton<IValidateOptions<DemoOptions>, DemoOptionsValidator>();

        return services;
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

internal sealed class OpenAiOptionsValidator : IValidateOptions<OpenAiOptions>
{
    private readonly IOptions<FeatureOptions> _features;

    public OpenAiOptionsValidator(IOptions<FeatureOptions> features)
    {
        _features = features;
    }

    public ValidateOptionsResult Validate(string? name, OpenAiOptions options)
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

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add(
                "Configuration key 'OpenAI:Model' is required when 'Features:AiEnabled' is true.");
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

internal sealed class DemoOptionsValidator : IValidateOptions<DemoOptions>
{
    private readonly IHostEnvironment _environment;

    public DemoOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, DemoOptions options)
    {
        if (options.SimulationEndpointsEnabled &&
            !_environment.IsEnvironment("Demo"))
        {
            return ValidateOptionsResult.Fail(
                "Configuration key 'Demo:SimulationEndpointsEnabled' may only be true in the Demo environment.");
        }

        return ValidateOptionsResult.Success;
    }
}
