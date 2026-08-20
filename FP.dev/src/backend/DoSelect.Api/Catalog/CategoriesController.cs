using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

[ApiController]
[Authorize(Policy = DoSelectPolicies.CatalogManager)]
[Route("api/v1/admin/categories")]
public sealed class CategoriesController : ControllerBase
{
    private readonly ICategoryAdminService _categoryAdminService;

    public CategoriesController(ICategoryAdminService categoryAdminService)
    {
        _categoryAdminService = categoryAdminService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<CategoryDto>>> List(
        [FromQuery] CatalogLookupListRequest request,
        CancellationToken cancellationToken)
    {
        var query = new CatalogLookupQuery(request.Q, request.IsActive, request.PageNumber, request.PageSize);
        var result = await _categoryAdminService.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _categoryAdminService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { }, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _categoryAdminService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}
