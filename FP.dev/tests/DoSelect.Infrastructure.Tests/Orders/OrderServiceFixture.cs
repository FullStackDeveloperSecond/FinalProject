using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DoSelect.Infrastructure.Tests.Orders;

public sealed class OrderServiceFixture : IAsyncLifetime
{
    public Task InitializeAsync() => ResetDatabaseAsync();

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    /// <summary>
    /// interceptor 參數讓測試在「查詢已經跑完、但服務還沒做下一步」的接縫上插手——組長 PR #85
    /// round-3 review 指出，只在呼叫服務之前改狀態證明不了逐筆重新載入的新鮮度檢查。形狀比照
    /// CompatibilityCheckServiceFixture.CreateContext。
    /// </summary>
    public static DoSelectDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(global::DoSelect.Infrastructure.Tests.SqlServerTestConnection.Build(
                "DoSelectOrderServiceTests"));
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new DoSelectDbContext(builder.Options);
    }

    public static async Task<string> SeedMemberUserIdAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(
            Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(member);
        context.MemberProfiles.Add(new MemberProfile(
            member.Id,
            member.PublicId,
            "訂單測試會員",
            birthDate: null,
            createdAtUtc: DateTime.UtcNow));
        await context.SaveChangesAsync();
        return member.Id;
    }

    public static async Task<ShippingProviderProfile> SeedShippingProviderProfileAsync(DoSelectDbContext context)
    {
        // ProviderCode+Version is unique (UX_ProviderProfiles_ProviderCode_Version) — each test
        // method in this collection shares one database, so a fixed code would collide across
        // test methods the same way CartServiceFixture.UniqueCode avoids for SKUs.
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(),
            $"home-delivery-{Guid.NewGuid():N}"[..24],
            version: 1,
            status: "active",
            effectiveFromUtc: null,
            effectiveToUtc: null,
            configurationJson: "{}",
            schemaVersion: 1,
            createdAtUtc: DateTime.UtcNow);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(),
            profile.Id,
            version: 1,
            maxWeightKg: 30m,
            maxLengthCm: 150m,
            maxWidthCm: 100m,
            maxHeightCm: 100m,
            maxTotalCm: 250m,
            maxDeclaredValue: 50_000m,
            effectiveFromUtc: null,
            effectiveToUtc: null,
            createdAtUtc: DateTime.UtcNow));
        await context.SaveChangesAsync();
        return profile;
    }

    /// <summary>
    /// Builds an Order + a single OrderItem in one status. Callers needing a delivered order
    /// with returnable quantity pass <paramref name="deliveredAtUtc"/> and
    /// <paramref name="returnableQuantity"/>.
    /// </summary>
    /// <summary>
    /// SeedOrderAsync 用的是固定的 `home-delivery` 代碼，但沒有建立對應的 ShippingMethod 列——
    /// 出貨流程要靠代碼查回那一列，所以測試得自己種。已存在就沿用（同一個資料庫在整個 collection
    /// 內共用）。
    /// </summary>
    public static async Task<ShippingMethod> SeedShippingMethodAsync(
        DoSelectDbContext context,
        string code = "home-delivery")
    {
        var existing = await context.ShippingMethods
            .FirstOrDefaultAsync(candidate => candidate.Code == code);
        if (existing is not null)
        {
            return existing;
        }

        var method = new ShippingMethod(
            Guid.CreateVersion7(), code, "宅配", "HomeDelivery",
            baseFee: 0m, freeShippingThreshold: null, allowsCod: true, requiresPrepayment: false,
            providerCode: "TCAT", createdAtUtc: DateTime.UtcNow);
        context.ShippingMethods.Add(method);
        await context.SaveChangesAsync();
        return method;
    }

    public static async Task<Order> SeedOrderAsync(
        DoSelectDbContext context,
        string? memberUserId,
        long shippingProviderProfileId,
        OrderStatus orderStatus,
        FulfillmentStatus fulfillmentStatus = FulfillmentStatus.Pending,
        DateTime? deliveredAtUtc = null,
        int returnableQuantity = 0,
        int returnedQuantity = 0,
        string? storeCode = null)
    {
        var now = DateTime.UtcNow;
        var packageLimitVersionId = await context.PackageLimitVersions
            .Where(candidate => candidate.ProviderProfileId == shippingProviderProfileId)
            .Select(candidate => candidate.Id)
            .SingleAsync();
        var creation = new OrderCreation(
            OrderNumber: $"DS{Guid.NewGuid():N}"[..20],
            MemberUserId: memberUserId,
            GuestEmailNormalized: memberUserId is null ? "guest@doselect.test" : null,
            OrderStatus: OrderStatus.PendingPayment,
            PaymentStatus: PaymentStatus.Pending,
            FulfillmentStatus: fulfillmentStatus,
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
            ShippingMethodCode: "home-delivery",
            ShippingProviderProfileVersionId: shippingProviderProfileId,
            StoreCode: storeCode,
            StoreName: storeCode is null ? null : "測試門市",
            StoreAddress: storeCode is null ? null : "台北市中正區測試路 1 號",
            ShippingConstraintPolicyVersion: 1,
            ReturnPolicyVersion: 1,
            CouponPolicyVersion: null,
            PaymentDueAtUtc: now.AddMinutes(15),
            CheckoutIdempotencyKey: $"checkout-{Guid.NewGuid():N}",
            SourceCartPublicId: null,
            TermsPolicyVersion: 1,
            PrivacyPolicyVersion: 1,
            InvoicePreference: new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Individual,
                "recipient@doselect.test",
                CarrierType: null,
                CarrierValueMasked: null,
                CompanyTaxId: null,
                CompanyName: null),
            ShippingFreeThresholdSnapshot: null,
            DeliveryNote: null,
            PackageSnapshot: new OrderPackageSnapshot(
                packageLimitVersionId,
                WeightKg: 1m,
                LengthCm: 10m,
                WidthCm: 10m,
                HeightCm: 10m,
                TotalCm: 30m,
                DeclaredValue: 1_000m));

        var order = Order.Create(Guid.CreateVersion7(), creation, now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Order.ChangeOrderStatus only allows the state machine's declared single-step edges
        // (狀態機設計.md), so reaching Processing/Completed from the PendingPayment a brand-new
        // order starts in means walking through Confirmed (and Processing) first.
        foreach (var step in StepsToReach(orderStatus))
        {
            order.ChangeOrderStatus(step, now);
        }

        if (fulfillmentStatus != FulfillmentStatus.Pending || deliveredAtUtc.HasValue)
        {
            order.ApplyFulfillmentProjection(fulfillmentStatus, deliveredAtUtc ?? now);
        }

        var item = new OrderItem(
            Guid.CreateVersion7(),
            order.Id,
            skuId: null,
            skuCodeSnapshot: "SKU-TEST",
            productNameSnapshot: "測試商品",
            skuNameSnapshot: "測試規格",
            quantity: 1,
            listUnitPrice: 1000m,
            saleUnitPrice: 1000m,
            finalUnitPrice: 1000m,
            unitCostSnapshot: 600m,
            lineSubtotal: 1000m,
            discountAllocation: 0m,
            lineTotal: 1000m,
            assemblyGroupKey: null,
            returnableQuantity: returnableQuantity,
            createdAtUtc: now,
            isCouponEligible: false,
            specificationSnapshot: new OrderItemSpecificationSnapshot("測試規格", "{}", 1));
        if (returnedQuantity > 0)
        {
            item.RecordReturnedQuantity(returnedQuantity);
        }

        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        return order;
    }

    public static async Task<(InventoryBalance Balance, InventoryReservation Reservation)>
        SeedInventoryReservationAsync(DoSelectDbContext context, Order order)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var brand = new Brand(Guid.CreateVersion7(), $"BR-{suffix}", "訂單測試品牌", now);
        var category = new Category(
            Guid.CreateVersion7(),
            $"CAT-{suffix}",
            $"order-test-{suffix.ToLowerInvariant()}",
            "訂單測試分類",
            parentCategoryId: null,
            createdAtUtc: now);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.CreateVersion7(),
            $"PROD-{suffix}",
            brand.Id,
            category.Id,
            "訂單測試商品",
            now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(
            Guid.CreateVersion7(),
            $"SKU-{suffix}",
            product.Id,
            "訂單測試 SKU",
            listPrice: 1_000m,
            unitCost: 600m,
            createdAtUtc: now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var balance = new InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity: 5, reorderLevel: 1, createdAtUtc: now);
        balance.ApplyQuantities(onHandQuantity: 5, reservedQuantity: 1, updatedAtUtc: now);
        var reservation = new InventoryReservation(
            Guid.CreateVersion7(),
            sku.Id,
            order.Id,
            quantity: 1,
            expiresAtUtc: now.AddMinutes(15),
            createdAtUtc: now);
        context.AddRange(balance, reservation);
        await context.SaveChangesAsync();
        return (balance, reservation);
    }

    public static async Task<(Coupon Coupon, CouponRedemption Redemption)>
        SeedCouponReservationAsync(
            DoSelectDbContext context,
            Order order,
            string memberUserId,
            bool markExhausted)
    {
        var now = DateTime.UtcNow;
        var coupon = new Coupon(
            Guid.CreateVersion7(),
            new CouponCreation(
                $"ORDER-{Guid.NewGuid():N}"[..24],
                "訂單取消測試券",
                CouponDiscountType.FixedAmount,
                DiscountValue: 100m,
                MinimumSpend: 0m,
                MaximumDiscount: null,
                StartsAtUtc: now.AddDays(-1),
                EndsAtUtc: now.AddDays(7),
                TotalUsageLimit: 1,
                PerMemberLimit: 1,
                MemberOnly: true,
                ExcludeSaleItems: false,
                ScopeType: CouponScopeType.All),
            now.AddDays(-1));
        coupon.ActivateNow(CouponUsageState.Unused, now);
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        var redemption = new CouponRedemption(
            Guid.CreateVersion7(),
            coupon.Id,
            order.Id,
            memberUserId,
            guestUsageKeyHash: null,
            reservedAtUtc: now,
            expiresAtUtc: now.AddMinutes(15),
            createdAtUtc: now);
        context.CouponRedemptions.Add(redemption);
        await context.SaveChangesAsync();

        if (markExhausted)
        {
            coupon.MarkExhausted(new CouponUsageState(1, 1), now);
            await context.SaveChangesAsync();
        }

        return (coupon, redemption);
    }

    private static IEnumerable<OrderStatus> StepsToReach(OrderStatus target) => target switch
    {
        OrderStatus.PendingPayment => [],
        OrderStatus.Cancelled => [OrderStatus.Cancelled],
        OrderStatus.Confirmed => [OrderStatus.Confirmed],
        OrderStatus.Processing => [OrderStatus.Confirmed, OrderStatus.Processing],
        OrderStatus.Completed => [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Completed],
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
