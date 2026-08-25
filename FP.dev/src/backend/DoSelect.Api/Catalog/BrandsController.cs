using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

[ApiController]
[Authorize(Policy = DoSelectPolicies.CatalogManager)]
[Route("api/v1/admin/brands")]
public sealed class BrandsController : ControllerBase
{
    private readonly IBrandAdminService _brandAdminService;

    public BrandsController(IBrandAdminService brandAdminService)
    {
        _brandAdminService = brandAdminService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<BrandDto>>> List(
        [FromQuery] CatalogLookupListRequest request,
        CancellationToken cancellationToken)
    {
        var query = new CatalogLookupQuery(request.Q, request.IsActive, request.PageNumber, request.PageSize);
        var result = await _brandAdminService.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<BrandDto>> Create(
        [FromBody] CreateBrandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _brandAdminService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { }, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BrandDto>> Update(
        Guid id,
        [FromBody] UpdateBrandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _brandAdminService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}

public sealed class CatalogLookupListRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    public bool? IsActive { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
