using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using DoSelect.Api.Security;

namespace DoSelect.Api.Common;

/// <summary>Named rate-limiter policies opted into via <c>[EnableRateLimiting(...)]</c> on a controller/action.</summary>
public static class RateLimiterPolicies
{
    /// <summary>
    /// Anonymous, unauthenticated endpoints that persist a database row per call
    /// (CompatibilityChecksController, BuildSharesController) — unbounded anonymous traffic is
    /// otherwise an unmetered write amplification / storage-growth vector (組長 PR #34 round-4
    /// review, item 3). 30 requests/minute per client IP is this PR's own starting number, not a
    /// value from any spec doc — flagged for correction if a different limit is expected.
    /// </summary>
    public const string PublicBuildsAnonymous = "public-builds-anonymous";
}

public static class ApiFoundationExtensions
{
    private static readonly HashSet<int> HandledStatusCodes =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status405MethodNotAllowed,
        StatusCodes.Status409Conflict,
        StatusCodes.Status415UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests,
        StatusCodes.Status500InternalServerError,
        StatusCodes.Status503ServiceUnavailable,
    ];

    public static IServiceCollection AddApiFoundation(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ApiProblemDetailsFactory.Customize;
        });
        services.AddExceptionHandler<DomainProblemExceptionHandler>();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Setting only the status code (no custom OnRejected body) lets the existing
        // UseStatusCodePages handler below produce the same rate_limit_exceeded ProblemDetails
        // envelope every other 429 already gets — HandledStatusCodes already includes 429, this
        // just needed something to actually return it.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimiterPolicies.PublicBuildsAnonymous, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        services
            .AddControllers(options =>
            {
                options.ModelMetadataDetailsProviders.Add(
                    new SystemTextJsonValidationMetadataProvider());
                options.Filters.Add<GlobalAntiforgeryFilter>();
            })
            .AddJsonOptions(options =>
            {
                // API DTO契約: enums serialize as stable camelCase tokens, never raw ints.
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = actionContext =>
                {
                    var problemDetails = ApiProblemDetailsFactory.CreateValidation(
                        actionContext.HttpContext,
                        actionContext.ModelState);
                    var result = new BadRequestObjectResult(problemDetails);
                    result.ContentTypes.Add("application/problem+json");
                    return result;
                };
            });

        // Minimal APIs and OpenAPI schema generation (Microsoft.AspNetCore.OpenApi) read
        // Http.Json.JsonOptions, a separate options object from the MVC JsonOptions configured
        // above — without mirroring the converter here, the generated schema (and hence the
        // TypeScript client) describes enums as numbers while every real response is a string.
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        });

        return services;
    }

    public static WebApplication UseApiFoundation(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        // UseStatusCodePages must wrap UseRateLimiter (registered first here = outermost), not the
        // other way around — it only rewrites a response for status codes produced by middleware
        // that runs *after* it in the pipeline. Registered after it, the rate limiter's 429
        // short-circuit would never reach UseStatusCodePages's rewrite logic at all.
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var httpContext = statusCodeContext.HttpContext;
            var statusCode = httpContext.Response.StatusCode;
            if (!HandledStatusCodes.Contains(statusCode) ||
                !ApiErrorCodes.TryGetForStatusCode(statusCode, out var code))
            {
                return;
            }

            var problemDetailsService =
                httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
            var problemDetails = ApiProblemDetailsFactory.Create(
                httpContext,
                statusCode,
                code);

            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
            });
        });
        app.UseRateLimiter();

        return app;
    }
}
