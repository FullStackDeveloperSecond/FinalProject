using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>Mirrors <see cref="ShoppingWriteExceptionMapper"/>'s shape for the shipping-admin controllers.</summary>
internal static class ShippingAdminWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this ShippingAdminWriteException exception, HttpContext httpContext)
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
        ShippingAdminErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        ShippingAdminErrorCodes.PackageLimitPeriodOverlap => StatusCodes.Status409Conflict,
        ShippingAdminErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ShippingAdminErrorCodes.StoreCodeDuplicate => StatusCodes.Status409Conflict,
        ShippingAdminErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
