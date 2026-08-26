using DoSelect.Application.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>
/// Maps <see cref="InventoryWriteException"/> error codes to HTTP status codes, mirroring
/// <see cref="ShoppingWriteExceptionMapper"/>'s shape so Inventory controllers stay thin adapters.
/// </summary>
internal static class InventoryWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this InventoryWriteException exception, HttpContext httpContext)
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
        InventoryWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        InventoryWriteException.ErrorCodes.InsufficientStock => StatusCodes.Status409Conflict,
        InventoryWriteException.ErrorCodes.ReservationNotActive => StatusCodes.Status409Conflict,
        InventoryWriteException.ErrorCodes.ReservationAlreadyProcessed => StatusCodes.Status409Conflict,
        InventoryWriteException.ErrorCodes.ReconciliationCaseNotOpen => StatusCodes.Status409Conflict,
        InventoryWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        InventoryWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
