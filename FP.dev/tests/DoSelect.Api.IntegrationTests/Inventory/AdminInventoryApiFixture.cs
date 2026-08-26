using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
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
            foreach (var key in EnvironmentOverrides.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
            Environment.SetEnvironmentVariable("Storage__DataRoot", null);
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

    /// <summary>Signs in as a real seeded admin user with the InventoryManager role — Movement/Case actor columns have a real FK to AspNetUsers.</summary>
    public async Task<HttpClient> CreateAuthenticatedInventoryManagerClientAsync()
    {
        string adminUserId;
        await using (var context = CreateContext())
        {
            var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
            context.Users.Add(admin);
            await context.SaveChangesAsync();
            adminUserId = admin.Id;
        }

        var client = CreateClient();
        var token = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new
            {
                includeMfa = true,
                roles = new[] { DoSelectRoles.InventoryManager },
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

    public async Task<(Guid ReservationPublicId, byte[] RowVersion)> SeedActiveReservationAsync(long skuId, int quantity)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), UniqueCode("PROVIDER"), version: 1, status: "Active",
            effectiveFromUtc: now.AddDays(-1), effectiveToUtc: null, configurationJson: "{}", schemaVersion: 1, now);
        context.ShippingProviderProfiles.Add(shippingProfile);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.CreateVersion7(),
            new OrderCreation(
                UniqueCode("ORD"), null, $"{Guid.NewGuid():N}@doselect.test",
                OrderStatus.PendingPayment, PaymentStatus.Pending, FulfillmentStatus.Pending, AssemblyStatus.NotRequired,
                1000m, 0m, 0m, 0m, 1000m,
                "測試收件人", "0912345678", "recipient@doselect.test",
                "100", "台北市", "中正區", "測試路 1 號", null,
                "home_delivery", shippingProfile.Id, null, null, null,
                1, 1, null, now.AddMinutes(15), UniqueCode("IDEMP"), null),
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
