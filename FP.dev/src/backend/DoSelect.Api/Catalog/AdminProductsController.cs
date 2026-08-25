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
[Route("api/v1/admin/products")]
public sealed class AdminProductsController : ControllerBase
{
    private readonly IProductAdminService _productAdminService;

    public AdminProductsController(IProductAdminService productAdminService)
    {
        _productAdminService = productAdminService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<AdminProductSummaryDto>>> List(
        [FromQuery] AdminProductListRequest request,
        CancellationToken cancellationToken)
    {
        var query = new AdminProductQuery(
            request.Q,
            request.BrandCodes,
            request.CategoryCodes,
            request.Statuses,
            request.StockState,
            request.Sort,
            request.PageNumber,
            request.PageSize);

        try
        {
            var result = await _productAdminService.ListAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminProductDetailDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await _productAdminService.GetByPublicIdAsync(id, cancellationToken);
        if (detail is null)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound);
            return NotFound(problem);
        }

        return Ok(detail);
    }

    [HttpPost]
    public async Task<ActionResult<AdminProductDetailDto>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _productAdminService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.PublicId }, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminProductDetailDto>> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _productAdminService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}

public sealed class AdminProductListRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? BrandCodes { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? CategoryCodes { get; init; }

    [MaxLength(10)]
    public IReadOnlyList<string>? Statuses { get; init; }

    public string? StockState { get; init; }

    public string? Sort { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
