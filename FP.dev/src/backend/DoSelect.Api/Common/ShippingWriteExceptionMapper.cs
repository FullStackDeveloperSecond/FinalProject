using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>Maps <see cref="ShippingWriteException"/> error codes to HTTP status codes, mirroring <see cref="InventoryWriteExceptionMapper"/>'s shape.</summary>
internal static class ShippingWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this ShippingWriteException exception, HttpContext httpContext)
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
        ShippingWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        ShippingWriteException.ErrorCodes.StoreCodeDuplicate => StatusCodes.Status409Conflict,
        ShippingWriteException.ErrorCodes.PackageLimitPeriodOverlap => StatusCodes.Status409Conflict,
        ShippingWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ShippingWriteException.ErrorCodes.ShippingBatchLimitExceeded => StatusCodes.Status400BadRequest,
        ShippingWriteException.ErrorCodes.ShippingOrderNotReady => StatusCodes.Status409Conflict,
        ShippingWriteException.ErrorCodes.ShippingTrackingDuplicate => StatusCodes.Status409Conflict,
        ShippingWriteException.ErrorCodes.ShippingMethodNotAllowed => StatusCodes.Status409Conflict,
        ShippingWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
