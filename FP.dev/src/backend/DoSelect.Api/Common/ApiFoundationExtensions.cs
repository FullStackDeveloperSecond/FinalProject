using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using DoSelect.Api.Security;

namespace DoSelect.Api.Common;

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
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services
            .AddControllers(options =>
            {
                options.ModelMetadataDetailsProviders.Add(
                    new SystemTextJsonValidationMetadataProvider());
                options.Filters.Add<GlobalAntiforgeryFilter>();
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

        return services;
    }

    public static WebApplication UseApiFoundation(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
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

        return app;
    }
}
