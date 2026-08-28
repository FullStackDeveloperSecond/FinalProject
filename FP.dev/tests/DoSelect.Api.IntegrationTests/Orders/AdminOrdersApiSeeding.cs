using DoSelect.Api.Security;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;

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
    /// Also seeds an AdminProfile — EfAdminOrderService resolves the acting admin's PublicId
    /// from it for the cancel/recipient-view Audit trail (Alex review, 2026-08-28) — plus a real
    /// OrderManager role assignment: DEC-BATCH-026 (DEC-P309) established that the test-only
    /// sign-in shortcut only stamps role claims onto the auth cookie, it never writes real
    /// AspNetUserRoles rows, but EfAdminOrderService's audit actor resolution re-queries the real
    /// Users/UserRoles/Roles tables (mirrors EfCompatibilityRuleAdminService/InvoiceAllowanceWriter),
    /// so a claims-only principal isn't enough here.
    /// </summary>
    public static async Task<string> SeedAdminUserAsync(DoSelectDbContext context)
    {
        var now = DateTime.UtcNow;
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(),
            $"{AdminOrdersApiFixture.UniqueCode("admin").ToLowerInvariant()}@example.com",
            now);
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        context.AdminProfiles.Add(new AdminProfile(
            admin.Id,
            Guid.CreateVersion7(),
            AdminOrdersApiFixture.UniqueCode("EMP"),
            "測試管理員",
            now));

        var role = new IdentityRole(DoSelectRoles.OrderManager);
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
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

    /// <summary>
    /// OrderCreation.PackageSnapshot 需要一筆真實的 PackageLimitVersion（見
    /// GuestOrderAccessControllerTests.SeedGuestOrderAsync 的作法）；每次呼叫建立獨立一筆，
    /// 避免與同一 shippingProviderProfileVersionId 下其他測試的版本互相干擾。
    /// </summary>
    public static async Task<long> SeedPackageLimitVersionAsync(
        DoSelectDbContext context,
        long shippingProviderProfileVersionId)
    {
        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(),
            shippingProviderProfileVersionId,
            1,
            30m,
            150m,
            100m,
            100m,
            250m,
            50_000m,
            null,
            null,
            DateTime.UtcNow);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();
        return packageLimit.Id;
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

        var packageLimitVersionId = await SeedPackageLimitVersionAsync(context, shippingProviderProfileVersionId);

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
            null,
            TermsPolicyVersion: 1,
            PrivacyPolicyVersion: 1,
            InvoicePreference: new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Individual,
                "buyer@example.com",
                null,
                null,
                null,
                null),
            ShippingFreeThresholdSnapshot: null,
            DeliveryNote: null,
            PackageSnapshot: new OrderPackageSnapshot(packageLimitVersionId, 1m, 40m, 30m, 20m, 90m, 1000m));

        var order = Order.Create(Guid.CreateVersion7(), creation, now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }
}
