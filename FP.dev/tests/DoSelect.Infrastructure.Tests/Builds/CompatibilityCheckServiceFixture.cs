using DoSelect.Application.Auditing;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DoSelect.Infrastructure.Tests.Builds;

/// <summary>
/// Seeds the 8 build-component categories and their protected specification-definition
/// templates once per test class run (reference data, mirrored on real seed data an admin
/// would eventually manage), then lets individual tests create SKUs under them. Mirrors
/// <c>Shopping/CartServiceFixture.cs</c>'s SQL Server-backed pattern.
/// </summary>
public sealed class CompatibilityCheckServiceFixture : IAsyncLifetime
{
    // 組長 PR #34: was hardcoded to the local ".\SQL2025" instance — CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION (SQL auth, localhost:1433).
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectCompatibilityCheckServiceTests");

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

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        await SeedCategoriesAndSpecTemplatesAsync(context);
    }

    /// <summary>Reusable by any test that needs its own isolated database (e.g. one seeding global CompatibilityRuleSettings rows that must not leak into this fixture's shared collection) but still wants the same 8-category reference data.</summary>
    internal static async Task SeedCategoriesAndSpecTemplatesAsync(DoSelectDbContext context)
    {
        var now = DateTime.UtcNow;
        foreach (var categoryCode in CompatibilityCatalogContract.Categories.All)
        {
            var category = new Category(
                Guid.CreateVersion7(),
                categoryCode,
                $"slot-{categoryCode.ToLowerInvariant()}",
                categoryCode,
                null,
                now);
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            foreach (var template in SpecTemplates[categoryCode])
            {
                context.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(),
                    category.Id,
                    template.SemanticKey,
                    template.SemanticKey,
                    template.ValueType,
                    null,
                    isRequired: false,
                    isProtected: true,
                    sortOrder: 0,
                    now,
                    allowsMultiple: template.AllowsMultiple));
            }

            await context.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new DoSelectDbContext(builder.Options);
    }

    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    /// <summary>PR #34 round-6 review, A1 裁定: EfSkuCompatibilityAttributeAdminService's audit actor resolution now requires a real Admin-type account holding one of the roles CompatibilityRule.ManageWarnings allows — mirrors CompatibilityRuleAdminServiceFixture's own admin+role seeding.</summary>
    public static async Task<string> SeedAdminUserIdAsync(DoSelectDbContext context, string roleName)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        var role = new IdentityRole(roleName);
        context.AddRange(admin, role);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
        await context.SaveChangesAsync();
        return admin.Id;
    }

    public static readonly AuditRequestContext TestAuditContext =
        new("test-correlation", "0123456789abcdef0123456789abcdef", null);

    // BuildLists.OwnerUserId has a foreign key to AspNetUsers, so build-list tests need a real
    // seeded ApplicationUser id rather than an arbitrary string (mirrors CartServiceFixture).
    public static async Task<string> SeedMemberUserIdAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(member);
        await context.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>
    /// Creates one published Sku under the given build-component category and hard-rule facts
    /// through the canonical multi-value model — mirrors
    /// <c>MinimalDevelopmentDataSeeder.CreateComponentSkuAsync</c>: a decimal value writes
    /// straight to <see cref="SkuSpecificationValue.DecimalValue"/>; a string value
    /// get-or-creates a <see cref="SpecificationOption"/> scoped to this category's own
    /// definition, then links via <see cref="SkuSpecificationValue.OptionId"/> (single-select,
    /// <paramref name="specValues"/>) or <see cref="SkuSpecificationOptionSelection"/>
    /// (multi-select, <paramref name="multiValues"/>).
    /// </summary>
    public static async Task<Sku> SeedComponentSkuAsync(
        DoSelectDbContext context,
        string categoryCode,
        IReadOnlyDictionary<string, object?>? specValues = null,
        IReadOnlyDictionary<string, string[]>? multiValues = null)
    {
        var now = DateTime.UtcNow;
        var category = await context.Categories.SingleAsync(c => c.Code == categoryCode);
        var source = await GetOrCreateSpecificationSourceAsync(context, now);

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

    private static async Task<SpecificationSource> GetOrCreateSpecificationSourceAsync(DoSelectDbContext context, DateTime now)
    {
        var source = await context.SpecificationSources
            .SingleOrDefaultAsync(s => s.ProviderName == "DoSelect Test Seed");
        if (source is not null)
        {
            return source;
        }

        var reviewer = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(), $"compat-check-reviewer-{Guid.NewGuid():N}@doselect.test", now);
        context.Users.Add(reviewer);
        await context.SaveChangesAsync();
        source = new SpecificationSource(
            Guid.CreateVersion7(), SpecificationSourceType.SystemEstimate, "DoSelect Test Seed",
            "https://doselect.dev/seed/compatibility-check-service-tests", null, now, now, reviewer.Id, "v1", now);
        context.SpecificationSources.Add(source);
        await context.SaveChangesAsync();
        return source;
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
}
