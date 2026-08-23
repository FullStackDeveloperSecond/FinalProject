using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

[ApiController]
[Authorize(Policy = DoSelectPolicies.CatalogManager)]
[Route("api/v1/admin/tags")]
public sealed class TagsController : ControllerBase
{
    private readonly ITagAdminService _tagAdminService;

    public TagsController(ITagAdminService tagAdminService)
    {
        _tagAdminService = tagAdminService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<CatalogLookupDto>>> List(
        [FromQuery] CatalogLookupListRequest request,
        CancellationToken cancellationToken)
    {
        var query = new CatalogLookupQuery(request.Q, request.IsActive, request.PageNumber, request.PageSize);
        var result = await _tagAdminService.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CatalogLookupDto>> Create(
        [FromBody] CreateTagRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _tagAdminService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { }, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CatalogLookupDto>> Update(
        Guid id,
        [FromBody] UpdateTagRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _tagAdminService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}
