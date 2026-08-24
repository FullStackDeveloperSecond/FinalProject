using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>M 配送選項支撐 — Public Cart／Member, no [Authorize]. Two unrelated route prefixes (cart-scoped vs top-level) live in one controller since both are the same read-only shipping-options surface.</summary>
[ApiController]
public sealed class ShippingOptionsController : ControllerBase
{
    private readonly IShippingOptionsQueryService _queryService;

    public ShippingOptionsController(IShippingOptionsQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet("api/v1/cart/shipping-options")]
    public async Task<ActionResult<ShippingOptionsDto>> GetShippingOptions(CancellationToken cancellationToken) =>
        Ok(await _queryService.GetShippingOptionsAsync(cancellationToken));

    [HttpGet("api/v1/convenience-stores")]
    public async Task<ActionResult<PageResult<ConvenienceStoreOptionDto>>> SearchConvenienceStores(
        [FromQuery] ConvenienceStoreSearchRequest request, CancellationToken cancellationToken)
    {
        var query = new ConvenienceStoreQuery(
            request.Q, request.City, request.District, request.PageNumber, request.PageSize);
        return Ok(await _queryService.SearchConvenienceStoresAsync(query, cancellationToken));
    }
}

public sealed class ConvenienceStoreSearchRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    [StringLength(60)]
    public string? City { get; init; }

    [StringLength(60)]
    public string? District { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
