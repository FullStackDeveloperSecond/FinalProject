using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Files;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Catalog;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for the 5 admin
/// Catalog controllers, so HTTP-layer concerns (routing, model binding, JSON
/// (de)serialization, ProblemDetails status codes) get exercised end-to-end — the
/// existing DoSelect.Infrastructure.Tests coverage for these services never leaves
/// the process and can't catch those. Database is reset once per collection, not per
/// test, so every test must use <see cref="UniqueCode"/> to avoid collisions.
/// </summary>
/// <remarks>
/// Program.cs reads several config keys (notably ConnectionStrings:DefaultConnection,
/// via AddDoSelectPersistence) EAGERLY at top-level statement execution, before
/// WebApplicationFactory's WithWebHostBuilder(...).ConfigureAppConfiguration(...)
/// customization is guaranteed to be visible to that code — confirmed empirically:
/// an AddInMemoryCollection override there left the app silently talking to the
/// real appsettings.json connection string (DoSelectDb) while IConfiguration itself
/// reported the override correctly when queried later via DI. Environment variables,
/// by contrast, are already part of the process when Program.cs's own
/// WebApplication.CreateBuilder(args) reads them, so they're the only override
/// mechanism proven to reach eagerly-read settings. This assembly has
/// [CollectionBehavior(DisableTestParallelization = true)] (AssemblyInfo.cs), so no
/// other collection runs concurrently while these env vars are set.
/// </remarks>
public sealed class CatalogAdminApiFixture : IAsyncLifetime
{
    private static readonly string ConnectionString =
        global::DoSelect.Api.IntegrationTests.SqlServerTestConnection.Build("DoSelectApiAdminTests");

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
        "DoSelectApiAdminTests",
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
                    // Ephemeral keys, not the real (Production-only) persisted ring — matches
                    // SecurityFoundationTests.CreateFactory, needed for antiforgery/cookie auth
                    // to work at all inside a test host.
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    // Exposes the test-only /__tests/security/sign-in/{accountType} endpoint
                    // (defined in SecurityFoundationTests.cs) so these tests can authenticate
                    // as a CatalogManager/SuperAdmin admin without needing real Identity users.
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
                    // 商品圖片上傳走真的 LocalImageStorage（Storage__DataRoot 指到暫存目錄），只把
                    // Defender 換成「一律乾淨」的掃描器——CI 沒有 Defender，不換就是 503。
                    services.RemoveAll<IFileScanner>();
                    services.AddSingleton<IFileScanner>(new CleanFileScanner());
                });
            });
            Client = _factory.CreateClient();
        }
    }

    private sealed class CleanFileScanner : IFileScanner
    {
        public Task<FileScanResult> ScanAsync(string quarantinedFilePath, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new FileScanResult(FileScanOutcome.Clean, "Synthetic scanner", now, now));
        }
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    /// <summary>Opens a fresh <see cref="DoSelectDbContext"/> for seeding data the admin HTTP API has no endpoint for (e.g. inventory balances).</summary>
    public DoSelectDbContext CreateScopedContext() => CreateContext();

    /// <summary>A brand-new, unauthenticated client with its own cookie jar — use for 401/anonymous scenarios.</summary>
    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// A fresh client signed in as an admin (with the MFA claim admin policies require —
    /// see SecurityFoundationTests.AdminCookie_WithoutMfaClaim_IsForbidden) holding the given
    /// roles. Defaults to CatalogManager, the role these controllers' policy actually grants.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedAdminClientAsync(params string[] roles)
    {
        var client = CreateClient();
        var effectiveRoles = roles.Length > 0 ? roles : [DoSelectRoles.CatalogManager];
        var signInToken = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa = true, roles = effectiveRoles }),
        };
        request.Headers.Add("X-XSRF-TOKEN", signInToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>
    /// 和 <see cref="CreateAuthenticatedAdminClientAsync"/> 的差別：這支會先在資料庫建立一個真的
    /// Admin 使用者與角色列，再用那個 userId 登入。批次動作要寫中央 Audit，而 Audit 的 Actor 必須
    /// 對得上一位真實管理員（EfProductAdminService.ResolveActorAsync），只有 Cookie 上的 Claim 是
    /// 不夠的。形狀比照 ProductImportsApiFixture.CreateAuthenticatedCatalogManagerClientAsync。
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedAdminClientWithIdentityAsync(params string[] roles)
    {
        var effectiveRoles = roles.Length > 0 ? roles : [DoSelectRoles.CatalogManager];
        string adminUserId;
        await using (var context = CreateContext())
        {
            var admin = ApplicationUser.CreateAdmin(
                Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
            context.Users.Add(admin);
            await context.SaveChangesAsync();
            adminUserId = admin.Id;

            foreach (var roleName in effectiveRoles)
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
        var signInToken = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa = true, roles = effectiveRoles, userId = adminUserId }),
        };
        request.Headers.Add("X-XSRF-TOKEN", signInToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Fetches a fresh antiforgery token bound to the admin scheme for the given (already-signed-in-or-not) client.</summary>
    public static async Task<string> GetAdminAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "admin");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    /// <summary>Attaches a fresh admin antiforgery token to an unsafe (POST/PUT/DELETE) request just before sending it.</summary>
    public static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(HttpClient client, HttpRequestMessage request)
    {
        request.Headers.Add("X-XSRF-TOKEN", await GetAdminAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    // Guid.NewGuid() (random), not Guid.CreateVersion7() (time-ordered), because
    // CreateVersion7's leading hex characters encode a millisecond timestamp and can
    // collide when called more than once within the same millisecond.
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

[CollectionDefinition(nameof(CatalogAdminApiCollection))]
public sealed class CatalogAdminApiCollection : ICollectionFixture<CatalogAdminApiFixture>;
