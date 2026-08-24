using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Shipping;

public sealed class ShippingServiceFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectShippingServiceTests;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    public Task InitializeAsync() => ResetDatabaseAsync();

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }

    // Guid.NewGuid() (random), not Guid.CreateVersion7(), to avoid collisions when a test seeds
    // several rows within the same millisecond (mirrors InventoryReservationServiceFixture's note).
    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public static async Task<ShippingMethod> SeedShippingMethodAsync(
        DoSelectDbContext context, string kind, bool allowsCod = false, bool requiresPrepayment = false,
        bool isActive = true)
    {
        var now = DateTime.UtcNow;
        var method = new ShippingMethod(
            Guid.CreateVersion7(), UniqueCode("METHOD"), "測試配送方式", kind,
            baseFee: 100m, freeShippingThreshold: 1000m, allowsCod, requiresPrepayment, now);
        if (!isActive)
        {
            // ShippingMethod has no explicit deactivate mutator (v1 doesn't support disabling the
            // two fixed methods per spec) — tests that need an inactive row go through raw EF.
            context.Entry(method).Property("IsActive").CurrentValue = false;
        }

        context.ShippingMethods.Add(method);
        await context.SaveChangesAsync();
        return method;
    }

    public static async Task<(ShippingProviderProfile Profile, PackageLimitVersion Limit)> SeedPublishedProviderAsync(
        DoSelectDbContext context, string providerCode, int version = 1)
    {
        var now = DateTime.UtcNow;
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(), providerCode, version, ShippingProviderProfile.PublishedStatus,
            effectiveFromUtc: now.AddDays(-1), effectiveToUtc: null,
            configurationJson: "{}", schemaVersion: 1, now);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();

        var limit = new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, version,
            maxWeightKg: 5m, maxLengthCm: 45m, maxWidthCm: 45m, maxHeightCm: 45m,
            maxTotalCm: 105m, maxDeclaredValue: 100000m,
            effectiveFromUtc: now.AddDays(-1), effectiveToUtc: null, now);
        context.PackageLimitVersions.Add(limit);
        await context.SaveChangesAsync();

        return (profile, limit);
    }

    public static async Task<ConvenienceStore> SeedConvenienceStoreAsync(DoSelectDbContext context, string providerCode)
    {
        var now = DateTime.UtcNow;
        var storeCode = UniqueCode("STORE");
        var store = new ConvenienceStore(
            Guid.CreateVersion7(), providerCode, storeCode, "測試門市", "測試地址 1 號",
            "台北市", "信義區", isDemoData: true, now);
        context.ConvenienceStores.Add(store);
        await context.SaveChangesAsync();
        return store;
    }

    public static async Task<Sku> SeedSkuWithBalanceAsync(
        DoSelectDbContext context, int onHandQuantity, int reservedQuantity = 0)
    {
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        var category = new Category(
            Guid.CreateVersion7(), UniqueCode("CAT"), "cat-" + Guid.NewGuid().ToString("N")[..12], "測試分類", null, now);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), UniqueCode("SKU"), product.Id, "測試SKU", 1000m, 600m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var balance = new InventoryBalance(Guid.CreateVersion7(), sku.Id, onHandQuantity, reorderLevel: 0, now);
        if (reservedQuantity > 0)
        {
            balance.ApplyQuantities(onHandQuantity, reservedQuantity, now);
        }

        context.InventoryBalances.Add(balance);
        await context.SaveChangesAsync();

        return sku;
    }

    public static async Task<string> SeedAdminUserIdAsync(DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        return admin.Id;
    }

    /// <summary>Seeds an Order + one Active InventoryReservation for it, ready to ship by default. Callers override individual fields via the optional parameters to build a not-ready scenario.</summary>
    public static async Task<Order> SeedShippableOrderAsync(
        DoSelectDbContext context,
        Sku sku,
        long providerProfileId,
        string shippingMethodCode,
        int reservedQuantity = 1,
        OrderStatus orderStatus = OrderStatus.Confirmed,
        PaymentStatus paymentStatus = PaymentStatus.Paid,
        FulfillmentStatus fulfillmentStatus = FulfillmentStatus.Pending,
        AssemblyStatus assemblyStatus = AssemblyStatus.NotRequired,
        string? storeCode = null,
        InventoryReservationStatus reservationStatus = InventoryReservationStatus.Active)
    {
        var now = DateTime.UtcNow;
        var creation = new OrderCreation(
            OrderNumber: UniqueCode("ORD"),
            MemberUserId: null,
            GuestEmailNormalized: $"{Guid.NewGuid():N}@doselect.test",
            OrderStatus: orderStatus,
            PaymentStatus: paymentStatus,
            FulfillmentStatus: fulfillmentStatus,
            AssemblyStatus: assemblyStatus,
            MerchandiseSubtotal: 1000m,
            ItemDiscountTotal: 0m,
            ShippingFee: 100m,
            AssemblyFee: 0m,
            GrandTotal: 1100m,
            RecipientName: "測試收件人",
            RecipientPhone: "0912345678",
            RecipientEmail: "recipient@doselect.test",
            PostalCode: storeCode is null ? "100" : null,
            RecipientCity: storeCode is null ? "台北市" : null,
            RecipientDistrict: storeCode is null ? "中正區" : null,
            AddressLine1: storeCode is null ? "測試路 1 號" : null,
            AddressLine2: null,
            ShippingMethodCode: shippingMethodCode,
            ShippingProviderProfileVersionId: providerProfileId,
            StoreCode: storeCode,
            StoreName: storeCode is null ? null : "測試門市",
            StoreAddress: storeCode is null ? null : "測試地址 1 號",
            ShippingConstraintPolicyVersion: 1,
            ReturnPolicyVersion: 1,
            CouponPolicyVersion: null,
            PaymentDueAtUtc: null,
            CheckoutIdempotencyKey: UniqueCode("IDEMP"),
            SourceCartPublicId: null);

        var order = Order.Create(Guid.CreateVersion7(), creation, now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        if (reservedQuantity > 0)
        {
            var reservation = new InventoryReservation(
                Guid.CreateVersion7(), sku.Id, order.Id, reservedQuantity, now.AddMinutes(30), now);
            if (reservationStatus == InventoryReservationStatus.Released)
            {
                reservation.Release("test_release", expired: false, now);
            }
            else if (reservationStatus == InventoryReservationStatus.Consumed)
            {
                reservation.Consume(now);
            }

            context.InventoryReservations.Add(reservation);
            await context.SaveChangesAsync();
        }

        return order;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
