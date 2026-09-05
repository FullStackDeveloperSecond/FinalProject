namespace DoSelect.Application.Common;

/// <summary>
/// A business-rule failure that must surface as a specific HTTP status and stable error code.
/// The Api layer translates this into Problem Details; Application code never references
/// ASP.NET Core status-code types directly to keep this layer framework-agnostic.
/// </summary>
public sealed class DomainProblemException : Exception
{
    private DomainProblemException(int statusCode, string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    private DomainProblemException(int statusCode, string code, string message)
        : this(statusCode, code, message, innerException: null)
    {
    }

    public int StatusCode { get; }

    public string Code { get; }

    /// <summary>
    /// Returns a copy of this exception with <paramref name="innerException"/> attached, so a
    /// secondary failure (for example a failed compensation delete) can be preserved for logging
    /// without losing the original status code/error code this exception maps to.
    /// </summary>
    public DomainProblemException WithInnerException(Exception innerException) =>
        new(StatusCode, Code, Message, innerException);

    public static DomainProblemException NotFound(string message) =>
        new(404, DomainErrorCodes.ResourceNotFound, message);

    public static DomainProblemException Forbidden(string message) =>
        new(403, DomainErrorCodes.AuthorizationForbidden, message);

    public static DomainProblemException Conflict(string code, string message) =>
        new(409, code, message);

    public static DomainProblemException Validation(string message) =>
        new(400, DomainErrorCodes.ValidationFailed, message);

    public static DomainProblemException BadRequest(string code, string message) =>
        new(400, code, message);

    public static DomainProblemException PayloadTooLarge(string code, string message) =>
        new(413, code, message);

    public static DomainProblemException UnsupportedMediaType(string code, string message) =>
        new(415, code, message);

    public static DomainProblemException UnprocessableEntity(string code, string message) =>
        new(422, code, message);

    public static DomainProblemException Gone(string code, string message) =>
        new(410, code, message);

    public static DomainProblemException ServiceUnavailable(string code, string message) =>
        new(503, code, message);
}
