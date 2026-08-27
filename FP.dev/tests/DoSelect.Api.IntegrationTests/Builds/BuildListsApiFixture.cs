using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Builds;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{Program}"/> for
/// <c>BuildListsController</c>, combining <c>CompatibilityChecksApiFixture</c>'s
/// component-category seeding with <c>Shopping.CartApiFixture</c>'s member-auth helpers, since
/// every build-list route requires an authenticated member.
/// </summary>
public sealed class BuildListsApiFixture : IAsyncLifetime
{
    // 組長 PR #34: was hardcoded to the local ".\SQL2025" instance — CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION (SQL auth, localhost:1433).
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectBuildListsApiTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
        // EfIdempotencyExecutor (shared foundation, PR #32) requires >=32 UTF-8 bytes.
        ["Idempotency__ActorScopePepper"] = "build-lists-api-tests-actor-scope-pepper-0",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectBuildListsApiTests",
        Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();
        await SeedReferenceCategoryAsync();

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
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
                });
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

    public DoSelectDbContext CreateScopedContext() => CreateContext();

    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>Mirrors <c>Shopping.CartApiFixture.CreateAuthenticatedMemberClientAsync</c>.</summary>
    public async Task<HttpClient> CreateAuthenticatedMemberClientAsync()
    {
        string memberUserId;
        await using (var context = CreateContext())
        {
            var member = ApplicationUser.CreateMember(
                Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
            context.Users.Add(member);
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
        return client;
    }

    public static async Task<string> GetMemberAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "member");
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

    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    /// <summary>Creates one published, unpriced-facts Sku under the seeded StorageDevice category (never appears in a Blocked/Warning rule unless a Motherboard is also present).</summary>
    public Task<Sku> SeedSkuAsync(decimal listPrice = 1000m) =>
        SeedComponentSkuAsync(BuildComponentCategoryCodes.StorageDevice, listPrice: listPrice);

    /// <summary>Creates one published Sku under the given build-component category, optionally with specification/attribute facts — mirrors CompatibilityCheckServiceFixture.SeedComponentSkuAsync.</summary>
    public async Task<Sku> SeedComponentSkuAsync(
        string categoryCode,
        IReadOnlyDictionary<string, object?>? specValues = null,
        IReadOnlyDictionary<string, string[]>? attributes = null,
        decimal listPrice = 1000m,
        IReadOnlyDictionary<string, int>? storagePorts = null)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        var category = await context.Categories.SingleAsync(c => c.Code == categoryCode);

        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), UniqueCode("SKU"), product.Id, "測試SKU", listPrice, listPrice * 0.6m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        if (specValues is not null)
        {
            foreach (var (semanticKey, rawValue) in specValues)
            {
                if (rawValue is null)
                {
                    continue;
                }

                var definition = await context.SpecificationDefinitions
                    .SingleAsync(d => d.CategoryId == category.Id && d.SemanticKey == semanticKey);
                var stringValue = rawValue as string;
                decimal? decimalValue = rawValue switch
                {
                    decimal value => value,
                    int value => value,
                    _ => null,
                };

                context.SkuSpecificationValues.Add(new SkuSpecificationValue(
                    sku.Id, definition.Id, stringValue, decimalValue, null, null, null, now));
            }

            await context.SaveChangesAsync();
        }

        if (attributes is not null)
        {
            foreach (var (attributeKey, values) in attributes)
            {
                foreach (var value in values)
                {
                    context.SkuCompatibilityAttributes.Add(new SkuCompatibilityAttribute(sku.Id, attributeKey, value, now));
                }
            }

            await context.SaveChangesAsync();
        }

        if (storagePorts is not null)
        {
            foreach (var (interfaceCode, portCount) in storagePorts)
            {
                context.SkuStorageInterfacePorts.Add(
                    new SkuStorageInterfacePort(sku.Id, interfaceCode, portCount, now));
            }

            await context.SaveChangesAsync();
        }

        return sku;
    }

    /// <summary>
    /// PR #34 review round 2: 組長's V1 ruling requires all 8 build-component categories to be
    /// present, not just the 5 that have direct compatibility rules — seeds a full, cleanly
    /// "compatible" set of all 8 (100 units of stock each), mirroring
    /// EfBuildListServiceTests.SeedCompleteBuildComponentsAsync.
    /// </summary>
    public async Task<IReadOnlyList<Sku>> SeedCompleteBuildComponentsAsync()
    {
        var cpu = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CpuSocket] = "AM5",
                [CompatibilitySemanticKeys.CpuGeneration] = "Ryzen7000",
                [CompatibilitySemanticKeys.CpuPowerWatts] = 105m,
            });
        var motherboard = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.BoardSocket] = "AM5",
                [CompatibilitySemanticKeys.BoardChipset] = "X670E",
                [CompatibilitySemanticKeys.BoardMemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.BoardMemorySlotCount] = 4,
                [CompatibilitySemanticKeys.BoardMaxMemoryCapacityGb] = 128m,
                [CompatibilitySemanticKeys.BoardFormFactor] = "ATX",
            },
            storagePorts: new Dictionary<string, int> { ["NVME"] = 4 });
        var memory = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Memory,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.MemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.MemoryCapacityGbPerModule] = 16m,
            });
        var psu = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.PowerSupply,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.PsuWattage] = 650m });
        var pcCase = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Case,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CaseMaxGpuLengthMm] = 320m,
                [CompatibilitySemanticKeys.CaseMaxCoolerHeightMm] = 170m,
            },
            attributes: new Dictionary<string, string[]>
            {
                [CompatibilityAttributeKeys.CaseSupportedFormFactors] = ["ATX"],
            });
        var gpu = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.GraphicsCard,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.GpuLengthMm] = 280m,
                [CompatibilitySemanticKeys.GpuRecommendedPsuWatts] = 450m,
                [CompatibilitySemanticKeys.GpuPowerWatts] = 200m,
            });
        var storage = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.StorageInterface] = "NVME",
                [CompatibilitySemanticKeys.StoragePowerWatts] = 5m,
            });
        var cooler = await SeedComponentSkuAsync(
            BuildComponentCategoryCodes.Cooler,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CoolerHeightMm] = 150m,
                [CompatibilitySemanticKeys.CoolerPowerWatts] = 10m,
            },
            attributes: new Dictionary<string, string[]>
            {
                [CompatibilityAttributeKeys.CoolerSupportedSockets] = ["AM5"],
            });

        var components = new[] { cpu, motherboard, memory, psu, pcCase, gpu, storage, cooler };
        foreach (var sku in components)
        {
            await SeedInventoryAsync(sku.Id, 100);
        }

        return components;
    }

    public async Task SeedInventoryAsync(long skuId, int onHandQuantity)
    {
        await using var context = CreateContext();
        context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), skuId, onHandQuantity, reorderLevel: 0, DateTime.UtcNow));
        await context.SaveChangesAsync();
    }

    public static async Task<(int Status, string? Code, JsonElement Root)> ReadProblemAsync(HttpResponseMessage response)
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

    /// <summary>Mirrors CompatibilityCheckServiceFixture's SpecTemplates — all 8 build-component categories with their protected specification-definition templates, not just StorageDevice.</summary>
    private static readonly IReadOnlyDictionary<string, (string SemanticKey, SpecificationValueType ValueType)[]> SpecTemplates =
        new Dictionary<string, (string, SpecificationValueType)[]>
        {
            [BuildComponentCategoryCodes.Cpu] =
            [
                (CompatibilitySemanticKeys.CpuSocket, SpecificationValueType.String),
                (CompatibilitySemanticKeys.CpuGeneration, SpecificationValueType.String),
                (CompatibilitySemanticKeys.CpuPowerWatts, SpecificationValueType.Decimal),
            ],
            [BuildComponentCategoryCodes.Motherboard] =
            [
                (CompatibilitySemanticKeys.BoardSocket, SpecificationValueType.String),
                (CompatibilitySemanticKeys.BoardChipset, SpecificationValueType.String),
                (CompatibilitySemanticKeys.BoardMemoryGeneration, SpecificationValueType.String),
                (CompatibilitySemanticKeys.BoardMemorySlotCount, SpecificationValueType.Decimal),
                (CompatibilitySemanticKeys.BoardMaxMemoryCapacityGb, SpecificationValueType.Decimal),
                (CompatibilitySemanticKeys.BoardFormFactor, SpecificationValueType.String),
            ],
            [BuildComponentCategoryCodes.Memory] =
            [
                (CompatibilitySemanticKeys.MemoryGeneration, SpecificationValueType.String),
                (CompatibilitySemanticKeys.MemoryCapacityGbPerModule, SpecificationValueType.Decimal),
            ],
            [BuildComponentCategoryCodes.GraphicsCard] =
            [
                (CompatibilitySemanticKeys.GpuLengthMm, SpecificationValueType.Decimal),
                (CompatibilitySemanticKeys.GpuRecommendedPsuWatts, SpecificationValueType.Decimal),
                (CompatibilitySemanticKeys.GpuPowerWatts, SpecificationValueType.Decimal),
            ],
            [BuildComponentCategoryCodes.StorageDevice] =
            [
                (CompatibilitySemanticKeys.StorageInterface, SpecificationValueType.String),
                (CompatibilitySemanticKeys.StoragePowerWatts, SpecificationValueType.Decimal),
            ],
            [BuildComponentCategoryCodes.PowerSupply] =
            [
                (CompatibilitySemanticKeys.PsuWattage, SpecificationValueType.Decimal),
            ],
            [BuildComponentCategoryCodes.Case] =
            [
                (CompatibilitySemanticKeys.CaseMaxGpuLengthMm, SpecificationValueType.Decimal),
                (CompatibilitySemanticKeys.CaseMaxCoolerHeightMm, SpecificationValueType.Decimal),
            ],
            [BuildComponentCategoryCodes.Cooler] =
            [
                (CompatibilitySemanticKeys.CoolerHeightMm, SpecificationValueType.Decimal),
                (CompatibilitySemanticKeys.CoolerPowerWatts, SpecificationValueType.Decimal),
            ],
        };

    private static async Task SeedReferenceCategoryAsync()
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        foreach (var categoryCode in BuildComponentCategoryCodes.All)
        {
            var category = new Category(
                Guid.CreateVersion7(), categoryCode, $"slot-{categoryCode.ToLowerInvariant()}", categoryCode, null, now);
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            foreach (var (semanticKey, valueType) in SpecTemplates[categoryCode])
            {
                context.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(), category.Id, semanticKey, semanticKey, valueType,
                    null, isRequired: false, isProtected: true, sortOrder: 0, now));
            }

            await context.SaveChangesAsync();
        }
    }
}

[CollectionDefinition(nameof(BuildListsApiCollection))]
public sealed class BuildListsApiCollection : ICollectionFixture<BuildListsApiFixture>;
