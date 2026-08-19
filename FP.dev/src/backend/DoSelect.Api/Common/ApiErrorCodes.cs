namespace DoSelect.Api.Common;

public static class ApiErrorCodes
{
    public const string AuthenticationRequired = "authentication_required";
    public const string AuthorizationForbidden = "authorization_forbidden";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string RateLimitExceeded = "rate_limit_exceeded";
    public const string RequestConflict = "request_conflict";
    public const string RequestContentTypeUnsupported = "request_content_type_unsupported";
    public const string RequestMethodNotAllowed = "request_method_not_allowed";
    public const string ResourceNotFound = "resource_not_found";
    public const string ServiceUnavailable = "service_unavailable";
    public const string UnexpectedError = "unexpected_error";
    public const string ValidationFailed = "validation_failed";

    public static bool TryGetForStatusCode(int statusCode, out string code)
    {
        code = statusCode switch
        {
            StatusCodes.Status400BadRequest => ValidationFailed,
            StatusCodes.Status401Unauthorized => AuthenticationRequired,
            StatusCodes.Status403Forbidden => AuthorizationForbidden,
            StatusCodes.Status404NotFound => ResourceNotFound,
            StatusCodes.Status405MethodNotAllowed => RequestMethodNotAllowed,
            StatusCodes.Status409Conflict => RequestConflict,
            StatusCodes.Status415UnsupportedMediaType => RequestContentTypeUnsupported,
            StatusCodes.Status429TooManyRequests => RateLimitExceeded,
            StatusCodes.Status500InternalServerError => UnexpectedError,
            StatusCodes.Status503ServiceUnavailable => ServiceUnavailable,
            _ => string.Empty,
        };

        return code.Length > 0;
    }
}
