using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Admin;

/// <summary>
/// Gives the admin-auth HTTP tests their own disposable SQL Server database. The tests create
/// Identity users, roles and audit rows, so running them against appsettings.json's shared
/// DoSelectDb would leave local development data behind.
/// </summary>
/// <remarks>
/// Program.cs reads the connection string while the entry point is bootstrapping. Derived
/// WebApplicationFactory instances are created throughout this collection to isolate cookies
/// and rate-limit state, so the environment override must remain active for the fixture's whole
/// lifetime. API integration-test parallelization is disabled at assembly level, preventing this
/// process-wide override from racing another collection.
/// </remarks>
public sealed class AdminAuthApiFixture : IAsyncLifetime
{
    private static readonly string DatabaseName =
        $"DoSelectAdminAuthApiTests_{Guid.NewGuid():N}";

    private static readonly string ConnectionString =
        global::DoSelect.Api.IntegrationTests.SqlServerTestConnection.Build(DatabaseName);

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides =
        new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] = ConnectionString,
            ["Observability__FileLoggingEnabled"] = "false",
            ["Features__AiEnabled"] = "false",
            ["Features__EmailEnabled"] = "false",
            ["Demo__SimulationEndpointsEnabled"] = "false",
        };

    private Dictionary<string, string?> _previousEnvironment = [];

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using (var context = CreateContext())
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
        }

        _previousEnvironment = EnvironmentOverrides.Keys
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);
        foreach (var (key, value) in EnvironmentOverrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));
    }

    public async Task DisposeAsync()
    {
        try
        {
            await Factory.DisposeAsync();

            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
        finally
        {
            foreach (var (key, value) in _previousEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }
}

[CollectionDefinition(nameof(AdminAuthApiCollection))]
public sealed class AdminAuthApiCollection : ICollectionFixture<AdminAuthApiFixture>;
