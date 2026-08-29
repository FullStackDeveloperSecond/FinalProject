using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.OperationalReports;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.OperationalReports;

public sealed class OperationalReportSqlFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable = "DOSELECT_SQLSERVER_TEST_CONNECTION";

    private const string DatabaseName = "DoSelectOperationalReportTests";
    private const string LocalServer = "Server=.\\SQL2025;";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(GetConfiguredConnectionString()) ||
        OperatingSystem.IsWindows() &&
        !string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context);
    }

    public async Task DisposeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(BuildConnectionString())
            .Options);

    private static async Task SeedAsync(DoSelectDbContext context)
    {
        var createdAtUtc = new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc);
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(),
            $"operational-report-{Guid.NewGuid():N}@example.test",
            createdAtUtc);
        context.Users.Add(admin);

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(),
            "REPORT-HOME",
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            createdAtUtc);
        context.Set<ShippingProviderProfile>().Add(shippingProfile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(),
            shippingProfile.Id,
            1,
            20m,
            100m,
            100m,
            100m,
            200m,
            100_000m,
            null,
            null,
            createdAtUtc);
        context.Set<PackageLimitVersion>().Add(packageLimit);
        await context.SaveChangesAsync();

        var paidOrder = Order.Create(
            Guid.CreateVersion7(),
            OrderCreationFor(
                "DS-REPORT-PAID",
                1_000m,
                OrderStatus.Processing,
                shippingProfile.Id,
                packageLimit.Id),
            createdAtUtc);
        var cancelledOrder = Order.Create(
            Guid.CreateVersion7(),
            OrderCreationFor(
                "DS-REPORT-CANCELLED",
                500m,
                OrderStatus.Confirmed,
                shippingProfile.Id,
                packageLimit.Id),
            new DateTime(2026, 8, 3, 2, 0, 0, DateTimeKind.Utc));
        cancelledOrder.ChangeOrderStatus(
            OrderStatus.Cancelled,
            new DateTime(2026, 8, 3, 3, 0, 0, DateTimeKind.Utc));
        context.Orders.AddRange(paidOrder, cancelledOrder);
        await context.SaveChangesAsync();

        var payment = new PaymentAttempt(
            Guid.CreateVersion7(),
            paidOrder.Id,
            PaymentMethod.CreditCard,
            1_000m,
            "DEMO",
            "report-payment",
            null,
            createdAtUtc);
        payment.SetPaymentInstruction("report-payment-reference", createdAtUtc.AddMinutes(1));
        payment.Transition(PaymentAttemptStatus.Processing, createdAtUtc.AddMinutes(2));
        payment.Transition(
            PaymentAttemptStatus.Paid,
            new DateTime(2026, 8, 1, 17, 0, 0, DateTimeKind.Utc));
        context.PaymentAttempts.Add(payment);
        await context.SaveChangesAsync();

        var refund = new Refund(
            Guid.CreateVersion7(),
            paidOrder.Id,
            returnRequestId: null,
            payment.Id,
            "RF-REPORT-1",
            200m,
            "AcceptedReturn",
            admin.Id,
            "report-refund",
            new DateTime(2026, 8, 4, 1, 0, 0, DateTimeKind.Utc));
        refund.Approve(200m, admin.Id, new DateTime(2026, 8, 4, 1, 1, 0, DateTimeKind.Utc));
        refund.BeginProcessing(admin.Id, new DateTime(2026, 8, 4, 1, 2, 0, DateTimeKind.Utc));
        refund.Complete(200m, new DateTime(2026, 8, 4, 1, 3, 0, DateTimeKind.Utc));
        context.Refunds.Add(refund);
        await context.SaveChangesAsync();

        await SeedWp002ReportsAsync(
            context,
            shippingProfile.Id,
            packageLimit.Id,
            admin.Id);
    }

    private static async Task SeedWp002ReportsAsync(
        DoSelectDbContext context,
        long shippingProfileId,
        long packageLimitId,
        string adminUserId)
    {
        var catalogCreatedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var brand = new Brand(
            Guid.CreateVersion7(),
            "REPORT-BRAND",
            "報表品牌",
            catalogCreatedAtUtc);
        var category = new Category(
            Guid.CreateVersion7(),
            "REPORT-CAT",
            "report-category",
            "報表分類",
            null,
            catalogCreatedAtUtc);
        var excludedCategory = new Category(
            Guid.CreateVersion7(),
            "OTHER-CAT",
            "other-category",
            "排除分類",
            null,
            catalogCreatedAtUtc);
        context.AddRange(brand, category, excludedCategory);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.CreateVersion7(),
            "REPORT-PRODUCT",
            brand.Id,
            category.Id,
            "報表商品",
            catalogCreatedAtUtc);
        var excludedProduct = new Product(
            Guid.CreateVersion7(),
            "OTHER-PRODUCT",
            brand.Id,
            excludedCategory.Id,
            "排除商品",
            catalogCreatedAtUtc);
        context.Products.AddRange(product, excludedProduct);
        await context.SaveChangesAsync();

        var skuA = new Sku(
            Guid.CreateVersion7(), "REPORT-A", product.Id, "報表 A", 900m, 999m, catalogCreatedAtUtc);
        var skuB = new Sku(
            Guid.CreateVersion7(), "REPORT-B", product.Id, "報表 B", 150m, 888m, catalogCreatedAtUtc);
        var skuC = new Sku(
            Guid.CreateVersion7(), "REPORT-C", product.Id, "報表 C", 50m, 777m, catalogCreatedAtUtc);
        var excludedSku = new Sku(
            Guid.CreateVersion7(),
            "OTHER-A",
            excludedProduct.Id,
            "排除 SKU",
            5_000m,
            1m,
            catalogCreatedAtUtc);
        skuA.ChangeStatus(SkuStatus.Published, catalogCreatedAtUtc);
        skuB.ChangeStatus(SkuStatus.Published, catalogCreatedAtUtc);
        skuC.ChangeStatus(SkuStatus.Published, catalogCreatedAtUtc);
        excludedSku.ChangeStatus(SkuStatus.Published, catalogCreatedAtUtc);
        context.Skus.AddRange(skuA, skuB, skuC, excludedSku);
        await context.SaveChangesAsync();

        var previousOrder = CreateCompletedOrder(
            "DS-REPORT-PREVIOUS",
            500m,
            new DateTime(2026, 8, 27, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 28, 1, 0, 0, DateTimeKind.Utc),
            shippingProfileId,
            packageLimitId);
        var currentOrder = CreateCompletedOrder(
            "DS-REPORT-CURRENT",
            1_100m,
            new DateTime(2026, 9, 1, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 2, 1, 0, 0, DateTimeKind.Utc),
            shippingProfileId,
            packageLimitId);
        var excludedOrder = CreateCompletedOrder(
            "DS-REPORT-EXCLUDED",
            5_000m,
            new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc),
            shippingProfileId,
            packageLimitId);
        context.Orders.AddRange(previousOrder, currentOrder, excludedOrder);
        await context.SaveChangesAsync();

        var currentItemA = ReportOrderItem(
            currentOrder.Id, skuA, quantity: 2, lineTotal: 900m, unitCostSnapshot: 300m);
        context.OrderItems.AddRange(
            ReportOrderItem(
                previousOrder.Id, skuA, quantity: 1, lineTotal: 500m, unitCostSnapshot: 250m),
            currentItemA,
            ReportOrderItem(
                currentOrder.Id, skuB, quantity: 1, lineTotal: 150m, unitCostSnapshot: 100m),
            ReportOrderItem(
                currentOrder.Id, skuC, quantity: 1, lineTotal: 50m, unitCostSnapshot: 20m),
            ReportOrderItem(
                excludedOrder.Id,
                excludedSku,
                quantity: 1,
                lineTotal: 5_000m,
                unitCostSnapshot: 1m));
        await context.SaveChangesAsync();

        var previousPayment = PaidPayment(
            previousOrder.Id,
            500m,
            "report-previous-payment",
            new DateTime(2026, 8, 28, 2, 0, 0, DateTimeKind.Utc));
        var currentPayment = PaidPayment(
            currentOrder.Id,
            1_100m,
            "report-current-payment",
            new DateTime(2026, 9, 2, 2, 0, 0, DateTimeKind.Utc));
        var excludedPayment = PaidPayment(
            excludedOrder.Id,
            5_000m,
            "report-excluded-payment",
            new DateTime(2026, 9, 3, 2, 0, 0, DateTimeKind.Utc));
        context.PaymentAttempts.AddRange(previousPayment, currentPayment, excludedPayment);
        await context.SaveChangesAsync();

        var refund = new Refund(
            Guid.CreateVersion7(),
            currentOrder.Id,
            returnRequestId: null,
            currentPayment.Id,
            "RF-REPORT-WP002",
            100m,
            "AcceptedReturn",
            adminUserId,
            "report-wp002-refund",
            new DateTime(2026, 9, 4, 1, 0, 0, DateTimeKind.Utc));
        refund.Approve(
            100m,
            adminUserId,
            new DateTime(2026, 9, 4, 1, 1, 0, DateTimeKind.Utc));
        refund.BeginProcessing(
            adminUserId,
            new DateTime(2026, 9, 4, 1, 2, 0, DateTimeKind.Utc));
        refund.Complete(100m, new DateTime(2026, 9, 4, 1, 3, 0, DateTimeKind.Utc));
        context.Refunds.Add(refund);
        await context.SaveChangesAsync();

        context.RefundAllocations.Add(new RefundAllocation(
            Guid.CreateVersion7(),
            refund.Id,
            currentItemA.Id,
            RefundAllocationType.ItemRefund,
            100m,
            originalDiscountAllocation: 0m,
            new DateTime(2026, 9, 4, 1, 3, 0, DateTimeKind.Utc),
            quantity: 1));
        await context.SaveChangesAsync();

        await SeedWp003InventoryAsync(context, skuA, skuB);
        await SeedWp003AssociationsAsync(
            context,
            skuA,
            skuB,
            skuC,
            shippingProfileId,
            packageLimitId);
        await SeedWp003ForecastAsync(
            context,
            skuC,
            shippingProfileId,
            packageLimitId);
    }

    private static async Task SeedWp003InventoryAsync(
        DoSelectDbContext context,
        Sku skuA,
        Sku skuB)
    {
        context.InventoryBalances.AddRange(
            new InventoryBalance(
                Guid.CreateVersion7(), skuA.Id, onHandQuantity: 8, reorderLevel: 8,
                new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc)),
            new InventoryBalance(
                Guid.CreateVersion7(), skuB.Id, onHandQuantity: 4, reorderLevel: 1,
                new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc)));
        context.InventoryMovements.AddRange(
            new InventoryMovement(
                Guid.CreateVersion7(), skuA.Id, null, "InitialStock", 10, 0,
                0, 10, 0, 0, 100m, "initial_stock", "Sku", skuA.PublicId, null,
                new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc)),
            new InventoryMovement(
                Guid.CreateVersion7(), skuA.Id, null, "Sale", -2, 0,
                10, 8, 0, 0, 100m, "completed_sale", "Order", null, null,
                new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc)),
            new InventoryMovement(
                Guid.CreateVersion7(), skuA.Id, null, "CostChange", 0, 0,
                8, 8, 0, 0, 120m, "sku_unit_cost_changed", "Sku", skuA.PublicId, null,
                new DateTime(2026, 9, 5, 1, 0, 0, DateTimeKind.Utc)));
        await context.SaveChangesAsync();
    }

    private static async Task SeedWp003AssociationsAsync(
        DoSelectDbContext context,
        Sku skuA,
        Sku skuB,
        Sku skuC,
        long shippingProfileId,
        long packageLimitId)
    {
        var orders = new List<Order>();
        for (var index = 0; index < 10; index++)
        {
            var completedAtUtc = new DateTime(2026, 9, 10 + index, 1, 0, 0, DateTimeKind.Utc);
            orders.Add(CreateCompletedOrder(
                $"DS-ASSOC-{index:00}",
                index < 5 ? 100m : 50m,
                completedAtUtc.AddHours(-1),
                completedAtUtc,
                shippingProfileId,
                packageLimitId));
        }

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();

        for (var index = 0; index < orders.Count; index++)
        {
            if (index < 5)
            {
                context.OrderItems.AddRange(
                    ReportOrderItem(orders[index].Id, skuA, 1, 50m, 10m),
                    ReportOrderItem(orders[index].Id, skuB, 1, 50m, 10m));
            }
            else
            {
                context.OrderItems.Add(ReportOrderItem(orders[index].Id, skuC, 1, 50m, 10m));
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedWp003ForecastAsync(
        DoSelectDbContext context,
        Sku sku,
        long shippingProfileId,
        long packageLimitId)
    {
        var orders = new List<(Order Order, int Quantity, DateTime PaidAtUtc)>();
        for (var day = 1; day <= 30; day++)
        {
            var quantity = day == 15 ? 100 : day;
            var paidAtUtc = new DateTime(2026, 10, day, 2, 0, 0, DateTimeKind.Utc);
            var order = CreateCompletedOrder(
                $"DS-FORECAST-{day:00}",
                quantity * 10m,
                paidAtUtc.AddHours(-2),
                paidAtUtc.AddHours(-1),
                shippingProfileId,
                packageLimitId);
            orders.Add((order, quantity, paidAtUtc));
        }

        context.Orders.AddRange(orders.Select(row => row.Order));
        await context.SaveChangesAsync();

        foreach (var row in orders)
        {
            context.OrderItems.Add(ReportOrderItem(
                row.Order.Id,
                sku,
                row.Quantity,
                row.Quantity * 10m,
                unitCostSnapshot: 1m));
            context.PaymentAttempts.Add(PaidPayment(
                row.Order.Id,
                row.Quantity * 10m,
                $"forecast-payment-{row.PaidAtUtc:yyyyMMdd}",
                row.PaidAtUtc));
        }

        await context.SaveChangesAsync();
    }

    private static Order CreateCompletedOrder(
        string orderNumber,
        decimal grandTotal,
        DateTime createdAtUtc,
        DateTime completedAtUtc,
        long shippingProfileId,
        long packageLimitId)
    {
        var order = Order.Create(
            Guid.CreateVersion7(),
            OrderCreationFor(
                orderNumber,
                grandTotal,
                OrderStatus.Processing,
                shippingProfileId,
                packageLimitId),
            createdAtUtc);
        order.ChangeOrderStatus(OrderStatus.Completed, completedAtUtc);
        return order;
    }

    private static OrderItem ReportOrderItem(
        long orderId,
        Sku sku,
        int quantity,
        decimal lineTotal,
        decimal unitCostSnapshot) => new(
        Guid.CreateVersion7(),
        orderId,
        sku.Id,
        sku.SkuCode,
        "報表商品",
        sku.NameZhTw,
        quantity,
        lineTotal / quantity,
        lineTotal / quantity,
        lineTotal / quantity,
        unitCostSnapshot,
        lineTotal,
        discountAllocation: 0m,
        lineTotal,
        assemblyGroupKey: null,
        returnableQuantity: quantity,
        new DateTime(2026, 9, 1, 1, 0, 0, DateTimeKind.Utc),
        isCouponEligible: true,
        new OrderItemSpecificationSnapshot("報表規格", "{}", 1));

    private static PaymentAttempt PaidPayment(
        long orderId,
        decimal amount,
        string reference,
        DateTime paidAtUtc)
    {
        var payment = new PaymentAttempt(
            Guid.CreateVersion7(),
            orderId,
            PaymentMethod.CreditCard,
            amount,
            "DEMO",
            reference,
            null,
            paidAtUtc.AddMinutes(-3));
        payment.SetPaymentInstruction(reference, paidAtUtc.AddMinutes(-2));
        payment.Transition(PaymentAttemptStatus.Processing, paidAtUtc.AddMinutes(-1));
        payment.Transition(PaymentAttemptStatus.Paid, paidAtUtc);
        return payment;
    }

    private static OrderCreation OrderCreationFor(
        string orderNumber,
        decimal grandTotal,
        OrderStatus orderStatus,
        long shippingProfileId,
        long packageLimitId) => new(
        orderNumber,
        MemberUserId: null,
        GuestEmailNormalized: $"{orderNumber.ToLowerInvariant()}@example.test",
        orderStatus,
        PaymentStatus.Pending,
        FulfillmentStatus.Pending,
        AssemblyStatus.NotRequired,
        MerchandiseSubtotal: grandTotal,
        ItemDiscountTotal: 0m,
        ShippingFee: 0m,
        AssemblyFee: 0m,
        grandTotal,
        RecipientName: "報表測試",
        RecipientPhone: "0912345678",
        RecipientEmail: "report@example.test",
        PostalCode: "100",
        RecipientCity: "Taipei",
        RecipientDistrict: "Zhongzheng",
        AddressLine1: "No. 1",
        AddressLine2: null,
        ShippingMethodCode: "HOME_DELIVERY",
        shippingProfileId,
        StoreCode: null,
        StoreName: null,
        StoreAddress: null,
        ShippingConstraintPolicyVersion: 1,
        ReturnPolicyVersion: 1,
        CouponPolicyVersion: null,
        PaymentDueAtUtc: null,
        CheckoutIdempotencyKey: $"checkout-{orderNumber}",
        SourceCartPublicId: null,
        TermsPolicyVersion: 1,
        PrivacyPolicyVersion: 1,
        new OrderInvoicePreference(
            SimulatedInvoiceBuyerType.Individual,
            "report@example.test",
            CarrierType: null,
            CarrierValueMasked: null,
            CompanyTaxId: null,
            CompanyName: null),
        ShippingFreeThresholdSnapshot: null,
        DeliveryNote: null,
        new OrderPackageSnapshot(packageLimitId, 1m, 40m, 30m, 20m, 90m, grandTotal));

    private static string BuildConnectionString()
    {
        var configured = GetConfiguredConnectionString();
        var builder = new SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? LocalServer : configured)
        {
            InitialCatalog = DatabaseName,
            TrustServerCertificate = true,
        };

        if (string.IsNullOrWhiteSpace(configured))
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static string? GetConfiguredConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
}

public sealed class OperationalReportSqlFactAttribute : FactAttribute
{
    public OperationalReportSqlFactAttribute()
    {
        if (!OperationalReportSqlFixture.IsEnabled)
        {
            Skip = "Set " + OperationalReportSqlFixture.ConnectionStringEnvironmentVariable +
                   " to run SQL Server integration tests.";
        }
    }
}

[CollectionDefinition(nameof(OperationalReportSqlCollection))]
public sealed class OperationalReportSqlCollection : ICollectionFixture<OperationalReportSqlFixture>;

[Collection(nameof(OperationalReportSqlCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfOperationalReportQueryServiceSqlServerTests
{
    [OperationalReportSqlFact]
    public async Task SalesOverviewUsesTaipeiPaidAndRefundDatesAndCreatedOrderCohort()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = new EfOperationalReportQueryService(
            context,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero)));
        var query = OperationalReportQueryValidator.Normalize(new ReportQuery(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 8),
            OperationalReportQueryValidator.SupportedTimeZone,
            CategoryCode: null,
            BrandCode: null,
            OrderStatuses: [],
            ReportGranularities.Day,
            Cursor: null,
            PageSize: 20));

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.SalesOverview),
            query,
            CancellationToken.None);

        Assert.Equal(800m, Metric(result, "net_revenue"));
        Assert.Equal(1_000m, Metric(result, "paid_amount"));
        Assert.Equal(200m, Metric(result, "refund_amount"));
        Assert.Equal(1m, Metric(result, "order_count"));
        Assert.Equal(800m, Metric(result, "average_order_value"));
        Assert.Equal(0.5m, Metric(result, "cancellation_rate"));
        Assert.Equal(1m, Metric(result, "payment_method_credit_card_share"));

        var rows = result.Rows.Items.Cast<SalesOverviewReportRowDto>().ToArray();
        Assert.Equal(
            ["2026-08-01", "2026-08-02", "2026-08-03", "2026-08-04"],
            rows.Select(row => row.Bucket));
        Assert.Equal(0m, rows[0].CancellationRate);
        Assert.Equal(1_000m, rows[1].NetRevenue);
        Assert.Equal(1, rows[1].OrderCount);
        Assert.Equal(1m, rows[2].CancellationRate);
        Assert.Equal(-200m, rows[3].NetRevenue);
        Assert.Equal(200m, rows[3].RefundAmount);
        Assert.False(result.Rows.HasMore);
        Assert.Null(result.Rows.NextCursor);
    }

    [OperationalReportSqlFact]
    public async Task ProductAbcUsesCompletedSkuRevenueAndSuccessfulItemRefunds()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ProductAbc),
            Wp002Query(),
            CancellationToken.None);

        var rows = result.Rows.Items.Cast<ProductAbcReportRowDto>().ToArray();
        Assert.Equal(["REPORT-A", "REPORT-B", "REPORT-C"], rows.Select(row => row.SkuCode));
        Assert.Equal([800m, 150m, 50m], rows.Select(row => row.NetRevenue));
        Assert.Equal([1, 1, 1], rows.Select(row => row.Quantity));
        Assert.Equal([0.80m, 0.15m, 0.05m], rows.Select(row => row.RevenueShare));
        Assert.Equal([0.80m, 0.95m, 1m], rows.Select(row => row.CumulativeRevenueShare));
        Assert.Equal(["A", "B", "C"], rows.Select(row => row.AbcClass));
        Assert.Equal([1, 2, 3], rows.Select(row => row.Rank));
        Assert.Equal(1_000m, Metric(result, "net_revenue"));
    }

    [OperationalReportSqlFact]
    public async Task ProductAbcCursorContinuesTheStableRankingWithoutDuplicates()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);
        var first = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ProductAbc),
            Wp002Query() with { PageSize = 2 },
            CancellationToken.None);

        Assert.True(first.Rows.HasMore);
        Assert.NotNull(first.Rows.NextCursor);
        Assert.Equal(
            ["REPORT-A", "REPORT-B"],
            first.Rows.Items.Cast<ProductAbcReportRowDto>().Select(row => row.SkuCode));

        var second = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ProductAbc),
            Wp002Query() with { PageSize = 2, Cursor = first.Rows.NextCursor },
            CancellationToken.None);

        Assert.False(second.Rows.HasMore);
        Assert.Null(second.Rows.NextCursor);
        Assert.Equal(
            ["REPORT-C"],
            second.Rows.Items.Cast<ProductAbcReportRowDto>().Select(row => row.SkuCode));
    }

    [OperationalReportSqlFact]
    public async Task PeriodComparisonUsesTheAdjacentSameLengthPeriodAndZeroDenominatorRules()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.PeriodComparison),
            Wp002Query(),
            CancellationToken.None);

        var rows = result.Rows.Items
            .Cast<PeriodComparisonReportRowDto>()
            .ToDictionary(row => row.MetricKey, StringComparer.Ordinal);
        AssertComparison(rows["net_revenue"], 1_000m, 500m, 1m, isNew: false);
        AssertComparison(rows["order_count"], 1m, 1m, 0m, isNew: false);
        AssertComparison(rows["average_order_value"], 1_000m, 500m, 1m, isNew: false);
        AssertComparison(rows["refund_amount"], 100m, 0m, null, isNew: true);
    }

    [OperationalReportSqlFact]
    public async Task GrossMarginUsesOrderItemCostSnapshotsAndRefundedQuantities()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.GrossMargin),
            Wp002Query(),
            CancellationToken.None);

        var rows = result.Rows.Items.Cast<GrossMarginReportRowDto>().ToArray();
        Assert.Equal(["REPORT-A", "REPORT-B", "REPORT-C"], rows.Select(row => row.SkuCode));
        Assert.Equal([800m, 150m, 50m], rows.Select(row => row.NetRevenue));
        Assert.Equal([300m, 100m, 20m], rows.Select(row => row.CostOfGoodsSold));
        Assert.Equal([500m, 50m, 30m], rows.Select(row => row.GrossProfit));
        Assert.Equal([1, 1, 1], rows.Select(row => row.QuantitySold));
        Assert.Equal([1, 0, 0], rows.Select(row => row.RefundedQuantity));
        Assert.Equal(1_000m, Metric(result, "net_revenue"));
        Assert.Equal(420m, Metric(result, "cost_of_goods_sold"));
        Assert.Equal(580m, Metric(result, "gross_profit"));
        Assert.Equal(0.58m, Metric(result, "gross_margin_rate"));
    }

    [OperationalReportSqlFact]
    public async Task InventoryTurnoverUsesHistoricalValuationSnapshotsAndMarksMissingHistory()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.InventoryTurnover),
            Wp002Query(),
            CancellationToken.None);

        var rows = result.Rows.Items
            .Cast<InventoryTurnoverReportRowDto>()
            .ToDictionary(row => row.SkuCode, StringComparer.Ordinal);
        Assert.False(rows["REPORT-A"].IsInsufficientData);
        Assert.Equal(1_000m, rows["REPORT-A"].BeginningInventoryCost);
        Assert.Equal(960m, rows["REPORT-A"].EndingInventoryCost);
        Assert.Equal(980m, rows["REPORT-A"].AverageInventoryCost);
        Assert.Equal(300m, rows["REPORT-A"].CostOfGoodsSold);
        Assert.Equal(300m / 980m, rows["REPORT-A"].TurnoverRate);
        Assert.Equal(7m / (300m / 980m), rows["REPORT-A"].TurnoverDays);

        Assert.True(rows["REPORT-B"].IsInsufficientData);
        Assert.Null(rows["REPORT-B"].BeginningInventoryCost);
        Assert.Null(rows["REPORT-B"].TurnoverRate);
    }

    [OperationalReportSqlFact]
    public async Task ProductAssociationsEmitBothDirectionsAndApplyMinimumEvidenceThresholds()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ProductAssociations),
            Wp003AssociationQuery(),
            CancellationToken.None);

        var rows = result.Rows.Items.Cast<ProductAssociationReportRowDto>().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row =>
            row.LeftSkuCode == "REPORT-A" &&
            row.RightSkuCode == "REPORT-B" &&
            row.CoOccurrenceOrderCount == 5 &&
            row.Support == 0.5m &&
            row.Confidence == 1m &&
            row.Lift == 2m);
        Assert.Contains(rows, row =>
            row.LeftSkuCode == "REPORT-B" &&
            row.RightSkuCode == "REPORT-A" &&
            row.Confidence == 1m);
    }

    [OperationalReportSqlFact]
    public async Task ForecastUsesThirtyDayRegressionAndResidualPopulationZScores()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ForecastAnomalies),
            Wp003ForecastQuery(),
            CancellationToken.None);

        var rows = result.Rows.Items.Cast<ForecastAnomalyReportRowDto>().ToArray();
        Assert.Equal(37, rows.Length);
        Assert.All(rows, row => Assert.False(row.IsInsufficientData));
        Assert.Equal(30, rows.Count(row => row.ActualValue is not null));
        Assert.Equal(7, rows.Count(row => row.ForecastValue is not null && row.ActualValue is null));
        Assert.Contains(rows, row => row.Date == new DateOnly(2026, 10, 15) && row.IsAnomaly);
        Assert.All(
            rows.Where(row => row.ActualValue is null),
            row => Assert.True(row.ForecastValue >= 0m));
    }

    [OperationalReportSqlFact]
    public async Task ForecastMarksWindowsShorterThanFourteenDaysAsInsufficient()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var service = CreateService(context);

        var result = await service.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.ForecastAnomalies),
            Wp002Query(),
            CancellationToken.None);

        var rows = result.Rows.Items.Cast<ForecastAnomalyReportRowDto>().ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.True(row.IsInsufficientData);
            Assert.Null(row.ForecastValue);
            Assert.Null(row.ZScore);
            Assert.False(row.IsAnomaly);
        });
    }

    private static EfOperationalReportQueryService CreateService(DoSelectDbContext context) =>
        new(
            context,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));

    private static ValidatedReportQuery Wp002Query() =>
        OperationalReportQueryValidator.Normalize(new ReportQuery(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 8),
            OperationalReportQueryValidator.SupportedTimeZone,
            CategoryCode: "REPORT-CAT",
            BrandCode: null,
            OrderStatuses: [],
            ReportGranularities.Day,
            Cursor: null,
            PageSize: 20));

    private static ValidatedReportQuery Wp003AssociationQuery() =>
        OperationalReportQueryValidator.Normalize(new ReportQuery(
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 20),
            OperationalReportQueryValidator.SupportedTimeZone,
            CategoryCode: "REPORT-CAT",
            BrandCode: null,
            OrderStatuses: [],
            ReportGranularities.Day,
            Cursor: null,
            PageSize: 20));

    private static ValidatedReportQuery Wp003ForecastQuery() =>
        OperationalReportQueryValidator.Normalize(new ReportQuery(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            OperationalReportQueryValidator.SupportedTimeZone,
            CategoryCode: "REPORT-CAT",
            BrandCode: null,
            OrderStatuses: [],
            ReportGranularities.Day,
            Cursor: null,
            PageSize: 100));

    private static void AssertComparison(
        PeriodComparisonReportRowDto row,
        decimal current,
        decimal previous,
        decimal? changeRate,
        bool isNew)
    {
        Assert.Equal(current, row.CurrentValue);
        Assert.Equal(previous, row.PreviousValue);
        Assert.Equal(changeRate, row.ChangeRate);
        Assert.Equal(isNew, row.IsNew);
    }

    private static decimal? Metric(ReportResultDto result, string key) =>
        Assert.Single(result.Summary, metric => metric.MetricKey == key).Value;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
