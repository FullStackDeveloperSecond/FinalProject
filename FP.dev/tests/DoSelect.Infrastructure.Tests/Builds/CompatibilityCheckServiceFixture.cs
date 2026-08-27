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
/// <c>Shopping/CartServiceFixture.cs</c>'s SQL Server-backed pattern; the database is created
/// via <c>EnsureCreatedAsync</c> from the current EF model, so the not-yet-migrated
/// <see cref="SkuCompatibilityAttribute"/> table is still available here even though no
/// migration has been generated for it yet.
/// </summary>
public sealed class CompatibilityCheckServiceFixture : IAsyncLifetime
{
    // 組長 PR #34: was hardcoded to the local ".\SQL2025" instance — CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION (SQL auth, localhost:1433).
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectCompatibilityCheckServiceTests");

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
        foreach (var categoryCode in BuildComponentCategoryCodes.All)
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

            foreach (var (semanticKey, valueType) in SpecTemplates[categoryCode])
            {
                context.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(),
                    category.Id,
                    semanticKey,
                    semanticKey,
                    valueType,
                    null,
                    isRequired: false,
                    isProtected: true,
                    sortOrder: 0,
                    now));
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
    /// Creates one published Sku under the given build-component category, with the given
    /// semantic-key facts (string or decimal, matching <see cref="SpecTemplates"/>) and any
    /// multi-value compatibility attributes.
    /// </summary>
    public static async Task<Sku> SeedComponentSkuAsync(
        DoSelectDbContext context,
        string categoryCode,
        IReadOnlyDictionary<string, object?>? specValues = null,
        IReadOnlyDictionary<string, string[]>? attributes = null,
        IReadOnlyDictionary<string, int>? storagePorts = null)
    {
        var now = DateTime.UtcNow;
        var category = await context.Categories.SingleAsync(c => c.Code == categoryCode);

        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
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
                    context.SkuCompatibilityAttributes.Add(
                        new SkuCompatibilityAttribute(sku.Id, attributeKey, value, now));
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
}
