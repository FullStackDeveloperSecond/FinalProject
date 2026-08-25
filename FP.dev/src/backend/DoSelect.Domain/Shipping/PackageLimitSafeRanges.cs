namespace DoSelect.Domain.Shipping;

public static class ShippingProviderCodes
{
    public const string StorePickup = "StorePickup";
    public const string HomeDelivery = "HomeDelivery";
}

/// <summary>
/// Program-fixed safe bounds an admin's package-limit draft must fall within — not itself
/// admin-configurable. Per 購物車、訂單、付款與物流.md's 包裹限制 section: "超商與宅配使用不同 Provider
/// Profile 及不可由一般管理員突破的安全範圍". MaxTotalCm is the three-sides-sum ("三邊和"), not a
/// fourth independent dimension.
/// </summary>
public static class PackageLimitSafeRanges
{
    public static readonly PackageLimitSafeRange StorePickup = new(
        MinSideCm: 1m, MaxSideCm: 45m,
        MinTotalCm: 3m, MaxTotalCm: 105m,
        MinWeightKg: 0.1m, MaxWeightKg: 5m);

    public static readonly PackageLimitSafeRange HomeDelivery = new(
        MinSideCm: 1m, MaxSideCm: 150m,
        MinTotalCm: 3m, MaxTotalCm: 150m,
        MinWeightKg: 0.1m, MaxWeightKg: 20m);

    /// <summary>Current effective defaults, per the doc's "超商 Profile 繼續使用單邊 45 cm、三邊和 105
    /// cm、5 kg 作為目前有效預設" — the store-pickup ceiling doubles as its own default.</summary>
    public static readonly PackageLimitDraft StorePickupDefault = new(45m, 45m, 45m, 105m, 5m);

    public static PackageLimitSafeRange ForProvider(string providerCode) => providerCode switch
    {
        ShippingProviderCodes.StorePickup => StorePickup,
        ShippingProviderCodes.HomeDelivery => HomeDelivery,
        _ => throw new ArgumentOutOfRangeException(nameof(providerCode), providerCode, "Unknown shipping provider code."),
    };
}

public sealed record PackageLimitSafeRange(
    decimal MinSideCm,
    decimal MaxSideCm,
    decimal MinTotalCm,
    decimal MaxTotalCm,
    decimal MinWeightKg,
    decimal MaxWeightKg);

public sealed record PackageLimitDraft(
    decimal MaxLengthCm,
    decimal MaxWidthCm,
    decimal MaxHeightCm,
    decimal MaxTotalCm,
    decimal MaxWeightKg);
