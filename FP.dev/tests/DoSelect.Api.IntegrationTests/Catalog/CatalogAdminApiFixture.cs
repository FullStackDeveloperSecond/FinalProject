using System.Text.Json;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

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
    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectApiAdminTests;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

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

        foreach (var (key, value) in EnvironmentOverrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        Environment.SetEnvironmentVariable("Storage__DataRoot", _dataRoot);

        try
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.UseEnvironment("Production"));
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

    /// <summary>Opens a fresh <see cref="DoSelectDbContext"/> for seeding data the admin HTTP API has no endpoint for (e.g. inventory balances).</summary>
    public DoSelectDbContext CreateScopedContext() => CreateContext();

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
