using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Shipping;

/// <summary>Same shape as Shopping.CartApiFixture — see its remarks for why environment
/// variables (not ConfigureAppConfiguration) are the only override mechanism that reaches
/// Program.cs's eagerly-read config keys.</summary>
public sealed class ShippingApiFixture : IAsyncLifetime
{
    // Same defect 組長 caught on PR #47 and PR #36 already fixed for the Inventory
    // fixtures: a hardcoded local instance passes locally, but CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION, so every test here
    // failed with a connection error in CI. Route through the shared helper instead.
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectShippingApiTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
        ["Idempotency__ActorScopePepper"] = "shipping-api-tests-actor-scope-pepper-0000",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "DoSelectShippingApiTests", Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();

        // 組長 PR #34 round-5 review, item 1 (see EnvironmentOverrideScope's remarks): clearing
        // these keys to null on the way out deletes CI's own job-level
        // ConnectionStrings__DefaultConnection from the whole test process — this assembly runs
        // sequentially, so every later fixture without its own override (LoginControllerTests et
        // al.) then falls back to the Windows-only ".\SQL2025" default and fails on the Linux
        // runner. The scope restores each key's actual prior value instead.
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

    public static string UniqueGuestKey() => $"guest-{Guid.NewGuid():N}";

    /// <summary>A brand-new, unauthenticated client — use for 401/anonymous scenarios.</summary>
    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>Same shape as Catalog.CatalogAdminApiFixture.CreateAuthenticatedAdminClientAsync.</summary>
    public async Task<HttpClient> CreateAuthenticatedAdminClientAsync(params string[] roles)
    {
        var client = CreateClient();
        var signInToken = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { includeMfa = true, roles }),
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

    /// <summary>Attaches a fresh admin antiforgery token to an unsafe (POST/PUT) request just before sending it.</summary>
    public static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(HttpClient client, HttpRequestMessage request)
    {
        request.Headers.Add("X-XSRF-TOKEN", await GetAdminAntiforgeryTokenAsync(client));
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

    private static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(ConnectionString).Options;
        return new DoSelectDbContext(options);
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}

[CollectionDefinition(nameof(ShippingApiCollection))]
public sealed class ShippingApiCollection : ICollectionFixture<ShippingApiFixture>;
