using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>UC-ADM-STORE-01. No delete action — see IConvenienceStoreAdminService's remarks.</summary>
[ApiController]
[Route("api/v1/admin/convenience-stores")]
public sealed class AdminConvenienceStoresController : ControllerBase
{
    private readonly IConvenienceStoreAdminService _convenienceStoreAdminService;

    public AdminConvenienceStoresController(IConvenienceStoreAdminService convenienceStoreAdminService)
    {
        _convenienceStoreAdminService = convenienceStoreAdminService;
    }

    [HttpGet]
    [Authorize(Policy = DoSelectPolicies.ShippingRead)]
    public async Task<ActionResult<PageResult<ConvenienceStoreDto>>> List(
        [FromQuery] AdminListConvenienceStoresRequest request,
        CancellationToken cancellationToken)
    {
        var query = new AdminConvenienceStoreQuery(
            request.ProviderCode, request.City, request.District, request.IsActive, request.PageNumber, request.PageSize);
        return Ok(await _convenienceStoreAdminService.ListAsync(query, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = DoSelectPolicies.ShippingManage)]
    public async Task<ActionResult<ConvenienceStoreDto>> Create(
        [FromBody] CreateConvenienceStoreRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var created = await _convenienceStoreAdminService.CreateAsync(request, actorUserId, cancellationToken);
            return CreatedAtAction(nameof(List), new { }, created);
        }
        catch (ShippingAdminWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = DoSelectPolicies.ShippingManage)]
    public async Task<ActionResult<ConvenienceStoreDto>> Update(
        Guid id,
        [FromBody] UpdateConvenienceStoreRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var updated = await _convenienceStoreAdminService.UpdateAsync(id, request, actorUserId, cancellationToken);
            return Ok(updated);
        }
        catch (ShippingAdminWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}

public sealed class AdminListConvenienceStoresRequest
{
    [StringLength(64)]
    public string? ProviderCode { get; init; }

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
