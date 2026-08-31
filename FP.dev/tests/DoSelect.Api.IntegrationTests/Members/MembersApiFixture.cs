using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Members;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for MembersController,
/// mirroring CartApiFixture's pattern (env-var overrides, not ConfigureAppConfiguration).
/// GuestOrderAccess__Pepper is required even though these tests never touch guest order access —
/// db7b17f's ValidateOnStart() on GuestOrderAccessOptions runs for every host startup.
/// </summary>
public sealed class MembersApiFixture : IAsyncLifetime
{
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectMembersApiTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
        ["Idempotency__ActorScopePepper"] = "members-api-tests-idempotency-pepper-0000",
        ["GuestOrderAccess__Pepper"] = "members-api-tests-guest-order-access-pepper",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectMembersApiTests",
        Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();

        var previousEnvironment = EnvironmentOverrides.Keys
            .Append("Storage__DataRoot")
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);

        foreach (var (key, value) in EnvironmentOverrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        Environment.SetEnvironmentVariable("Storage__DataRoot", _dataRoot);

        try
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
            // WebApplicationFactory builds the host lazily on first Services/CreateClient access —
            // force it now, while the environment overrides below are still active, or Program.cs
            // reads the restored (wrong) values once a test later calls CreateClient().
            using var warmup = _factory.CreateClient();
        }
        finally
        {
            foreach (var (key, value) in previousEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    public DoSelectDbContext CreateScopedContext() => CreateContext();

    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>A fresh client signed in as a member with a real ApplicationUser + MemberProfile
    /// pair (matching what RegisterMemberService actually creates) — MembersController's Gateway
    /// requires both rows to exist.</summary>
    public async Task<(HttpClient Client, string MemberUserId)> CreateAuthenticatedMemberClientAsync()
    {
        string memberUserId;
        await using (var context = CreateContext())
        {
            var nowUtc = DateTime.UtcNow;
            var member = ApplicationUser.CreateMember(
                Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", nowUtc);
            context.Users.Add(member);
            await context.SaveChangesAsync();

            context.MemberProfiles.Add(new MemberProfile(
                member.Id, Guid.CreateVersion7(), "測試會員", null, nowUtc));
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
        request.Headers.Add(SecurityController.ClientHeaderName, "member");
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

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(ConnectionString).Options);

    private static async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}

[CollectionDefinition(nameof(MembersApiCollection))]
public sealed class MembersApiCollection : ICollectionFixture<MembersApiFixture>;
