using DoSelect.Api.Common;
using DoSelect.Application.Favorites;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Favorites;

internal static class FavoriteWriteExceptionMapper
{
    public static ActionResult ToActionResult(
        this FavoriteWriteException exception,
        HttpContext httpContext)
    {
        var status = exception.Code switch
        {
            FavoriteWriteException.ErrorCodes.ProductNotFound => StatusCodes.Status404NotFound,
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
