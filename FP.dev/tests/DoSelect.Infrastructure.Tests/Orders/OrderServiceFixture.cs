using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Orders;

public sealed class OrderServiceFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectOrderServiceTests;Trusted_Connection=True;" +
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

    public static async Task<string> SeedMemberUserIdAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(
            Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(member);
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
        return profile;
    }

    /// <summary>
    /// Builds an Order + a single OrderItem in one status. Callers needing a delivered order
    /// with returnable quantity pass <paramref name="deliveredAtUtc"/> and
    /// <paramref name="returnableQuantity"/>.
    /// </summary>
    public static async Task<Order> SeedOrderAsync(
        DoSelectDbContext context,
        string? memberUserId,
        long shippingProviderProfileId,
        OrderStatus orderStatus,
        FulfillmentStatus fulfillmentStatus = FulfillmentStatus.Pending,
        DateTime? deliveredAtUtc = null,
        int returnableQuantity = 0,
        int returnedQuantity = 0)
    {
        var now = DateTime.UtcNow;
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
            StoreCode: null,
            StoreName: null,
            StoreAddress: null,
            ShippingConstraintPolicyVersion: 1,
            ReturnPolicyVersion: 1,
            CouponPolicyVersion: null,
            PaymentDueAtUtc: now.AddMinutes(15),
            CheckoutIdempotencyKey: $"checkout-{Guid.NewGuid():N}",
            SourceCartPublicId: null);

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
            createdAtUtc: now);
        if (returnedQuantity > 0)
        {
            item.RecordReturnedQuantity(returnedQuantity);
        }

        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        return order;
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
