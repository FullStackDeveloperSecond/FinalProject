using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

/// <summary>
/// API Endpoint 目錄「M 規格範本」：`GET/POST /api/v1/admin/specification-definitions`、
/// `PUT .../{id}`、`POST .../{id}/actions/disable`，CatalogManager／SuperAdmin。刪除端點刻意不存在
/// ——資料字典要求以停用代替刪除。
/// </summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.CatalogManager)]
[Route("api/v1/admin/specification-definitions")]
public sealed class AdminSpecificationDefinitionsController : ControllerBase
{
    private readonly ISpecificationDefinitionAdminService _service;

    public AdminSpecificationDefinitionsController(ISpecificationDefinitionAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<SpecificationDefinitionDto>>> List(
        [FromQuery] SpecificationDefinitionListRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new SpecificationDefinitionQuery(
                request.CategoryPublicId, request.Q, request.IsActive, request.PageNumber, request.PageSize);
            return Ok(await _service.ListAsync(query, cancellationToken));
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost]
    public async Task<ActionResult<SpecificationDefinitionDto>> Create(
        [FromBody] CreateSpecificationDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _service.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { }, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SpecificationDefinitionDto>> Update(
        Guid id,
        [FromBody] UpdateSpecificationDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.UpdateAsync(id, request, cancellationToken));
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/actions/disable")]
    public async Task<ActionResult<SpecificationDefinitionDto>> Disable(
        Guid id,
        [FromBody] DisableSpecificationDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.DisableAsync(id, request, cancellationToken));
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}

public sealed class SpecificationDefinitionListRequest
{
    public Guid? CategoryPublicId { get; init; }

    [StringLength(160)]
    public string? Q { get; init; }

    public bool? IsActive { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
