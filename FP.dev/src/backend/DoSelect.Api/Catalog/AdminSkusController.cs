using DoSelect.Api.Common;
using DoSelect.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

// TODO(catalog-search): add [Authorize(Policy = "CatalogManager")] once alex's
// shared Cookie/Policy work package registers the CatalogManager policy
// (工程包 2/6 節：Policy 由 alex 的共用工作包推進，本次不得以臨時授權替代)。
[ApiController]
public sealed class AdminSkusController : ControllerBase
{
    private readonly ISkuAdminService _skuAdminService;

    public AdminSkusController(ISkuAdminService skuAdminService)
    {
        _skuAdminService = skuAdminService;
    }

    [HttpPost("api/v1/admin/products/{productId:guid}/skus")]
    public async Task<ActionResult<SkuDto>> Create(
        Guid productId,
        [FromBody] CreateSkuRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _skuAdminService.CreateAsync(productId, request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.PublicId }, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("api/v1/admin/skus/{id:guid}")]
    public async Task<ActionResult<SkuDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var sku = await _skuAdminService.GetByPublicIdAsync(id, cancellationToken);
        if (sku is null)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound);
            return NotFound(problem);
        }

        return Ok(sku);
    }

    [HttpPut("api/v1/admin/skus/{id:guid}")]
    public async Task<ActionResult<SkuDto>> Update(
        Guid id,
        [FromBody] UpdateSkuRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _skuAdminService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpDelete("api/v1/admin/skus/{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromBody] DeleteSkuRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _skuAdminService.DeleteAsync(id, request.RowVersion, cancellationToken);
            return NoContent();
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}

/// <summary>
/// No documented transport convention exists for RowVersion on DELETE requests
/// (checked 03-架構/API DTO與Schema契約.md and API共通規範.md — neither specifies
/// an If-Match header or similar). Chosen to match every other write endpoint in
/// this module, which carries RowVersion in the request body; revisit if kafen's
/// review or a future common-API decision says otherwise.
/// </summary>
public sealed record DeleteSkuRequest(byte[] RowVersion);
