using DoSelect.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>
/// Maps <see cref="CatalogWriteException"/> error codes to the HTTP status codes
/// registered in 03-架構/API錯誤碼目錄.md. Shared by every Catalog admin controller
/// so each controller stays a thin adapter with no duplicated status-code logic.
/// </summary>
internal static class CatalogWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this CatalogWriteException exception, HttpContext httpContext)
    {
        var statusCode = StatusCodeFor(exception.ErrorCode);
        var problem = ApiProblemDetailsFactory.Create(
            httpContext,
            statusCode,
            exception.ErrorCode,
            detail: exception.Message);
        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private static int StatusCodeFor(string errorCode) => errorCode switch
    {
        CatalogWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        CatalogWriteException.ErrorCodes.BrandCodeDuplicate => StatusCodes.Status409Conflict,
        CatalogWriteException.ErrorCodes.CategoryCodeDuplicate => StatusCodes.Status409Conflict,
        CatalogWriteException.ErrorCodes.TagCodeDuplicate => StatusCodes.Status409Conflict,
        CatalogWriteException.ErrorCodes.ProductCodeDuplicate => StatusCodes.Status409Conflict,
        CatalogWriteException.ErrorCodes.SkuCodeDuplicate => StatusCodes.Status409Conflict,
        CatalogWriteException.ErrorCodes.SkuCodeImmutable => StatusCodes.Status409Conflict,
        CatalogWriteException.ErrorCodes.SkuDeleteReferenced => StatusCodes.Status409Conflict,
        CatalogWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        // CategoryParentInvalid and ReferenceNotFound are not yet registered in
        // 03-架構/API錯誤碼目錄.md (pre-existing gap, flagged for alex to log).
        // Treated as 400 because both describe an invalid caller-supplied reference.
        CatalogWriteException.ErrorCodes.CategoryParentInvalid => StatusCodes.Status400BadRequest,
        CatalogWriteException.ErrorCodes.ReferenceNotFound => StatusCodes.Status400BadRequest,
        CatalogWriteException.ErrorCodes.SpecificationInvalid => StatusCodes.Status400BadRequest,
        CatalogWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
