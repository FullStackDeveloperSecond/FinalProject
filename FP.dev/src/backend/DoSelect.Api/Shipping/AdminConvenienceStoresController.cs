using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>UC-ADM-STORE-01. ConvenienceStoreView (OrderManager／CatalogManager／SuperAdmin) covers reads; writes additionally require OrderManager so CatalogManager stays read-only, per 購物車、訂單、付款與物流.md §超商門市維護.</summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.ConvenienceStoreView)]
[Route("api/v1/admin/convenience-stores")]
public sealed class AdminConvenienceStoresController : ControllerBase
{
    private readonly IConvenienceStoreAdminService _adminService;

    public AdminConvenienceStoresController(IConvenienceStoreAdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<ConvenienceStoreDto>>> List(
        [FromQuery] AdminConvenienceStoreListRequest request, CancellationToken cancellationToken)
    {
        var query = new ConvenienceStoreAdminQuery(
            request.Q, request.City, request.District, request.IsActive, request.PageNumber, request.PageSize);
        return Ok(await _adminService.ListAsync(query, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = DoSelectPolicies.OrderManager)]
    public async Task<ActionResult<ConvenienceStoreDto>> Create(
        [FromBody] CreateConvenienceStoreRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _adminService.CreateAsync(request, DateTime.UtcNow, cancellationToken);
            return CreatedAtAction(nameof(List), new { }, dto);
        }
        catch (ShippingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = DoSelectPolicies.OrderManager)]
    public async Task<ActionResult<ConvenienceStoreDto>> Update(
        Guid id, [FromBody] UpdateConvenienceStoreRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.UpdateAsync(id, request, DateTime.UtcNow, cancellationToken));
        }
        catch (ShippingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}

public sealed class AdminConvenienceStoreListRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    [StringLength(60)]
    public string? City { get; init; }

    [StringLength(60)]
    public string? District { get; init; }

    public bool? IsActive { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
