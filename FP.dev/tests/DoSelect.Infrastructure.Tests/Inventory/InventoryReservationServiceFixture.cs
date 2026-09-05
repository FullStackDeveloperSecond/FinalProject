using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DoSelect.Infrastructure.Tests.Inventory;

public sealed class InventoryReservationServiceFixture : IAsyncLifetime
{
    // 組長 PR #36: was hardcoded to the local ".\SQL2025" instance — CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION (SQL auth, localhost:1433),
    // so every test in this fixture failed with a connection error in CI despite passing locally.
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectInventoryReservationServiceTests");

    public Task InitializeAsync() => ResetDatabaseAsync();

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new DoSelectDbContext(builder.Options);
    }

    // Guid.NewGuid() (random), not Guid.CreateVersion7(), to avoid collisions when a test seeds
    // several rows within the same millisecond (mirrors CartServiceFixture's own note).
    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public async Task<Sku> SeedSkuWithBalanceAsync(
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

    // InventoryMovements.ActorUserId and InventoryReconciliationCases's admin columns have real
    // foreign keys to AspNetUsers, so tests that pass an actor need a seeded row, not an arbitrary string.
    /// <summary>
    /// 種一個真的管理員，並掛上指定角色的 Roles／UserRoles 列。人工釋放的中央稽核要留角色快照，
    /// 服務層是從 UserRoles 讀的，不是從呼叫端傳的字串——所以測試裡的管理員也要有真的角色列。
    /// </summary>
    public static async Task<string> SeedAdminUserIdAsync(DoSelectDbContext context, params string[] roles)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        foreach (var roleName in roles)
        {
            var role = await context.Roles.SingleOrDefaultAsync(candidate => candidate.Name == roleName);
            if (role is null)
            {
                role = new IdentityRole(roleName);
                context.Roles.Add(role);
                await context.SaveChangesAsync();
            }

            context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
        }

        await context.SaveChangesAsync();
        return admin.Id;
    }

    public async Task<long> SeedOrderAsync(DoSelectDbContext context)
    {
        var now = DateTime.UtcNow;
        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), UniqueCode("PROVIDER"), version: 1, status: "Active",
            effectiveFromUtc: now.AddDays(-1), effectiveToUtc: null,
            configurationJson: "{}", schemaVersion: 1, now);
        context.ShippingProviderProfiles.Add(shippingProfile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), shippingProfile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, now);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var creation = new OrderCreation(
            OrderNumber: UniqueCode("ORD"),
            MemberUserId: null,
            GuestEmailNormalized: $"{Guid.NewGuid():N}@doselect.test",
            OrderStatus: OrderStatus.PendingPayment,
            PaymentStatus: PaymentStatus.Pending,
            FulfillmentStatus: FulfillmentStatus.Pending,
            AssemblyStatus: AssemblyStatus.NotRequired,
            MerchandiseSubtotal: 1000m,
            ItemDiscountTotal: 0m,
            ShippingFee: 0m,
            AssemblyFee: 0m,
            GrandTotal: 1000m,
            RecipientName: "測試收件人",
            RecipientPhone: "0912345678",
            RecipientEmail: "recipient@doselect.test",
            PostalCode: "100",
            RecipientCity: "台北市",
            RecipientDistrict: "中正區",
            AddressLine1: "測試路 1 號",
            AddressLine2: null,
            ShippingMethodCode: "home_delivery",
            ShippingProviderProfileVersionId: shippingProfile.Id,
            StoreCode: null,
            StoreName: null,
            StoreAddress: null,
            ShippingConstraintPolicyVersion: 1,
            ReturnPolicyVersion: 1,
            CouponPolicyVersion: null,
            PaymentDueAtUtc: now.AddMinutes(15),
            CheckoutIdempotencyKey: UniqueCode("IDEMP"),
            SourceCartPublicId: null,
            TermsPolicyVersion: 1,
            PrivacyPolicyVersion: 1,
            InvoicePreference: new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Individual, "recipient@doselect.test", null, null, null, null),
            ShippingFreeThresholdSnapshot: null,
            DeliveryNote: null,
            PackageSnapshot: new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 1000m));

        var order = Order.Create(Guid.CreateVersion7(), creation, now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order.Id;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
