namespace DoSelect.Infrastructure.Persistence.Seeding;

public static class DemoSeedManifest
{
    public const string Version = "implemented-features-v1";
    public const int RandomSeed = 20260907;

    public static readonly DateTime PeriodStartUtc =
        new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime PeriodEndUtc =
        new(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc);

    public const int Members = 600;
    public const int Addresses = 500;
    public const int Products = 250;
    public const int Skus = 750;
    public const int ProductSpecificationValues = 1_600;
    public const int Orders = 650;
    public const int OrderItems = 1_600;
    public const int PaymentAttempts = 700;
    public const int Shipments = 550;
    public const int InventoryMovements = 800;
    public const int SupportTickets = 250;
    public const int SupportMessages = 900;
    public const int ReturnRequests = 150;
    public const int Refunds = 120;
    public const int ProductReviews = 250;
    public const int Favorites = 200;
    public const int Coupons = 30;
    public const int CouponRedemptions = 100;
    public const int AiSearchFunnelEvents = 0;

    public const int MainBusinessRecordTotal =
        Members + Addresses + Products + Skus + ProductSpecificationValues +
        Orders + OrderItems + PaymentAttempts + Shipments + InventoryMovements +
        SupportTickets + SupportMessages + ReturnRequests + Refunds + ProductReviews +
        Favorites + Coupons + CouponRedemptions + AiSearchFunnelEvents;

    public static IReadOnlyDictionary<string, int> ExpectedCounts { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["members"] = Members,
            ["addresses"] = Addresses,
            ["products"] = Products,
            ["skus"] = Skus,
            ["productSpecificationValues"] = ProductSpecificationValues,
            ["orders"] = Orders,
            ["orderItems"] = OrderItems,
            ["paymentAttempts"] = PaymentAttempts,
            ["shipments"] = Shipments,
            ["inventoryMovements"] = InventoryMovements,
            ["supportTickets"] = SupportTickets,
            ["supportMessages"] = SupportMessages,
            ["returnRequests"] = ReturnRequests,
            ["refunds"] = Refunds,
            ["productReviews"] = ProductReviews,
            ["favorites"] = Favorites,
            ["couponsAndRedemptions"] = Coupons + CouponRedemptions,
            ["aiSearchFunnelEvents"] = AiSearchFunnelEvents,
        };
}

public sealed record DemoSeedResult(
    string FeatureProfile,
    int RandomSeed,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int MainBusinessRecordTotal,
    bool Created,
    IReadOnlyDictionary<string, int> Counts);
