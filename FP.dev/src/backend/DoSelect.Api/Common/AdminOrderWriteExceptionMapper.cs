using DoSelect.Application.Orders;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

/// <summary>
/// Maps <see cref="AdminOrderWriteException"/> error codes to the HTTP status codes
/// registered in 03-架構/API錯誤碼目錄.md. Shared by AdminOrdersController so it stays a
/// thin adapter with no duplicated status-code logic (mirrors CatalogWriteExceptionMapper).
/// </summary>
internal static class AdminOrderWriteExceptionMapper
{
    public static ObjectResult ToActionResult(this AdminOrderWriteException exception, HttpContext httpContext)
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
        AdminOrderWriteException.ErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        AdminOrderWriteException.ErrorCodes.OrderStateConflict => StatusCodes.Status409Conflict,
        AdminOrderWriteException.ErrorCodes.OrderCancellationNotAllowed => StatusCodes.Status409Conflict,
        AdminOrderWriteException.ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        AdminOrderWriteException.ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}
