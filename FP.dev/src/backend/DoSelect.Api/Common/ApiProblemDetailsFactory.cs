using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;

namespace DoSelect.Api.Common;

public static class ApiProblemDetailsFactory
{
    public static ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string code,
        string? title = null,
        string? detail = null)
    {
        var problemDetails = new ProblemDetails
        {
            Detail = detail,
            Instance = httpContext.Request.Path,
            Status = statusCode,
            Title = title ?? ReasonPhrases.GetReasonPhrase(statusCode),
            Type = CreateType(code),
        };

        AddExtensions(problemDetails, httpContext, code);
        return problemDetails;
    }

    public static ValidationProblemDetails CreateValidation(
        HttpContext httpContext,
        ModelStateDictionary modelState)
    {
        var errors = modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The value is invalid."
                        : error.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Detail = "One or more validation errors occurred.",
            Instance = httpContext.Request.Path,
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Type = CreateType(ApiErrorCodes.ValidationFailed),
        };

        AddExtensions(problemDetails, httpContext, ApiErrorCodes.ValidationFailed);
        return problemDetails;
    }

    public static void Customize(ProblemDetailsContext context)
    {
        var problemDetails = context.ProblemDetails;
        var statusCode = problemDetails.Status ?? context.HttpContext.Response.StatusCode;

        problemDetails.Status = statusCode;
        problemDetails.Instance ??= context.HttpContext.Request.Path;
        problemDetails.Title ??= ReasonPhrases.GetReasonPhrase(statusCode);

        if (!TryGetCode(problemDetails, out var code) &&
            !ApiErrorCodes.TryGetForStatusCode(statusCode, out code))
        {
            return;
        }

        problemDetails.Type ??= CreateType(code);
        AddExtensions(problemDetails, context.HttpContext, code);
    }

    private static void AddExtensions(
        ProblemDetails problemDetails,
        HttpContext httpContext,
        string code)
    {
        problemDetails.Extensions.TryAdd("code", code);
        problemDetails.Extensions.TryAdd("traceId", GetTraceId(httpContext));
        problemDetails.Extensions.TryAdd(
            "correlationId",
            CorrelationIdMiddleware.GetCorrelationId(httpContext));
    }

    private static string CreateType(string code) => $"urn:doselect:problem:{code}";

    private static string GetTraceId(HttpContext httpContext) =>
        Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

    private static bool TryGetCode(ProblemDetails problemDetails, out string code)
    {
        if (problemDetails.Extensions.TryGetValue("code", out var value) &&
            value is string stringValue &&
            !string.IsNullOrWhiteSpace(stringValue))
        {
            code = stringValue;
            return true;
        }

        code = string.Empty;
        return false;
    }
}
