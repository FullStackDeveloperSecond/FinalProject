using DoSelect.Application.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace DoSelect.Api.Common;

/// <summary>
/// Translates Application-layer DomainProblemException into the matching Problem Details
/// response. Registered before GlobalExceptionHandler so known business-rule failures never
/// fall through to the generic 500 handler.
/// </summary>
public sealed class DomainProblemExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public DomainProblemExceptionHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainProblemException domainException)
        {
            return false;
        }

        httpContext.Response.StatusCode = domainException.StatusCode;

        var problemDetails = ApiProblemDetailsFactory.Create(
            httpContext,
            domainException.StatusCode,
            domainException.Code,
            detail: domainException.Message);

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            Exception = exception,
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });
    }
}
