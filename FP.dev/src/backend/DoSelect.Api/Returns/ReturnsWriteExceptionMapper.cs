using DoSelect.Api.Common;
using DoSelect.Application.Returns;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Returns;

/// <summary>Maps ReturnsWriteException error codes to HTTP status codes, mirroring
/// ShoppingWriteExceptionMapper's shape so Return controllers stay thin adapters. File error
/// codes use the same specific status codes as FileUploadProblemDetailsFactory (413/415/422/503)
/// rather than a generic 400/409, even though this module throws them via ReturnsWriteException
/// instead of catching PrivateFileStoreStatus directly at the Controller.</summary>
internal static class ReturnsWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this ReturnsWriteException exception, HttpContext httpContext)
    {
        var statusCode = StatusCodeFor(exception.ErrorCode);
        var problem = ApiProblemDetailsFactory.Create(httpContext, statusCode, exception.ErrorCode, detail: exception.Message);
        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private static int StatusCodeFor(string errorCode) => errorCode switch
    {
        ReturnsWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        ReturnsWriteException.ErrorCodes.AuthorizationForbidden => StatusCodes.Status403Forbidden,
        ReturnsWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ReturnsWriteException.ErrorCodes.ReturnDeadlineExpired => StatusCodes.Status409Conflict,
        ReturnsWriteException.ErrorCodes.ReturnQuantityExceeded => StatusCodes.Status409Conflict,
        ReturnsWriteException.ErrorCodes.ReturnStateConflict => StatusCodes.Status409Conflict,
        ReturnsWriteException.ErrorCodes.ReturnShipmentDeadlineExpired => StatusCodes.Status409Conflict,
        ReturnsWriteException.ErrorCodes.ReturnShipmentExtensionNotAllowed => StatusCodes.Status409Conflict,
        ReturnsWriteException.ErrorCodes.FileCountExceeded => StatusCodes.Status409Conflict,
        ReturnsWriteException.ErrorCodes.FileSizeExceeded => StatusCodes.Status413PayloadTooLarge,
        ReturnsWriteException.ErrorCodes.FileFormatInvalid => StatusCodes.Status415UnsupportedMediaType,
        ReturnsWriteException.ErrorCodes.FileMalwareDetected => StatusCodes.Status422UnprocessableEntity,
        ReturnsWriteException.ErrorCodes.FileScanUnavailable => StatusCodes.Status503ServiceUnavailable,
        ReturnsWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
