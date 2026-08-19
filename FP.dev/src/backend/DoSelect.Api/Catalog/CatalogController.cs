using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogController : ControllerBase
{
    private readonly ICatalogFilterOptionsService _filterOptionsService;

    public CatalogController(ICatalogFilterOptionsService filterOptionsService)
    {
        _filterOptionsService = filterOptionsService;
    }

    [HttpGet("filter-options")]
    public async Task<ActionResult<CatalogFilterOptionsDto>> GetFilterOptions(
        [FromQuery] CatalogFilterOptionsRequest request,
        CancellationToken cancellationToken)
    {
        var query = new CatalogFilterOptionsQuery(
            request.Category,
            CatalogLocaleParser.Parse(request.Locale));

        try
        {
            var result = await _filterOptionsService.GetAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (CatalogSearchException exception)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                exception.ErrorCode,
                "Filter options request is invalid",
                exception.Message);
            return BadRequest(problem);
        }
    }
}

public sealed class CatalogFilterOptionsRequest
{
    [StringLength(64)]
    public string? Category { get; init; }

    [AllowedValues("zh-TW", "ja-JP", "ko-KR", null)]
    public string? Locale { get; init; }
}
