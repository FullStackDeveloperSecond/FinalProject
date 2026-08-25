using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Api.Shopping;
using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using DoSelect.Application.Shopping;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>
/// Both actions are anonymous-or-member, same identity resolution convention as
/// <see cref="CartController"/> — shipping options are always asked "for my current cart",
/// guest or signed in.
/// </summary>
[ApiController]
public sealed class ShippingController : ControllerBase
{
    private readonly IShippingOptionsService _shippingOptionsService;
    private readonly IConvenienceStoreQueryService _convenienceStoreQueryService;

    public ShippingController(
        IShippingOptionsService shippingOptionsService,
        IConvenienceStoreQueryService convenienceStoreQueryService)
    {
        _shippingOptionsService = shippingOptionsService;
        _convenienceStoreQueryService = convenienceStoreQueryService;
    }

    [HttpGet("api/v1/cart/shipping-options")]
    public async Task<ActionResult<ShippingOptionsDto>> GetShippingOptions(CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ShoppingWriteException.ErrorCodes.ValidationFailed,
                detail: $"A member session or the '{CartIdentityResolver.GuestCartKeyHeaderName}' header is required.");
            return BadRequest(problem);
        }

        var options = await _shippingOptionsService.GetOptionsForCartAsync(identity, cancellationToken);
        return Ok(options);
    }

    [HttpGet("api/v1/convenience-stores")]
    public async Task<ActionResult<PageResult<ConvenienceStoreOptionDto>>> ListConvenienceStores(
        [FromQuery] ListConvenienceStoresRequest request,
        CancellationToken cancellationToken)
    {
        var query = new ConvenienceStoreQuery(
            request.ProviderCode,
            request.City,
            request.District,
            request.Q,
            request.PageNumber,
            request.PageSize);
        var result = await _convenienceStoreQueryService.ListAsync(query, cancellationToken);
        return Ok(result);
    }
}

public sealed class ListConvenienceStoresRequest
{
    [StringLength(32)]
    public string? ProviderCode { get; init; }

    [StringLength(64)]
    public string? City { get; init; }

    [StringLength(64)]
    public string? District { get; init; }

    [StringLength(160)]
    public string? Q { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
