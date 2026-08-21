using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using DoSelect.Infrastructure.Persistence;

namespace DoSelect.Api.IntegrationTests.Catalog;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for the public
/// <c>ProductsController</c>/<c>CatalogController</c> endpoints — all anonymous, so unlike
/// <c>Catalog.CatalogAdminApiFixture</c> this needs no sign-in helper. See that fixture's
/// remarks for why environment variables, not ConfigureAppConfiguration, are the only
/// override mechanism that reaches Program.cs's eagerly-read config keys (same eager-read
/// gotcha applies here). Database is reset once per collection, not per test.
/// </summary>
public sealed class ProductsApiFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectProductsApiTests;Trusted_Connection=True;" +
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
        "DoSelectProductsApiTests",
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
                builder.UseEnvironment("Development"));
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

    /// <summary>Opens a fresh <see cref="DoSelectDbContext"/> to seed data this read-only API has no write endpoint for.</summary>
    public DoSelectDbContext CreateScopedContext() => CreateContext();

    public static async Task<(int Status, string? Code, JsonElement Root)> ReadProblemAsync(
        HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement.Clone();
        var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        return ((int)response.StatusCode, code, root);
    }

    // Guid.NewGuid() (random), not Guid.CreateVersion7() (time-ordered), because
    // CreateVersion7's leading hex characters encode a millisecond timestamp and can
    // collide when called more than once within the same millisecond.
    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

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

[CollectionDefinition(nameof(ProductsApiCollection))]
public sealed class ProductsApiCollection : ICollectionFixture<ProductsApiFixture>;
