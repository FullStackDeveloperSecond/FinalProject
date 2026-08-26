using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;

namespace DoSelect.Api.IntegrationTests.Orders;

/// <summary>
/// Seeds Orders directly via <see cref="DoSelectDbContext"/> — there is no Checkout/order-
/// creation API in this branch yet (blocked on terry/yinyin's Application contracts), so
/// AdminOrdersController tests build their fixture data the same way
/// CatalogAdminApiFixture.CreateScopedContext seeds inventory balances.
/// </summary>
public static class AdminOrdersApiSeeding
{
    /// <summary>
    /// OrderStatusHistories.ActorUserId is a real FK to AspNetUsers (Restrict) — the test-only
    /// sign-in endpoint (/__tests/security/sign-in/admin) mints a claims-only principal with no
    /// backing Identity row, so any test that exercises an admin action recording an actor
    /// (ExecuteActionAsync) must seed a real ApplicationUser and sign in with its exact Id.
    /// </summary>
    public static async Task<string> SeedAdminUserAsync(DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(),
            $"{AdminOrdersApiFixture.UniqueCode("admin").ToLowerInvariant()}@example.com",
            DateTime.UtcNow);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        return admin.Id;
    }

    public static async Task<long> SeedShippingProviderProfileAsync(DoSelectDbContext context)
    {
        // Every test in the collection shares one database (reset once, not per test — see
        // AdminOrdersApiFixture), so ProviderCode must be unique per call, not a fixed literal.
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(),
            AdminOrdersApiFixture.UniqueCode("SHIP"),
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            DateTime.UtcNow);
        context.Add(profile);
        await context.SaveChangesAsync();
        return profile.Id;
    }

    public static async Task<Order> SeedOrderAsync(
        DoSelectDbContext context,
        long shippingProviderProfileVersionId,
        OrderStatus orderStatus = OrderStatus.Confirmed,
        PaymentStatus paymentStatus = PaymentStatus.Paid,
        FulfillmentStatus fulfillmentStatus = FulfillmentStatus.Pending,
        string? memberUserId = null,
        string? guestEmailNormalized = null)
    {
        // Orders.MemberUserId is a real FK to AspNetUsers (Restrict) — seeding a fake id would
        // violate the constraint. Default to a guest order (no Identity user needed) unless a
        // test explicitly passes a memberUserId it has actually seeded into AspNetUsers itself.
        guestEmailNormalized ??= memberUserId is null
            ? $"{AdminOrdersApiFixture.UniqueCode("guest").ToLowerInvariant()}@example.com"
            : null;

        var now = DateTime.UtcNow;
        var creation = new OrderCreation(
            AdminOrdersApiFixture.UniqueCode("ORD"),
            memberUserId,
            guestEmailNormalized,
            orderStatus,
            paymentStatus,
            fulfillmentStatus,
            AssemblyStatus.NotRequired,
            1000m,
            0m,
            100m,
            0m,
            1100m,
            "測試收件人",
            "0912345678",
            "buyer@example.com",
            "100",
            "台北市",
            "中正區",
            "測試路 1 號",
            null,
            "home-delivery",
            shippingProviderProfileVersionId,
            null,
            null,
            null,
            1,
            1,
            null,
            orderStatus == OrderStatus.PendingPayment ? now.AddHours(1) : null,
            AdminOrdersApiFixture.UniqueCode("IDEMP"),
            null);

        var order = Order.Create(Guid.CreateVersion7(), creation, now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }
}
