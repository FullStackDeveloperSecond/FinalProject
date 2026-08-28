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

    /// <summary>Creates one published, unpriced-facts Sku under the seeded Storage category (never appears in a Blocked/Warning rule unless a Motherboard is also present).</summary>
    public Task<Sku> SeedSkuAsync(decimal listPrice = 1000m) =>
        SeedComponentSkuAsync(CompatibilityCatalogContract.Categories.Storage, listPrice: listPrice);

    /// <summary>Creates one published Sku under the given build-component category and hard-rule facts through
    /// the canonical multi-value model — mirrors <c>MinimalDevelopmentDataSeeder.CreateComponentSkuAsync</c>: a
    /// decimal value writes straight to <see cref="SkuSpecificationValue.DecimalValue"/>; a string value
    /// get-or-creates a <see cref="SpecificationOption"/> scoped to this category's own definition, then links via
    /// <see cref="SkuSpecificationValue.OptionId"/> (single-select, <paramref name="specValues"/>) or
    /// <see cref="SkuSpecificationOptionSelection"/> (multi-select, <paramref name="multiValues"/>).</summary>
    public async Task<Sku> SeedComponentSkuAsync(
        string categoryCode,
        IReadOnlyDictionary<string, object?>? specValues = null,
        decimal listPrice = 1000m,
        IReadOnlyDictionary<string, string[]>? multiValues = null)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        var category = await context.Categories.SingleAsync(c => c.Code == categoryCode);

        var reviewer = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(), $"build-lists-reviewer-{Guid.NewGuid():N}@doselect.test", now);
        context.Users.Add(reviewer);
        await context.SaveChangesAsync();
        var source = new SpecificationSource(
            Guid.CreateVersion7(), SpecificationSourceType.SystemEstimate, "DoSelect Test Seed",
            "https://doselect.dev/seed/build-lists-api-tests", null, now, now, reviewer.Id, "v1", now);
        context.SpecificationSources.Add(source);
        await context.SaveChangesAsync();

        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        product.ChangeStatus(ProductStatus.Published, now);
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

                if (rawValue is string optionCode)
                {
                    var option = await GetOrCreateOptionAsync(context, definition.Id, optionCode, now);
                    context.SkuSpecificationValues.Add(new SkuSpecificationValue(
                        sku.Id, definition.Id, null, null, null, option.Id, source.Id, now));
                    continue;
                }

                decimal? decimalValue = rawValue switch
                {
                    decimal value => value,
                    int value => value,
                    _ => null,
                };
                context.SkuSpecificationValues.Add(new SkuSpecificationValue(
                    sku.Id, definition.Id, null, decimalValue, null, null, source.Id, now));
            }

            await context.SaveChangesAsync();
        }

        if (multiValues is not null)
        {
            foreach (var (semanticKey, optionCodes) in multiValues)
            {
                var definition = await context.SpecificationDefinitions
                    .SingleAsync(d => d.CategoryId == category.Id && d.SemanticKey == semanticKey);

                foreach (var optionCode in optionCodes)
                {
                    var option = await GetOrCreateOptionAsync(context, definition.Id, optionCode, now);
                    context.SkuSpecificationOptionSelections.Add(
                        new SkuSpecificationOptionSelection(sku.Id, option.Id, now, source.Id));
                }
            }

            await context.SaveChangesAsync();
        }

        return sku;
    }

    private static async Task<SpecificationOption> GetOrCreateOptionAsync(
        DoSelectDbContext context, long specificationDefinitionId, string code, DateTime now)
    {
        var option = await context.SpecificationOptions.SingleOrDefaultAsync(
            o => o.SpecificationDefinitionId == specificationDefinitionId && o.Code == code);
        if (option is not null)
        {
            return option;
        }

        option = new SpecificationOption(Guid.CreateVersion7(), specificationDefinitionId, code, code, 0, now);
        context.SpecificationOptions.Add(option);
        await context.SaveChangesAsync();
        return option;
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
            CompatibilityCatalogContract.Categories.Cpu,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [CompatibilityCatalogContract.SemanticKeys.CpuGeneration] = "RYZEN_7000",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 105m,
            });
        var motherboard = await SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 128m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var memory = await SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Memory,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemoryModuleCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryKitCapacityGb] = 16m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        var psu = await SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Psu,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.PsuRatedWatts] = 650m,
                [CompatibilityCatalogContract.SemanticKeys.PsuFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.PsuPcie62PinCount] = 2m,
                [CompatibilityCatalogContract.SemanticKeys.Psu12VhpwrCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PsuCpuEps8PinCount] = 2m,
            });
        var pcCase = await SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Case,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CaseGpuMaxLengthMm] = 320m,
                [CompatibilityCatalogContract.SemanticKeys.CaseCoolerMaxHeightMm] = 170m,
            },
            multiValues: new Dictionary<string, string[]>
            {
                [CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor] = ["ATX"],
                [CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor] = ["ATX"],
            });
        var gpu = await SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Gpu,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.GpuLengthMm] = 280m,
                [CompatibilityCatalogContract.SemanticKeys.GpuRecommendedPsuWatts] = 450m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 200m,
                [CompatibilityCatalogContract.SemanticKeys.GpuPcie62PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.Gpu12VhpwrRequiredCount] = 0m,
            });
        var storage = await SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.Storage,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.StorageInterface] = "M2_NVME",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        var cooler = await SeedComponentSkuAsync(
            CompatibilityCatalogContract.Categories.CpuCooler,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm] = 150m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 10m,
            },
            multiValues: new Dictionary<string, string[]>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = ["AM5"],
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

    private sealed record SpecDefinitionTemplate(string SemanticKey, SpecificationValueType ValueType, bool AllowsMultiple);

    /// <summary>Mirrors MinimalDevelopmentDataSeeder's BuildCompatibilitySpecTemplates — all 8 build-component categories with their canonical specification-definition templates.</summary>
    private static readonly IReadOnlyDictionary<string, SpecDefinitionTemplate[]> SpecTemplates =
        new Dictionary<string, SpecDefinitionTemplate[]>
        {
            [CompatibilityCatalogContract.Categories.Cpu] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.CpuSocket, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.CpuGeneration, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts, SpecificationValueType.Decimal, false),
            ],
            [CompatibilityCatalogContract.Categories.Motherboard] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.CpuSocket, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.MotherboardChipset, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.MemoryType, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.MemorySlotCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.M2SlotCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.SataPortCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts, SpecificationValueType.Decimal, false),
            ],
            [CompatibilityCatalogContract.Categories.Memory] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.MemoryType, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.MemoryModuleCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.MemoryKitCapacityGb, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts, SpecificationValueType.Decimal, false),
            ],
            [CompatibilityCatalogContract.Categories.Gpu] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.GpuLengthMm, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.GpuRecommendedPsuWatts, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.GpuPcie62PinRequiredCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.Gpu12VhpwrRequiredCount, SpecificationValueType.Decimal, false),
            ],
            [CompatibilityCatalogContract.Categories.Storage] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.StorageInterface, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts, SpecificationValueType.Decimal, false),
            ],
            [CompatibilityCatalogContract.Categories.Psu] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.PsuRatedWatts, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.PsuFormFactor, SpecificationValueType.Option, false),
                new(CompatibilityCatalogContract.SemanticKeys.PsuPcie62PinCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.Psu12VhpwrCount, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.PsuCpuEps8PinCount, SpecificationValueType.Decimal, false),
            ],
            [CompatibilityCatalogContract.Categories.Case] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor, SpecificationValueType.Option, true),
                new(CompatibilityCatalogContract.SemanticKeys.CaseGpuMaxLengthMm, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.CaseCoolerMaxHeightMm, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor, SpecificationValueType.Option, true),
            ],
            [CompatibilityCatalogContract.Categories.CpuCooler] =
            [
                new(CompatibilityCatalogContract.SemanticKeys.CpuSocket, SpecificationValueType.Option, true),
                new(CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm, SpecificationValueType.Decimal, false),
                new(CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts, SpecificationValueType.Decimal, false),
            ],
        };

    private static async Task SeedReferenceCategoryAsync()
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        foreach (var categoryCode in CompatibilityCatalogContract.Categories.All)
        {
            var category = new Category(
                Guid.CreateVersion7(), categoryCode, $"slot-{categoryCode.ToLowerInvariant()}", categoryCode, null, now);
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            foreach (var template in SpecTemplates[categoryCode])
            {
                context.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(), category.Id, template.SemanticKey, template.SemanticKey, template.ValueType,
                    null, isRequired: false, isProtected: true, sortOrder: 0, now, allowsMultiple: template.AllowsMultiple));
            }

            await context.SaveChangesAsync();
        }
    }
}

[CollectionDefinition(nameof(BuildListsApiCollection))]
public sealed class BuildListsApiCollection : ICollectionFixture<BuildListsApiFixture>;
