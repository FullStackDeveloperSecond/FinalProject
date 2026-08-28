using DoSelect.Application.Builds;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>
/// Maps <see cref="BuildWriteException"/> error codes to HTTP status codes, mirroring
/// <see cref="ShoppingWriteExceptionMapper"/>'s shape so Builds controllers stay thin adapters.
/// </summary>
internal static class BuildWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this BuildWriteException exception, HttpContext httpContext)
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
        BuildWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        BuildWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        BuildWriteException.ErrorCodes.BuildIncompatible => StatusCodes.Status409Conflict,
        BuildWriteException.ErrorCodes.BuildUnavailableItem => StatusCodes.Status409Conflict,
        BuildWriteException.ErrorCodes.InventoryInsufficient => StatusCodes.Status409Conflict,
        BuildWriteException.ErrorCodes.BuildIncomplete => StatusCodes.Status400BadRequest,
        BuildWriteException.ErrorCodes.CompatibilityThresholdOutOfRange => StatusCodes.Status400BadRequest,
        BuildWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
