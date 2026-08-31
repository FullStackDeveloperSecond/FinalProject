using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Support;

[CollectionDefinition(nameof(SupportAuditSqlServerCollection), DisableParallelization = true)]
public sealed class SupportAuditSqlServerCollection : ICollectionFixture<SupportAuditSqlServerFixture>;

/// <summary>
/// Provides the support write-path acceptance tests with an isolated, fully migrated database.
/// Those tests assert central audit rows and must not depend on the developer's ambient
/// <c>DoSelectDb</c> having received every migration.
/// </summary>
public sealed class SupportAuditSqlServerFixture : IAsyncLifetime
{
    private readonly string _connectionString = SqlServerTestConnection.Build(
        $"DoSelectSupportAuditTests_{Guid.NewGuid():N}");
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
            ["GuestOrderAccess__Pepper"] = "support-audit-tests-guest-pepper-32-bytes",
            ["Idempotency__ActorScopePepper"] = "support-audit-tests-actor-pepper-32-bytes",
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
