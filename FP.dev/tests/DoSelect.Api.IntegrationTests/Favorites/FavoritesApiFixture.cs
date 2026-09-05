using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Favorites;

[CollectionDefinition(nameof(FavoritesApiCollection), DisableParallelization = true)]
public sealed class FavoritesApiCollection : ICollectionFixture<FavoritesApiFixture>;

/// <summary>
/// Same shape as SupportAuditSqlServerFixture: an isolated, fully migrated database so these
/// tests don't depend on the developer's ambient DoSelectDb.
/// </summary>
public sealed class FavoritesApiFixture : IAsyncLifetime
{
    private readonly string _connectionString = SqlServerTestConnection.Build(
        $"DoSelectFavoritesApiTests_{Guid.NewGuid():N}");
    private EnvironmentOverrideScope? _environment;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _environment = new EnvironmentOverrideScope(new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] = _connectionString,
            ["Observability__FileLoggingEnabled"] = "false",
            ["Features__AiEnabled"] = "false",
            ["Features__EmailEnabled"] = "false",
            ["Demo__SimulationEndpointsEnabled"] = "false",
            ["GuestOrderAccess__Pepper"] = "favorites-api-tests-guest-pepper-32-bytes",
            ["Idempotency__ActorScopePepper"] = "favorites-api-tests-actor-pepper-32b",
        });

        await using (var context = CreateContext())
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.MigrateAsync();
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Factory is not null)
            {
                await Factory.DisposeAsync();
            }

            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
        finally
        {
            _environment?.Dispose();
        }
    }

    private DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new DoSelectDbContext(options);
    }
}
