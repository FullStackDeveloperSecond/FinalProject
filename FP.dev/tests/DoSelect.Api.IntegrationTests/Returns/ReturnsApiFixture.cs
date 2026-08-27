using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Files;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Returns;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for the Returns/Orders
/// customer-facing HTTP surface — mirrors CartApiFixture's pattern (env-var overrides, not
/// ConfigureAppConfiguration, since Program.cs reads config eagerly; test-only sign-in endpoint
/// via SecurityFoundationTestController's application part). Database is reset once per
/// collection, not per test — every test must seed its own order/return rows.
/// </summary>
public sealed class ReturnsApiFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable = "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelectReturnsApiTests;Trusted_Connection=True;TrustServerCertificate=True;";
    private static readonly string ConnectionString = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? LocalConnectionString)
    {
        InitialCatalog = $"DoSelectReturnsApiTests_{Guid.NewGuid():N}",
    }.ConnectionString;

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
        ["Idempotency__ActorScopePepper"] = "returns-api-tests-actor-scope-pepper-0000",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectReturnsApiTests",
        Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();

        var previousEnvironment = EnvironmentOverrides.Keys
            .Append("Storage__DataRoot")
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);

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
                    services.RemoveAll<IFileScanner>();
                    services.AddSingleton<IFileScanner>(new CleanFileScanner());
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
            foreach (var (key, value) in previousEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private sealed class CleanFileScanner : IFileScanner
    {
        public Task<FileScanResult> ScanAsync(
            string quarantinedFilePath,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new FileScanResult(
                FileScanOutcome.Clean,
                nameof(CleanFileScanner),
                now,
                now));
        }
    }
    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    public DoSelectDbContext CreateScopedContext() => CreateContext();

    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// A fresh client signed in as a member via the test-only sign-in endpoint, plus a real
    /// delivered Order + OrderItem owned by that exact member (Orders.MemberUserId is a real
    /// AspNetUsers FK). Returns the client together with the seeded ids the test needs to call
    /// the Returns HTTP endpoints.
    /// </summary>
    public async Task<(HttpClient Client, string MemberUserId, Guid OrderPublicId, Guid OrderItemPublicId, byte[] OrderRowVersion)>
        CreateAuthenticatedMemberWithDeliveredOrderAsync(int returnableQuantity = 1)
    {
        string memberUserId;
        Guid orderPublicId;
        Guid orderItemPublicId;
        byte[] orderRowVersion;
        var nowUtc = DateTime.UtcNow;
        await using (var context = CreateContext())
        {
            var member = ApplicationUser.CreateMember(
                Guid.CreateVersion7(),
                $"{Guid.NewGuid():N}@doselect.test",
                nowUtc);
            context.Users.Add(member);
            await context.SaveChangesAsync();
            memberUserId = member.Id;

            var shippingProfile = new ShippingProviderProfile(
                Guid.CreateVersion7(), $"HOME-{Guid.NewGuid():N}"[..20], 1, "Active",
                null, null, "{}", 1, nowUtc);
            context.Set<ShippingProviderProfile>().Add(shippingProfile);
            await context.SaveChangesAsync();

            var order = Order.Create(Guid.CreateVersion7(), ValidOrderCreation(memberUserId, shippingProfile.Id), nowUtc);
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            order.ApplyFulfillmentProjection(FulfillmentStatus.Delivered, nowUtc.AddDays(-1));
            await context.SaveChangesAsync();

            var item = new OrderItem(
                Guid.CreateVersion7(), order.Id, skuId: null, "SKU-1", "27型螢幕", "27型螢幕 White",
                quantity: returnableQuantity, listUnitPrice: 100m, saleUnitPrice: 100m, finalUnitPrice: 100m,
                unitCostSnapshot: 60m, lineSubtotal: 100m * returnableQuantity, discountAllocation: 0m,
                lineTotal: 100m * returnableQuantity, assemblyGroupKey: null, returnableQuantity: returnableQuantity,
                nowUtc, isCouponEligible: true);
            context.OrderItems.Add(item);
            await context.SaveChangesAsync();

            orderPublicId = order.PublicId;
            orderItemPublicId = item.PublicId;
            orderRowVersion = order.RowVersion;
        }

        var client = CreateClient();
        var signInToken = await GetMemberAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/member")
        {
            Content = JsonContent.Create(new { includeMfa = false, roles = Array.Empty<string>(), userId = memberUserId }),
        };
        request.Headers.Add("X-XSRF-TOKEN", signInToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (client, memberUserId, orderPublicId, orderItemPublicId, orderRowVersion);
    }

    /// <summary>
    /// Seeds a guest-owned ReturnRequest with <paramref name="itemCount"/> items directly via
    /// DbContext (bypassing HTTP — D1's validation tests only need a return already sitting at a
    /// given status, not a proof of the full customer-facing create flow, which
    /// ReturnsMemberHttpTests already covers), driven forward to
    /// <paramref name="targetStatus"/> using the real Domain transition methods. Optionally also
    /// creates a Pending ReturnShipment when <paramref name="withShipment"/> is true.
    /// </summary>
    public async Task<(Guid ReturnPublicId, byte[] RowVersion, IReadOnlyList<Guid> ItemPublicIds, Guid? ShipmentPublicId)>
        SeedReturnAsync(ReturnRequestStatus targetStatus, int itemCount = 1, bool withShipment = false)
    {
        var nowUtc = DateTime.UtcNow;
        await using var context = CreateContext();

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"HOME-{Guid.NewGuid():N}"[..20], 1, "Active", null, null, "{}", 1, nowUtc);
        context.Set<ShippingProviderProfile>().Add(shippingProfile);
        await context.SaveChangesAsync();

        var order = Order.Create(Guid.CreateVersion7(), ValidGuestOrderCreation(shippingProfile.Id), nowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        order.ApplyFulfillmentProjection(FulfillmentStatus.Delivered, nowUtc.AddDays(-10));
        await context.SaveChangesAsync();

        var returnRequest = new ReturnRequest(
            Guid.CreateVersion7(), $"RT-{Guid.NewGuid():N}"[..12], order.Id, requesterUserId: null,
            "Defective", "面板有亮點", policyVersion: 1, nowUtc);
        context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();

        var itemPublicIds = new List<Guid>();
        for (var i = 0; i < itemCount; i++)
        {
            var orderItem = new OrderItem(
                Guid.CreateVersion7(), order.Id, skuId: null, $"SKU-{i}", $"品項{i}", $"品項{i} White",
                quantity: 1, listUnitPrice: 100m, saleUnitPrice: 100m, finalUnitPrice: 100m,
                unitCostSnapshot: 60m, lineSubtotal: 100m, discountAllocation: 0m, lineTotal: 100m,
                assemblyGroupKey: null, returnableQuantity: 1, nowUtc, isCouponEligible: true);
            context.OrderItems.Add(orderItem);
            await context.SaveChangesAsync();

            var item = new ReturnItem(Guid.CreateVersion7(), returnRequest.Id, orderItem.Id, 1, 0m, "NotInspected", nowUtc);
            context.ReturnItems.Add(item);
            await context.SaveChangesAsync();
            itemPublicIds.Add(item.PublicId);
        }

        foreach (var status in TransitionPath(targetStatus))
        {
            returnRequest.Transition(status, nowUtc);
        }
        await context.SaveChangesAsync();

        Guid? shipmentPublicId = null;
        if (withShipment)
        {
            var shipment = new ReturnShipment(
                Guid.CreateVersion7(), returnRequest.Id, $"RS-{Guid.NewGuid():N}"[..12], ReturnShipmentMethod.SelfShip,
                carrierCode: null, trackingNumber: null,
                recipientName: null, recipientPhone: null, postalCode: null, addressLine: null,
                storeCode: null, storeName: null, nowUtc);
            context.Set<ReturnShipment>().Add(shipment);
            await context.SaveChangesAsync();
            shipmentPublicId = shipment.PublicId;
        }

        return (returnRequest.PublicId, returnRequest.RowVersion, itemPublicIds, shipmentPublicId);
    }

    /// <summary>The Allowed-transition path from Requested to the target status, per
    /// ReturnRequest's own state graph (never skips a step Transition itself would reject).</summary>
    private static IEnumerable<ReturnRequestStatus> TransitionPath(ReturnRequestStatus target)
    {
        if (target == ReturnRequestStatus.Requested)
        {
            yield break;
        }

        var fullPath = new[]
        {
            ReturnRequestStatus.UnderReview,
            ReturnRequestStatus.Approved,
            ReturnRequestStatus.AwaitingShipment,
            ReturnRequestStatus.InTransit,
            ReturnRequestStatus.Received,
        };
        foreach (var status in fullPath)
        {
            yield return status;
            if (status == target)
            {
                yield break;
            }
        }
    }

    private static OrderCreation ValidGuestOrderCreation(long shippingProviderProfileId) =>
        new(
            $"DS{Guid.NewGuid():N}"[..15],
            null,
            $"{Guid.NewGuid():N}@doselect.test",
            OrderStatus.Processing,
            PaymentStatus.Paid,
            FulfillmentStatus.Preparing,
            AssemblyStatus.NotRequired,
            1_200m, 100m, 225m, 0m, 1_325m,
            "Guest", "0912345678", "guest@example.com",
            "100", "Taipei", "Zhongzheng", "No. 1", null,
            "HOME_DELIVERY", shippingProviderProfileId, null, null, null,
            1, 1, null, null, $"checkout-{Guid.NewGuid():N}", null);

    /// <summary>
    /// A fresh client signed in as an admin with the OrderManager role — satisfies the
    /// Return.Approve policy every AdminReturnsController action requires. Seeds a real
    /// ApplicationUser row and signs in as that exact id: ReturnRequests.ReviewedByAdminUserId
    /// has a foreign key to AspNetUsers, so an arbitrary test-only identifier (the sign-in
    /// endpoint's random-GUID fallback) fails with a 500 the moment Review/Reject writes it.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedOrderManagerClientAsync()
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
        var signInToken = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            // Return.Approve's policy also requires the MultiFactor authentication-method claim.
            Content = JsonContent.Create(new { includeMfa = true, roles = new[] { "OrderManager" }, userId = adminUserId }),
        };
        request.Headers.Add("X-XSRF-TOKEN", signInToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    public static async Task<string> GetAdminAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, "admin");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    public static async Task<HttpResponseMessage> SendWithAdminAntiforgeryAsync(HttpClient client, HttpRequestMessage request)
    {
        request.Headers.Add("X-XSRF-TOKEN", await GetAdminAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    public static async Task<string> GetMemberAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, "member");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    public static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(HttpClient client, HttpRequestMessage request)
    {
        request.Headers.Add("X-XSRF-TOKEN", await GetMemberAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    public static async Task<(int Status, string? Code, JsonElement Root)> ReadProblemAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement.Clone();
        var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        return ((int)response.StatusCode, code, root);
    }

    private static OrderCreation ValidOrderCreation(string memberUserId, long shippingProviderProfileId) =>
        new(
            $"DS{Guid.NewGuid():N}"[..15],
            memberUserId,
            null,
            OrderStatus.Processing,
            PaymentStatus.Paid,
            FulfillmentStatus.Preparing,
            AssemblyStatus.NotRequired,
            1_200m,
            100m,
            225m,
            0m,
            1_325m,
            "Member",
            "0912345678",
            "member@example.com",
            "100",
            "Taipei",
            "Zhongzheng",
            "No. 1",
            null,
            "HOME_DELIVERY",
            shippingProviderProfileId,
            null,
            null,
            null,
            1,
            1,
            null,
            null,
            $"checkout-{Guid.NewGuid():N}",
            null);

    private static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}

[CollectionDefinition(nameof(ReturnsApiCollection))]
public sealed class ReturnsApiCollection : ICollectionFixture<ReturnsApiFixture>;
