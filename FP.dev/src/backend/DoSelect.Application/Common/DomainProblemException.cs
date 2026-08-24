namespace DoSelect.Application.Common;

/// <summary>
/// A business-rule failure that must surface as a specific HTTP status and stable error code.
/// The Api layer translates this into Problem Details; Application code never references
/// ASP.NET Core status-code types directly to keep this layer framework-agnostic.
/// </summary>
public sealed class DomainProblemException : Exception
{
    private DomainProblemException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }

    public static DomainProblemException NotFound(string message) =>
        new(404, DomainErrorCodes.ResourceNotFound, message);

    public static DomainProblemException Forbidden(string message) =>
        new(403, DomainErrorCodes.AuthorizationForbidden, message);

    public static DomainProblemException Conflict(string code, string message) =>
        new(409, code, message);
}
