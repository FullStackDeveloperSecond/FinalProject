using System.Diagnostics;
using System.Security;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Configuration;
using DoSelect.Application.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace DoSelect.Api.Observability;

public static class ObservabilityExtensions
{
    private const long LogFileSizeLimitBytes = 100L * 1024 * 1024;
    private const int RetainedLogFileCountLimit = 20;
    private static readonly TimeSpan RetainedLogFileTimeLimit = TimeSpan.FromDays(14);

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedConfiguration();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(
            (services, loggerConfiguration) =>
            {
                var environment = services.GetRequiredService<IHostEnvironment>();
                var storageOptions = services.GetRequiredService<IOptions<StorageOptions>>().Value;
                var observabilityOptions = services
                    .GetRequiredService<IOptions<ObservabilityOptions>>()
                    .Value;

                ConfigureLogger(
                    loggerConfiguration,
                    environment,
                    storageOptions,
                    observabilityOptions);
            },
            writeToProviders: false);

        builder.Services
            .AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy(),
                tags: ["live"])
            .AddCheck<StorageReadinessHealthCheck>(
                "storage",
                tags: ["ready"])
            .AddCheck<DatabaseReadinessHealthCheck>(
                "database",
                tags: ["ready"]);

        builder.Services.AddSingleton<IDatabaseReadinessProbe, EfCoreDatabaseReadinessProbe>();

        return builder;
    }

    public static WebApplication UseRequestObservability(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {PathTemplate} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.GetLevel = static (httpContext, _, exception) =>
            {
                if (exception is not null ||
                    httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    return LogEventLevel.Error;
                }

                return httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest
                    ? LogEventLevel.Warning
                    : LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = static (diagnosticContext, httpContext) =>
            {
                var pathTemplate = httpContext.GetEndpoint() is RouteEndpoint routeEndpoint
                    ? routeEndpoint.RoutePattern.RawText
                    : null;

                diagnosticContext.Set(
                    "CorrelationId",
                    CorrelationIdMiddleware.GetCorrelationId(httpContext));
                diagnosticContext.Set(
                    "TraceId",
                    Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);
                diagnosticContext.Set(
                    "PathTemplate",
                    pathTemplate ?? httpContext.Request.Path.Value ?? "/");
                diagnosticContext.Set(
                    "UserType",
                    httpContext.User.Identity?.IsAuthenticated == true
                        ? "Authenticated"
                        : "Anonymous");
            };
        });

        return app;
    }

    public static WebApplication MapObservabilityHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("live"),
            ResponseWriter = WriteSafeHealthResponseAsync,
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteSafeHealthResponseAsync,
        });

        return app;
    }

    private static void ConfigureLogger(
        LoggerConfiguration loggerConfiguration,
        IHostEnvironment environment,
        StorageOptions storageOptions,
        ObservabilityOptions observabilityOptions)
    {
        var minimumLevel = environment.IsDevelopment()
            ? LogEventLevel.Debug
            : LogEventLevel.Information;

        loggerConfiguration
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "DoSelect.Api")
            .Enrich.WithProperty("Environment", environment.EnvironmentName)
            .WriteTo.Console(new JsonFormatter(renderMessage: true));

        if (!observabilityOptions.FileLoggingEnabled)
        {
            return;
        }

        var logDirectory = Path.Combine(storageOptions.DataRoot, "logs");
        EnsureWritableLogDirectory(logDirectory);

        loggerConfiguration.WriteTo.File(
            new JsonFormatter(renderMessage: true),
            Path.Combine(logDirectory, "doselect-api-.json"),
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: LogFileSizeLimitBytes,
            rollOnFileSizeLimit: true,
            shared: true,
            retainedFileCountLimit: RetainedLogFileCountLimit,
            retainedFileTimeLimit: RetainedLogFileTimeLimit,
            buffered: false,
            flushToDiskInterval: TimeSpan.FromSeconds(1));
    }

    private static void EnsureWritableLogDirectory(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            var probePath = Path.Combine(logDirectory, $".{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or SecurityException or
                NotSupportedException)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(StorageOptions),
                ["Configuration key 'Storage:DataRoot' must reference a writable directory."]);
        }
    }

    private static Task WriteSafeHealthResponseAsync(
        HttpContext httpContext,
        HealthReport healthReport)
    {
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            new { status = healthReport.Status.ToString() },
            cancellationToken: httpContext.RequestAborted);
    }
}
