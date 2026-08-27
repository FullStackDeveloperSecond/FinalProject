using DoSelect.Application.Checkout;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Checkout;

internal sealed class CheckoutPolicyProvider(IOptions<CheckoutPolicyOptions> options)
    : ICheckoutPolicyProvider
{
    public CheckoutPolicySnapshot Current
    {
        get
        {
            var current = options.Value;
            return new CheckoutPolicySnapshot(
                current.TermsVersion,
                current.ReturnVersion,
                current.PrivacyVersion,
                current.ShippingConstraintVersion);
        }
    }
}
