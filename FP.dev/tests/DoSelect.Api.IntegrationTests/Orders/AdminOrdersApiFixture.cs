using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Orders;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for
/// AdminOrdersController — mirrors CatalogAdminApiFixture (see its remarks for why
/// environment-variable overrides, not ConfigureAppConfiguration, are required). Own
/// database (DoSelectApiAdminOrderTests) so this collection doesn't collide with the
/// Catalog admin test collection running in parallel.
/// </summary>
public sealed class AdminOrdersApiFixture : IAsyncLifetime
{
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectApiAdminOrderTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
        // Required since db7b17f (M-02 guest order access, merged into dev) added
        // ValidateOnStart() on GuestOrderAccessOptions — this fixture predates that merge and
        // doesn't itself exercise any guest-order-access endpoint, but the host still fails fast
        // on startup without a >=32 UTF-8 byte pepper.
        ["GuestOrderAccess__Pepper"] = "admin-orders-api-tests-pepper-32-bytes-000",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectApiAdminOrderTests",
        Guid.NewGuid().ToString("N"));

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

    /// <summary>Opens a fresh <see cref="DoSelectDbContext"/> for seeding Orders directly — there is no Checkout/order-creation API yet.</summary>
    public DoSelectDbContext CreateScopedContext() => CreateContext();

    /// <summary>A brand-new, unauthenticated client with its own cookie jar — use for 401/anonymous scenarios.</summary>
    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>A fresh client signed in as an admin holding the given roles (defaults to OrderManager, the role AdminOrdersController's policy grants).</summary>
    public Task<HttpClient> CreateAuthenticatedAdminClientAsync(params string[] roles) =>
        CreateAuthenticatedAdminClientForUserAsync(userId: null, roles);

    /// <summary>
    /// As above, but signs in with the given ApplicationUser.Id (see
    /// AdminOrdersApiSeeding.SeedAdminUserAsync) rather than a random, non-existent one. Required
    /// for any test that exercises an admin action recording ActorUserId — that column is a real
    /// FK to AspNetUsers, and the test-only sign-in endpoint mints a claims-only principal with no
    /// backing Identity row unless told which row to impersonate.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedAdminClientForUserAsync(string? userId, params string[] roles)
    {
        var client = CreateClient();
        var effectiveRoles = roles.Length > 0 ? roles : [DoSelectRoles.OrderManager];
        var signInToken = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa = true, roles = effectiveRoles, userId }),
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

    public static async Task<(int Status, string? Code, JsonElement Root)> ReadProblemAsync(
        HttpResponseMessage response)
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
}

[CollectionDefinition(nameof(AdminOrdersApiCollection))]
public sealed class AdminOrdersApiCollection : ICollectionFixture<AdminOrdersApiFixture>;
