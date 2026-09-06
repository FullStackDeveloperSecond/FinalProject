using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// Gives the M-14B SQL Server read-model tests a fresh database for every test run. The SLA
/// supervisor query intentionally has no keyword filter and returns at most 100 rows, so using
/// the shared development database would make repeated local runs depend on historical fixtures.
/// </summary>
public sealed class M14BReadModelSqlServerFixture : IAsyncLifetime
{
    private readonly string _connectionString = SqlServerTestConnection.Build(
        $"DoSelectE2E_{Guid.NewGuid():N}") + ";Encrypt=False;";
    private readonly Dictionary<string, string?> _previousEnvironment = [];

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var overrides = new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] = _connectionString,
            ["Observability__FileLoggingEnabled"] = "false",
            ["Features__AiEnabled"] = "false",
            ["Features__EmailEnabled"] = "false",
            ["Demo__SimulationEndpointsEnabled"] = "false",
            ["GuestOrderAccess__Pepper"] = "m14-read-model-tests-pepper-32-bytes",
        };

        foreach (var (key, value) in overrides)
        {
            _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

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
            foreach (var (key, value) in _previousEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
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
