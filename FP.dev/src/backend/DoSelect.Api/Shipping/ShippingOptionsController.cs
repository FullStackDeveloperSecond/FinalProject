using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>
/// Public read supporting Cart／Checkout's shipping method picker (「M 配送選項支撐」in the API
/// Endpoint 目錄). Deliberately its own controller rather than an action on CartController — this
/// read has no cart identity, and Cart itself belongs to a different module's ownership boundary.
/// </summary>
[ApiController]
[Route("api/v1/cart")]
public sealed class ShippingOptionsController : ControllerBase
{
    private readonly IShippingOptionsReader _reader;

    public ShippingOptionsController(IShippingOptionsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    [HttpGet("shipping-options")]
    public async Task<ActionResult<ShippingOptionsDto>> GetShippingOptions(CancellationToken cancellationToken)
    {
        var options = await _reader.GetActiveOptionsAsync(cancellationToken);
        return Ok(options);
    }
}
