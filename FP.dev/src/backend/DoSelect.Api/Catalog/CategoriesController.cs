using DoSelect.Api.Common;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

// TODO(catalog-search): add [Authorize(Policy = "CatalogManager")] once alex's
// shared Cookie/Policy work package registers the CatalogManager policy
// (工程包 2/6 節：Policy 由 alex 的共用工作包推進，本次不得以臨時授權替代)。
[ApiController]
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
