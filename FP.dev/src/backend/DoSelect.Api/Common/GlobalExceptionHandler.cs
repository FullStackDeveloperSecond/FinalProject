using DoSelect.Application.Idempotency;
using Microsoft.AspNetCore.Diagnostics;

namespace DoSelect.Api.Common;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetailsService;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IProblemDetailsService problemDetailsService)
    {
        _logger = logger;
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        if (exception is IdempotencyConflictException idempotencyConflict)
        {
            return await HandleIdempotencyConflictAsync(
                httpContext,
                idempotencyConflict,
                cancellationToken);
        }

        _logger.LogError(
            exception,
            "An unhandled exception occurred while processing {RequestMethod} {RequestPath}.",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problemDetails = ApiProblemDetailsFactory.Create(
            httpContext,
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.UnexpectedError,
            "Unexpected error",
            "An unexpected error occurred.");

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            Exception = exception,
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });
    }

    private async ValueTask<bool> HandleIdempotencyConflictAsync(
        HttpContext httpContext,
        IdempotencyConflictException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Idempotency conflict {ErrorCode} occurred while processing {RequestMethod} {RequestPath}.",
            exception.ErrorCode,
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        if (exception.RetryAfterSeconds is int retryAfterSeconds)
        {
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        var problemDetails = ApiProblemDetailsFactory.Create(
            httpContext,
            StatusCodes.Status409Conflict,
            exception.ErrorCode,
            "Idempotency conflict",
            "The idempotent request conflicts with an existing request.");

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            Exception = exception,
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });
    }
}
