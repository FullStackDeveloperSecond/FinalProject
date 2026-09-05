using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Inventory;

/// <summary>Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for AdminInventoryController.</summary>
public sealed class AdminInventoryApiFixture : IAsyncLifetime
{
    // 組長 PR #36: was hardcoded to the local ".\SQL2025" instance and, worse, actively
    // overwrote CI's own ConnectionStrings__DefaultConnection env var with that local value via
    // EnvironmentOverrides below — every test in this fixture failed to connect in CI despite
    // passing locally.
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectAdminInventoryApiTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "DoSelectAdminInventoryApiTests", Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();

        // 組長 PR #36 review, item 2: this used to unconditionally null out every overridden key
        // in the finally block below, instead of restoring whatever value CI had already set
        // (e.g. ConnectionStrings__DefaultConnection pointing at the container-hosted SQL
        // Server). Environment variables are process-global, so any fixture from a different
        // xUnit collection whose factory construction happens to run concurrently with — or
        // right after — this one's InitializeAsync/finally window could read the wiped value
        // instead of CI's real one. Capture the real prior value (including "key was unset", i.e.
        // null) for every key up front, and restore exactly that, not null.
        var priorValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var key in EnvironmentOverrides.Keys.Append("Storage__DataRoot"))
        {
            priorValues[key] = Environment.GetEnvironmentVariable(key);
        }

        foreach (var (key, value) in EnvironmentOverrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        Environment.SetEnvironmentVariable("Storage__DataRoot", _dataRoot);

        try
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
                });
            });
            Client = _factory.CreateClient();
        }
        finally
        {
            foreach (var (key, priorValue) in priorValues)
            {
                Environment.SetEnvironmentVariable(key, priorValue);
            }
        }
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(ConnectionString).Options;
        return new DoSelectDbContext(options);
    }

    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// Signs in as a real seeded admin user with the InventoryManager role — Movement/Case actor
    /// columns have a real FK to AspNetUsers, and the manual-release audit's role snapshot is read
    /// back from the real Roles／UserRoles tables (not the sign-in claims), so the role rows are
    /// seeded too — same shape as ProductImportsApiFixture.
    /// </summary>
    public Task<HttpClient> CreateAuthenticatedInventoryManagerClientAsync() =>
        CreateAuthenticatedAdminClientAsync(DoSelectRoles.InventoryManager);

    public async Task<HttpClient> CreateAuthenticatedAdminClientAsync(params string[] roles)
    {
        string adminUserId;
        await using (var context = CreateContext())
        {
            var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
            context.Users.Add(admin);
            await context.SaveChangesAsync();
            adminUserId = admin.Id;

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
        }

        var client = CreateClient();
        var token = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new
            {
                includeMfa = true,
                roles,
                userId = adminUserId,
            }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Signs in as an admin with no InventoryManager role — used to prove the policy actually blocks.</summary>
    public async Task<HttpClient> CreateAuthenticatedUnrelatedAdminClientAsync()
    {
        var client = CreateClient();
        var token = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa = true, roles = Array.Empty<string>() }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    public static async Task<string> GetAdminAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "admin");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    public static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(HttpClient client, HttpRequestMessage request)
    {
        request.Headers.Add("X-XSRF-TOKEN", await GetAdminAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public async Task<Sku> SeedSkuWithBalanceAsync(int onHandQuantity)
    {
        await using var context = CreateContext();
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

        context.InventoryBalances.Add(new InventoryBalance(Guid.CreateVersion7(), sku.Id, onHandQuantity, reorderLevel: 0, now));
        await context.SaveChangesAsync();

        return sku;
    }

    /// <summary>
    /// Seeds one InventoryMovement of the given type against an existing balance. CostChange is the
    /// reason this exists (組長 PR #36 ruling A1): it is written by the SKU cost-change flow with
    /// zero quantity deltas, so it cannot be produced through any of the reservation service paths
    /// the other fixtures use.
    /// </summary>
    public async Task<Guid> SeedMovementAsync(long skuId, string movementType, string reasonCode)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        var balance = await context.InventoryBalances.SingleAsync(candidate => candidate.SkuId == skuId);
        var sku = await context.Skus.SingleAsync(candidate => candidate.Id == skuId);
        var publicId = Guid.CreateVersion7();

        context.InventoryMovements.Add(new InventoryMovement(
            publicId,
            skuId,
            reservationId: null,
            movementType,
            onHandDelta: 0,
            reservedDelta: 0,
            beforeOnHand: balance.OnHandQuantity,
            afterOnHand: balance.OnHandQuantity,
            beforeReserved: balance.ReservedQuantity,
            afterReserved: balance.ReservedQuantity,
            unitCostSnapshot: sku.UnitCost,
            reasonCode,
            referenceType: "Sku",
            referencePublicId: sku.PublicId,
            actorUserId: null,
            occurredAtUtc: now));
        await context.SaveChangesAsync();

        return publicId;
    }

    /// <summary>直接種一筆 Open 案件（Expected＝Balance 快照、Actual＝帳本重算），不跑偵測排程。</summary>
    public async Task<(Guid CasePublicId, byte[] RowVersion)> SeedReconciliationCaseAsync(
        long skuId, int expectedOnHand, int actualOnHand, int expectedReserved = 0, int actualReserved = 0)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        var reconciliationCase = new InventoryReconciliationCase(
            Guid.CreateVersion7(), skuId, expectedOnHand, actualOnHand, expectedReserved, actualReserved, now, now);
        context.InventoryReconciliationCases.Add(reconciliationCase);
        await context.SaveChangesAsync();
        return (reconciliationCase.PublicId, reconciliationCase.RowVersion);
    }

    public async Task<(Guid ReservationPublicId, byte[] RowVersion)> SeedActiveReservationAsync(long skuId, int quantity)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), UniqueCode("PROVIDER"), version: 1, status: "Active",
            effectiveFromUtc: now.AddDays(-1), effectiveToUtc: null, configurationJson: "{}", schemaVersion: 1, now);
        context.ShippingProviderProfiles.Add(shippingProfile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), shippingProfile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, now);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.CreateVersion7(),
            new OrderCreation(
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
                PackageSnapshot: new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 1000m)),
            now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var balance = await context.InventoryBalances.SingleAsync(b => b.SkuId == skuId);
        balance.ApplyQuantities(balance.OnHandQuantity, balance.ReservedQuantity + quantity, now);
        var reservation = new InventoryReservation(Guid.CreateVersion7(), skuId, order.Id, quantity, now.AddMinutes(15), now);
        context.InventoryReservations.Add(reservation);
        await context.SaveChangesAsync();

        return (reservation.PublicId, reservation.RowVersion);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
