using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Favorites;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for
/// <c>FavoritesController</c>, mirroring <c>Shopping.CartApiFixture</c>'s pattern. Database is
/// reset once per collection, not per test — every test must use its own signed-in member and
/// its own product to avoid cross-test favorite collisions.
/// </summary>
public sealed class FavoritesApiFixture : IAsyncLifetime
{
    private static readonly string ConnectionString =
        global::DoSelect.Api.IntegrationTests.SqlServerTestConnection.Build("DoSelectFavoritesApiTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
        ["Idempotency__ActorScopePepper"] = "favorites-api-tests-actor-scope-pepper-0000",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectFavoritesApiTests",
        Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();

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

    /// <summary>Opens a fresh <see cref="DoSelectDbContext"/> for seeding products/SKUs/inventory the Favorites HTTP API has no write endpoint for.</summary>
    public DoSelectDbContext CreateScopedContext() => CreateContext();

    /// <summary>A brand-new client with its own cookie jar and no identity attached.</summary>
    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// A fresh client signed in as a member via the test-only sign-in endpoint. Seeds a real
    /// <c>ApplicationUser</c> row first and signs in as that exact id — <c>Favorites.MemberUserId</c>
    /// has a foreign key to <c>AspNetUsers</c>, so an arbitrary fake identifier would fail with a
    /// 500 the moment a favorite is created.
    /// </summary>
    public async Task<(HttpClient Client, string MemberUserId)> CreateAuthenticatedMemberClientAsync()
    {
        string memberUserId;
        await using (var context = CreateContext())
        {
            var member = ApplicationUser.CreateMember(
                Guid.CreateVersion7(),
                $"{Guid.NewGuid():N}@doselect.test",
                DateTime.UtcNow);
            context.Users.Add(member);
            await context.SaveChangesAsync();
            memberUserId = member.Id;
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
        return (client, memberUserId);
    }

    public static async Task<string> GetMemberAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "member");
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

[CollectionDefinition(nameof(FavoritesApiCollection))]
public sealed class FavoritesApiCollection : ICollectionFixture<FavoritesApiFixture>;
