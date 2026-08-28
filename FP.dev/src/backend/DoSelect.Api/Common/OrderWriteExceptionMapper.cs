using DoSelect.Application.Orders;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>
/// Maps <see cref="OrderWriteException"/> error codes to HTTP status codes, mirroring
/// <see cref="ShoppingWriteExceptionMapper"/>'s shape so Order controllers stay thin adapters.
/// </summary>
internal static class OrderWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this OrderWriteException exception, HttpContext httpContext)
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
        OrderWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        OrderWriteException.ErrorCodes.OrderStateConflict => StatusCodes.Status409Conflict,
        OrderWriteException.ErrorCodes.OrderCancellationNotAllowed => StatusCodes.Status409Conflict,
        OrderWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        OrderWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
