using DoSelect.Application.Shipping;
using DoSelect.Application.Shopping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// The authoritative COD check — haru's order-creation Use Case calls this directly instead of
/// re-deriving eligibility from an earlier <see cref="ShippingOptionsDto"/> read, since the cart
/// may have changed between the shipping-options page and the moment the order is submitted.
/// </summary>
public sealed class EfCodEligibilityService : ICodEligibilityService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly ICartService _cartService;

    public EfCodEligibilityService(DoSelectDbContext dbContext, ICartService cartService)
    {
        _dbContext = dbContext;
        _cartService = cartService;
    }

    public async Task<CodEligibilityResult> EvaluateAsync(
        CartIdentity identity,
        string shippingMethodCode,
        CancellationToken cancellationToken)
    {
        var method = await _dbContext.ShippingMethods
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Code == shippingMethodCode, cancellationToken);
        if (method is null || !method.IsActive)
        {
            return new CodEligibilityResult(false, ShippingErrorCodes.ShippingMethodNotAllowed);
        }

        var cart = await _cartService.GetCartAsync(identity, cancellationToken);
        var contents = await CartContentsInspector.InspectAsync(_dbContext, cart, cancellationToken);

        var fee = method.FreeShippingThreshold.HasValue
            && cart.Amounts.Subtotal - cart.Amounts.ItemDiscount - cart.Amounts.CouponDiscount >= method.FreeShippingThreshold.Value
                ? 0m
                : method.BaseFee;
        var finalPayableAmount = cart.Amounts.TotalEstimate + fee;

        return CodEligibilityRules.Evaluate(
            method,
            finalPayableAmount,
            contents.HasAssemblyItem,
            contents.HasPrepaymentRequiredSku);
    }
}
