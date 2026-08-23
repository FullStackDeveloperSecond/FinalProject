using DoSelect.Application.Shopping;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>
/// Maps <see cref="ShoppingWriteException"/> error codes to HTTP status codes, mirroring
/// <see cref="CatalogWriteExceptionMapper"/>'s shape so Cart controllers stay thin adapters.
/// </summary>
internal static class ShoppingWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this ShoppingWriteException exception, HttpContext httpContext)
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
        ShoppingWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        ShoppingWriteException.ErrorCodes.SkuUnavailable => StatusCodes.Status409Conflict,
        ShoppingWriteException.ErrorCodes.CartQuantityExceeded => StatusCodes.Status409Conflict,
        ShoppingWriteException.ErrorCodes.CartItemLimitExceeded => StatusCodes.Status409Conflict,
        ShoppingWriteException.ErrorCodes.CartItemRequiresAttention => StatusCodes.Status409Conflict,
        ShoppingWriteException.ErrorCodes.CartMergeConflict => StatusCodes.Status409Conflict,
        ShoppingWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ShoppingWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
