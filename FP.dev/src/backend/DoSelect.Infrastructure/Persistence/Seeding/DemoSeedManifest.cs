namespace DoSelect.Infrastructure.Persistence.Seeding;

public static class DemoSeedManifest
{
    public const string Version = "implemented-features-v3";
    public const string MarkerBrandCode = "DEMO-V3-BRAND-001";
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

    public static IReadOnlyDictionary<string, int> ExpectedDistributionCounts { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["completedOrders"] = 250,
            ["cancelledOrders"] = 100,
            ["ordersWithExpiredPayment"] = 75,
            ["cancelledOrExpiredOrders"] = 125,
            ["refundRecords"] = 120,
            ["succeededRefunds"] = 100,
            ["failedPaymentAttempts"] = 100,
            ["expiredPaymentAttempts"] = 100,
            ["lowStockPublishedSkus"] = 100,
            ["pendingShipments"] = 100,
            ["preparingShipments"] = 50,
            ["inTransitShipments"] = 150,
            ["deliveredShipments"] = 250,
            ["openSupportTickets"] = 50,
            ["inProgressSupportTickets"] = 100,
            ["waitingForCustomerSupportTickets"] = 50,
            ["closedSupportTickets"] = 50,
            ["awaitingRefundReturnRequests"] = 20,
            ["awaitingShipmentReturnRequests"] = 30,
            ["completedReturnRequests"] = 100,
            ["pendingReviewProductReviews"] = 50,
            ["approvedProductReviews"] = 200,
        };

    public static IReadOnlyDictionary<string, decimal?> ExpectedReportBaselines { get; } =
        new Dictionary<string, decimal?>(StringComparer.Ordinal)
        {
            ["sales-overview.paid_amount"] = 1_350_000.00m,
            ["sales-overview.refund_amount"] = 100_000.00m,
            ["sales-overview.net_revenue"] = 1_250_000.00m,
            ["sales-overview.order_count"] = 500m,
            ["sales-overview.average_order_value"] = 2_500.00m,
            ["sales-overview.refund_amount_rate"] = 0.0740740740740740740740740741m,
            ["sales-overview.cancellation_rate"] = 0.1538461538461538461538461538m,
            ["sales-overview.payment_method_credit_card_share"] = 1m,
            ["product-abc.net_revenue"] = 650_000.00m,
            ["product-abc.quantity"] = 650m,
            ["product-abc.sku_count"] = 650m,
            ["period-comparison.net_revenue"] = 1_250_000.00m,
            ["period-comparison.paid_amount"] = 1_350_000.00m,
            ["period-comparison.refund_amount"] = 100_000.00m,
            ["period-comparison.order_count"] = 500m,
            ["period-comparison.average_order_value"] = 2_500.00m,
            ["inventory-turnover.cost_of_goods_sold"] = 455_000.0000m,
            ["inventory-turnover.sku_count"] = 750m,
            ["inventory-turnover.low_stock_count"] = 100m,
            ["inventory-turnover.out_of_stock_count"] = 0m,
            ["inventory-turnover.long_term_unsold_count"] = 368m,
            ["inventory-turnover.insufficient_data_count"] = 750m,
            ["gross-margin.net_revenue"] = 650_000.00m,
            ["gross-margin.cost_of_goods_sold"] = 455_000.0000m,
            ["gross-margin.gross_profit"] = 195_000.0000m,
            ["gross-margin.gross_margin_rate"] = 0.30m,
            ["gross-margin.quantity_sold"] = 650m,
            ["gross-margin.refunded_quantity"] = 100m,
            ["product-associations.completed_order_count"] = 250m,
            ["product-associations.directional_rule_count"] = 0m,
            ["forecast-anomalies.observed_days"] = 30m,
            ["forecast-anomalies.forecast_days"] = 7m,
            ["forecast-anomalies.anomaly_count"] = 1m,
            ["forecast-anomalies.slope"] = -0.234705228031146m,
        };
}

public sealed record DemoSeedResult(
    string FeatureProfile,
    int RandomSeed,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int MainBusinessRecordTotal,
    bool Created,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, int> Distribution);
