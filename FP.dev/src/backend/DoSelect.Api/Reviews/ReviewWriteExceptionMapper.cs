using DoSelect.Api.Common;
using DoSelect.Application.Reviews;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Reviews;

internal static class ReviewWriteExceptionMapper
{
    public static ActionResult ToActionResult(
        this ReviewWriteException exception,
        HttpContext httpContext)
    {
        var status = exception.Code switch
        {
            ReviewWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
            ReviewWriteException.ErrorCodes.NotEligible => StatusCodes.Status403Forbidden,
            ReviewWriteException.ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ReviewWriteException.ErrorCodes.Conflict or
                ReviewWriteException.ErrorCodes.ConcurrencyConflict or
                ReviewWriteException.ErrorCodes.ImageLimitExceeded => StatusCodes.Status409Conflict,
            ReviewWriteException.ErrorCodes.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
            ReviewWriteException.ErrorCodes.FileTypeNotAllowed => StatusCodes.Status415UnsupportedMediaType,
            ReviewWriteException.ErrorCodes.FileMalwareDetected => StatusCodes.Status422UnprocessableEntity,
            ReviewWriteException.ErrorCodes.FileScanUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };
        var problem = ApiProblemDetailsFactory.Create(
            httpContext,
            status,
            exception.Code,
            detail: exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }
}
