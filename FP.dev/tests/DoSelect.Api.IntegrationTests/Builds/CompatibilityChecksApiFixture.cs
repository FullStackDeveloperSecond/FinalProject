using System.Net.Http.Json;
using System.Text.Json;
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
/// <c>CompatibilityChecksController</c>, mirroring <c>Shopping.CartApiFixture</c>'s pattern.
/// The endpoint is public, so unlike Cart there is no member/guest identity plumbing here.
/// </summary>
public sealed class CompatibilityChecksApiFixture : IAsyncLifetime
{
    // 組長 PR #34: was hardcoded to the local ".\SQL2025" instance — CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION (SQL auth, localhost:1433).
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectCompatibilityChecksApiTests");

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
        "DoSelectCompatibilityChecksApiTests",
        Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();
        await SeedReferenceCategoriesAsync();

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

    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    /// <summary>Creates one published Sku under the given build-component category with the given semantic-key facts.
    /// A string value is written as a single-select <see cref="SkuSpecificationValue.OptionId"/> (get-or-create the
    /// option under that category's own definition row), a numeric value as <see cref="SkuSpecificationValue.DecimalValue"/> —
    /// mirrors the canonical seeding pattern in <c>MinimalDevelopmentDataSeeder.CreateComponentSkuAsync</c>, since
    /// <see cref="DoSelect.Infrastructure.Catalog.EfCompatibilityCatalogReader"/> only reads those two, and only when
    /// <see cref="SkuSpecificationValue.SpecificationSourceId"/> is populated.</summary>
    public async Task<Sku> SeedComponentSkuAsync(
        string categoryCode,
        IReadOnlyDictionary<string, object?> specValues,
        IReadOnlyDictionary<string, string[]>? multiValues = null)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        var category = await context.Categories.SingleAsync(c => c.Code == categoryCode);

        var reviewer = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(), $"compat-checks-reviewer-{Guid.NewGuid():N}@doselect.test", now);
        context.Users.Add(reviewer);
        await context.SaveChangesAsync();
        var source = new SpecificationSource(
            Guid.CreateVersion7(), SpecificationSourceType.SystemEstimate, "DoSelect Test Seed",
            "https://doselect.dev/seed/compatibility-checks-api-tests", null, now, now, reviewer.Id, "v1", now);
        context.SpecificationSources.Add(source);
        await context.SaveChangesAsync();

        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        product.ChangeStatus(ProductStatus.Published, now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), UniqueCode("SKU"), product.Id, "測試SKU", 1000m, 600m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        foreach (var (semanticKey, rawValue) in specValues)
        {
            if (rawValue is null)
            {
                continue;
            }

            var definition = await context.SpecificationDefinitions
                .SingleAsync(d => d.CategoryId == category.Id && d.SemanticKey == semanticKey);
            if (rawValue is string code)
            {
                var option = await context.SpecificationOptions
                    .SingleOrDefaultAsync(o => o.SpecificationDefinitionId == definition.Id && o.Code == code);
                if (option is null)
                {
                    option = new SpecificationOption(Guid.CreateVersion7(), definition.Id, code, code, 0, now);
                    context.SpecificationOptions.Add(option);
                    await context.SaveChangesAsync();
                }

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

        if (multiValues is not null)
        {
            foreach (var (semanticKey, optionCodes) in multiValues)
            {
                var definition = await context.SpecificationDefinitions
                    .SingleAsync(d => d.CategoryId == category.Id && d.SemanticKey == semanticKey);

                foreach (var optionCode in optionCodes)
                {
                    var option = await context.SpecificationOptions
                        .SingleOrDefaultAsync(o => o.SpecificationDefinitionId == definition.Id && o.Code == optionCode);
                    if (option is null)
                    {
                        option = new SpecificationOption(Guid.CreateVersion7(), definition.Id, optionCode, optionCode, 0, now);
                        context.SpecificationOptions.Add(option);
                        await context.SaveChangesAsync();
                    }

                    context.SkuSpecificationOptionSelections.Add(
                        new SkuSpecificationOptionSelection(sku.Id, option.Id, now, source.Id));
                }
            }

            await context.SaveChangesAsync();
        }

        await context.SaveChangesAsync();
        return sku;
    }

    /// <summary>
    /// Seeds a full, otherwise-compatible 8-category build — the canonical
    /// <see cref="DoSelect.Domain.Builds.CompatibilityEvaluator"/> requires every singleton role
    /// (and at least one Memory/Storage) present before it evaluates any pairwise rule, so a bare
    /// CPU+Motherboard pair alone only ever reaches <c>insufficientData</c>.
    /// </summary>
    public async Task<(Sku Cpu, Sku Motherboard, Sku Memory, Sku Psu, Sku Case, Sku Gpu, Sku Storage, Sku Cooler)>
        SeedCompleteBuildComponentsAsync()
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

        return (cpu, motherboard, memory, psu, pcCase, gpu, storage, cooler);
    }

    internal static List<(Guid SkuPublicId, int Quantity)> ToBuildItems(
        (Sku Cpu, Sku Motherboard, Sku Memory, Sku Psu, Sku Case, Sku Gpu, Sku Storage, Sku Cooler) components) =>
    [
        (components.Cpu.PublicId, 1),
        (components.Motherboard.PublicId, 1),
        (components.Memory.PublicId, 1),
        (components.Psu.PublicId, 1),
        (components.Case.PublicId, 1),
        (components.Gpu.PublicId, 1),
        (components.Storage.PublicId, 1),
        (components.Cooler.PublicId, 1),
    ];

    /// <summary>
    /// This endpoint is public, but the global antiforgery filter still requires a token on
    /// every unsafe (POST/PATCH/DELETE) request regardless of authentication — mirrors
    /// <c>Shopping.CartApiFixture.GetMemberAntiforgeryTokenAsync</c>.
    /// </summary>
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "member");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    public static async Task<HttpResponseMessage> PostWithAntiforgeryAsync(
        HttpClient client, string requestUri, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
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

    /// <summary>Seeds all 8 build-component categories with the canonical specification-definition
    /// templates — mirrors <c>MinimalDevelopmentDataSeeder.BuildCompatibilitySpecTemplates</c>. CpuSocket is
    /// its own <see cref="SpecificationDefinition"/> row per category — the canonical model
    /// (<see cref="CompatibilityEvaluator"/>) reuses the same semantic key for both the CPU's own socket and
    /// the Motherboard's/CpuCooler's socket, comparing each category's own option codes.</summary>
    private static async Task SeedReferenceCategoriesAsync()
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        var templates = new Dictionary<string, SpecDefinitionTemplate[]>
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

        foreach (var (categoryCode, specTemplates) in templates)
        {
            var category = new Category(
                Guid.CreateVersion7(), categoryCode, $"slot-{categoryCode.ToLowerInvariant()}", categoryCode, null, now);
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            foreach (var template in specTemplates)
            {
                context.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(), category.Id, template.SemanticKey, template.SemanticKey,
                    template.ValueType, null, isRequired: false, isProtected: true, sortOrder: 0, now,
                    allowsMultiple: template.AllowsMultiple));
            }

            await context.SaveChangesAsync();
        }
    }
}

[CollectionDefinition(nameof(CompatibilityChecksApiCollection))]
public sealed class CompatibilityChecksApiCollection : ICollectionFixture<CompatibilityChecksApiFixture>;
