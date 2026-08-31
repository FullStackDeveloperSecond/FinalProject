namespace DoSelect.Application.Shipping;

/// <summary>
/// Checkout-facing shipping option — BaseFee/FreeShippingThreshold are for display only; Checkout
/// always re-resolves and re-prices the method server-side from ShippingMethod.Code, never trusts
/// a client-echoed fee.
/// </summary>
public sealed record ShippingMethodOptionDto(
    string Code,
    string NameZhTw,
    string Kind,
    decimal BaseFee,
    decimal? FreeShippingThreshold,
    bool AllowsCod,
    bool RequiresPrepayment);

public sealed record ShippingOptionsDto(IReadOnlyList<ShippingMethodOptionDto> Methods);

public interface IShippingOptionsReader
{
    Task<ShippingOptionsDto> GetActiveOptionsAsync(CancellationToken cancellationToken);
}
