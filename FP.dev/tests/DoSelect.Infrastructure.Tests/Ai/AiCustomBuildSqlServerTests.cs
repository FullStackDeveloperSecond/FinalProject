using DoSelect.Application.Ai;
using DoSelect.Application.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class AiCustomBuildSqlServerTests
{
    [Fact]
    public async Task ExistingCpu_IsIncludedButExcludedFromPurchaseBudget_AndBuildIsCompleteAndCompatible()
    {
        var connectionString = SqlServerTestConnection.Build(
            $"DoSelectAiCustomBuild_{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Seed:AdminPassword"] = "E2e_Admin_123!",
                ["Seed:MemberPassword"] = "E2e_Member_123!",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDoSelectPersistence(configuration);
        services.AddDoSelectCatalogServices();
        services.AddDoSelectBuildsServices();
        services.AddScoped<IAiProductSearchCatalog, EfAiProductSearchCatalog>();

        await using var provider = services.BuildServiceProvider();
        try
        {
            await using (var migrationScope = provider.CreateAsyncScope())
            {
                var context = migrationScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
                await context.Database.MigrateAsync();
                await migrationScope.ServiceProvider.GetRequiredService<MinimalDevelopmentDataSeeder>()
                    .SeedAsync();
            }

            Guid cpuPublicId;
            await using (var readScope = provider.CreateAsyncScope())
            {
                var context = readScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
                cpuPublicId = await (
                        from sku in context.Skus.AsNoTracking()
                        join product in context.Products.AsNoTracking() on sku.ProductId equals product.Id
                        join category in context.Categories.AsNoTracking() on product.CategoryId equals category.Id
                        where category.Code == CompatibilityCatalogContract.Categories.Cpu &&
                              sku.SkuCode == "DEV-COMPAT-CPU-001"
                        select sku.PublicId)
                    .SingleAsync();
            }

            await using var searchScope = provider.CreateAsyncScope();
            var catalog = searchScope.ServiceProvider.GetRequiredService<IAiProductSearchCatalog>();
            var metadata = await catalog.ReadMetadataAsync(CancellationToken.None);
            Assert.NotEmpty(metadata.SemanticKeys);
            Assert.Contains(CompatibilityCatalogContract.SemanticKeys.CpuSocket, metadata.SemanticKeys);
            Assert.All(
                metadata.SemanticKeys,
                semanticKey => Assert.Matches("^[A-Z0-9][A-Z0-9._-]{0,63}$", semanticKey));
            var result = await catalog.FindCandidatesAsync(
                new AiProductSearchIntent(
                    AiProductSearchIntentType.CustomBuild,
                    ["Gaming"],
                    new AiBudgetRange(null, 35_300m),
                    Keyword: null,
                    CategoryCode: null,
                    PreferredBrandCodes: [],
                    ExcludedBrandCodes: [],
                    RequiredSpecs: [],
                    Preferences: [],
                    ProposedExistingParts: [],
                    Clarifications: []),
                [new AiProductSearchExistingPart(
                    cpuPublicId,
                    "catalogSku",
                    CategoryCode: null,
                    DisplayName: null,
                    Specifications: [],
                    Quantity: 1,
                    ConfirmedByUser: true)],
                SupportedLocale.ZhTw,
                CancellationToken.None);

            Assert.True(result.IsValid);
            Assert.NotNull(result.CustomBuild);
            Assert.Equal(8, result.CustomBuild.Components.Count);
            Assert.Equal(
                CompatibilityCatalogContract.Categories.All.OrderBy(category => category),
                result.CustomBuild.Components.Select(component => component.CategoryCode).OrderBy(category => category));
            Assert.Equal(35_000m, result.CustomBuild.PurchaseSubtotal);
            Assert.Equal(300m, result.CustomBuild.AssemblyFee);
            Assert.Equal(35_300m, result.CustomBuild.PurchaseTotal);
            Assert.Equal(AiCompatibilityStatus.Compatible, result.CustomBuild.CompatibilityStatus);
            Assert.Equal(
                result.CustomBuild.PurchaseSubtotal,
                result.CustomBuild.Components
                    .Where(component => !component.IsExistingPart)
                    .Sum(component => component.Product!.Price.Sale ?? component.Product.Price.List));
            var existingCpu = Assert.Single(result.CustomBuild.Components, component =>
                component.CategoryCode == CompatibilityCatalogContract.Categories.Cpu);
            Assert.True(existingCpu.IsExistingPart);
            Assert.Equal(cpuPublicId, existingCpu.SkuPublicId);
        }
        finally
        {
            await using var cleanupScope = provider.CreateAsyncScope();
            var context = cleanupScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
