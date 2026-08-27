namespace DoSelect.Application.Checkout;

/// <summary>
/// Authoritative policy versions currently offered by Checkout. Values are configuration-owned
/// and validated during application startup so the API never accepts an unknown default version.
/// </summary>
public sealed class CheckoutPolicyOptions
{
    public const string SectionName = "CheckoutPolicy";

    public int TermsVersion { get; set; }
    public int ReturnVersion { get; set; }
    public int PrivacyVersion { get; set; }
    public int ShippingConstraintVersion { get; set; }
}

public sealed record CheckoutPolicySnapshot(
    int Terms,
    int Return,
    int Privacy,
    int ShippingConstraint);

public interface ICheckoutPolicyProvider
{
    CheckoutPolicySnapshot Current { get; }
}
