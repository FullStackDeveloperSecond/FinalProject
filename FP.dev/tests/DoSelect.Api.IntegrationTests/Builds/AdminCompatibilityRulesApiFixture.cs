using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Builds;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for
/// <c>AdminCompatibilityRulesController</c>, combining <c>BuildListsApiFixture</c>'s
/// component-category seeding with an admin sign-in helper (mirrors
/// <c>Shopping.CartApiFixture.CreateAuthenticatedMemberClientAsync</c>, but for the Admin scheme).
/// </summary>
public sealed class AdminCompatibilityRulesApiFixture : IAsyncLifetime
{
    // 組長 PR #34: was hardcoded to the local ".\SQL2025" instance — CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION (SQL auth, localhost:1433).
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectAdminCompatibilityRulesApiTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectAdminCompatibilityRulesApiTests",
        Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();
        await SeedReferenceCategoryAsync();

        var allOverrides = new Dictionary<string, string>(EnvironmentOverrides)
        {
            ["Storage__DataRoot"] = _dataRoot,
        };

        using (new EnvironmentOverrideScope(allOverrides))
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
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public DoSelectDbContext CreateScopedContext() => CreateContext();

    public HttpClient CreateClient() => _factory.CreateClient();

    public async Task<HttpClient> CreateAuthenticatedAdminClientAsync(params string[] roles)
    {
        string adminUserId;
        await using (var context = CreateContext())
        {
            var admin = ApplicationUser.CreateAdmin(
                Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
            context.Users.Add(admin);
            await context.SaveChangesAsync();
            adminUserId = admin.Id;

            // DEC-BATCH-026 (DEC-P309): the `/__tests/security/sign-in/admin` shortcut only stamps
            // role claims onto the auth cookie — it never writes real AspNetUserRoles rows. That
            // was fine while nothing re-checked roles server-side, but
            // EfCompatibilityRuleAdminService's audit actor resolution now re-queries the real
            // Users/UserRoles/Roles tables (mirroring InvoiceAllowanceWriter's own pattern), so the
            // fixture needs to seed a real role assignment too, not just fake the claim.
            foreach (var roleName in roles)
            {
                var role = new IdentityRole(roleName);
                context.Roles.Add(role);
                await context.SaveChangesAsync();
                context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
            }

            await context.SaveChangesAsync();
        }

        var client = CreateClient();
        var signInToken = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa = true, roles, userId = adminUserId }),
        };
        request.Headers.Add("X-XSRF-TOKEN", signInToken);
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

    /// <summary>Creates one published Sku under the seeded StorageDevice category.</summary>
    public Task<Sku> SeedSkuAsync(decimal listPrice = 1000m) =>
        SeedSkuInCategoryAsync(CompatibilityCatalogContract.Categories.Storage, listPrice);

    /// <summary>Creates one published Sku under the given build-component category (must already be seeded by <see cref="SeedReferenceCategoryAsync"/>).</summary>
    public async Task<Sku> SeedSkuInCategoryAsync(string categoryCode, decimal listPrice = 1000m)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        var category = await context.Categories.SingleAsync(c => c.Code == categoryCode);

        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        product.ChangeStatus(ProductStatus.Published, now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), UniqueCode("SKU"), product.Id, "測試SKU", listPrice, listPrice * 0.6m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        return sku;
    }

    public static async Task<(int Status, string? Code, JsonElement Root)> ReadProblemAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement.Clone();
        var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        return ((int)response.StatusCode, code, root);
    }

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

    private static async Task SeedReferenceCategoryAsync()
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        foreach (var categoryCode in new[] { CompatibilityCatalogContract.Categories.Storage, CompatibilityCatalogContract.Categories.Motherboard })
        {
            context.Categories.Add(new Category(
                Guid.CreateVersion7(), categoryCode, $"slot-{categoryCode.ToLowerInvariant()}", categoryCode, null, now));
        }

        await context.SaveChangesAsync();
    }
}

[CollectionDefinition(nameof(AdminCompatibilityRulesApiCollection))]
public sealed class AdminCompatibilityRulesApiCollection : ICollectionFixture<AdminCompatibilityRulesApiFixture>;
