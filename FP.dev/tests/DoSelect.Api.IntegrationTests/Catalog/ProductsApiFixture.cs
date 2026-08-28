using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Application.Files;
using Microsoft.Extensions.DependencyInjection;

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
    private static readonly string ConnectionString =
        global::DoSelect.Api.IntegrationTests.SqlServerTestConnection.Build("DoSelectProductsApiTests");

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
    private readonly TestImageStorage _imageStorage = new();

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
                    services.AddSingleton<IImageStorage>(_imageStorage));
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

    /// <summary>Opens a fresh <see cref="DoSelectDbContext"/> to seed data this read-only API has no write endpoint for.</summary>
    public DoSelectDbContext CreateScopedContext() => CreateContext();

    public async Task WriteImageVariantAsync(
        string storageKey,
        string variantFileName,
        byte[] content)
    {
        await Task.CompletedTask;
        _imageStorage.Add(storageKey, variantFileName, content);
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

    private sealed class TestImageStorage : IImageStorage
    {
        private readonly Dictionary<(string StorageKey, ProductImageVariant Variant), byte[]> _files = [];

        public void Add(string storageKey, string variantFileName, byte[] content)
        {
            var variant = variantFileName switch
            {
                "320.webp" => ProductImageVariant.Small320,
                "800.webp" => ProductImageVariant.Medium800,
                "1600.webp" => ProductImageVariant.Large1600,
                _ => throw new ArgumentOutOfRangeException(nameof(variantFileName)),
            };
            _files[(storageKey, variant)] = content.ToArray();
        }

        public Task<ProductImageStoreResult> StoreAsync(
            ProductImageUpload upload,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(
            string storageKey,
            ProductImageVariant variant,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream?>(_files.TryGetValue((storageKey, variant), out var content)
                ? new MemoryStream(content, writable: false)
                : null);
        }

        public Task<bool> DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}

[CollectionDefinition(nameof(ProductsApiCollection))]
public sealed class ProductsApiCollection : ICollectionFixture<ProductsApiFixture>;
