using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// Pure COD go/no-go decision, shared by <see cref="EfShippingOptionsService"/> (to populate
/// <c>ShippingOptionDto.AllowedPaymentMethods</c>) and <see cref="EfCodEligibilityService"/>
/// (the authoritative check haru's checkout re-runs at order-creation time). Per 購物車、訂單、
/// 付款與物流.md's 貨到付款 section: capped at NT$20,000 final payable, and blocked outright by
/// an assembly item or any SKU with RequiresPrepayment — regardless of amount.
/// </summary>
internal static class CodEligibilityRules
{
    internal const decimal MaxCodAmount = 20_000m;

    internal static CodEligibilityResult Evaluate(
        ShippingMethod method,
        decimal finalPayableAmount,
        bool cartHasAssemblyItem,
        bool cartHasPrepaymentRequiredSku)
    {
        if (!method.AllowsCod)
        {
            return new CodEligibilityResult(false, "payment_method_not_allowed");
        }

        if (cartHasAssemblyItem || cartHasPrepaymentRequiredSku)
        {
            return new CodEligibilityResult(false, "payment_cod_restricted_item");
        }

        if (finalPayableAmount > MaxCodAmount)
        {
            return new CodEligibilityResult(false, "payment_cod_amount_exceeded");
        }

        return new CodEligibilityResult(true, null);
    }
}
