using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests;

/// <summary>
/// SQL Server-backed API fixture for guest-order access. The database name is unique to this
/// fixture run and is deleted during disposal, so these tests never read or write the shared
/// development <c>DoSelectDb</c>. Environment overrides remain active for the fixture lifetime
/// because derived WebApplicationFactory instances can execute Program.cs lazily.
/// </summary>
public sealed class GuestOrderAccessApiFixture : IAsyncLifetime
{
    private readonly string _connectionString = SqlServerTestConnection.Build(
        $"DoSelectGuestOrderAccessApiTests_{Guid.NewGuid():N}");
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectGuestOrderAccessApiTests",
        Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _previousEnvironment = [];
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();

        var overrides = new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] = _connectionString,
            ["Storage__DataRoot"] = _dataRoot,
            ["Observability__FileLoggingEnabled"] = "false",
            ["Features__AiEnabled"] = "false",
            ["Features__EmailEnabled"] = "false",
            // Tests invoke the selected outbox consumer explicitly. A local Development
            // settings file must not start a dispatcher that also sends earlier test messages.
            ["Features__BackgroundJobsEnabled"] = "false",
            ["Demo__SimulationEndpointsEnabled"] = "false",
            ["GuestOrderAccess__Pepper"] = "guest-order-access-api-tests-pepper-32-bytes",
            ["Idempotency__ActorScopePepper"] = "guest-order-access-api-tests-idempotency-pepper",
            // TestServer reports one stable RemoteIpAddress. Keep provider-backed tests isolated
            // from each other's persistent IP budget while production defaults remain unchanged.
            ["RateLimiting__GuestOrderAccessIpPermitLimit"] = "1000",
            ["RateLimiting__GuestOrderAccessEmailPermitLimit"] = "1000",
            ["RateLimiting__GuestOrderAccessOrderLookupPermitLimit"] = "1000",
        };

        foreach (var (key, value) in overrides)
        {
            _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        _factory = new WebApplicationFactory<Program>();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _factory.DisposeAsync();
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

    public WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
                configureServices?.Invoke(services);
            });
        });

    private DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new DoSelectDbContext(options);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}

[CollectionDefinition(nameof(GuestOrderAccessApiCollection), DisableParallelization = true)]
public sealed class GuestOrderAccessApiCollection : ICollectionFixture<GuestOrderAccessApiFixture>;
