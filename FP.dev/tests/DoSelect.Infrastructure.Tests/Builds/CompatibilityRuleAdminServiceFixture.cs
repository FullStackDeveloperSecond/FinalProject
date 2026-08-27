using DoSelect.Application.Auditing;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Builds;

/// <summary>
/// Own database, separate from <see cref="CompatibilityCheckServiceFixture"/> — admin actions
/// here (SetActivationAsync, UpdateWarningSettingAsync) mutate CompatibilityRuleSettings
/// persistently for the whole collection-shared database, which would otherwise silently break
/// unrelated read-only tests sharing that fixture (a rule disabled by one test stays disabled
/// for every other test in the same collection).
/// </summary>
public sealed class CompatibilityRuleAdminServiceFixture : IAsyncLifetime
{
    // 組長 PR #34: was hardcoded to the local ".\SQL2025" instance — CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION (SQL auth, localhost:1433).
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectCompatibilityRuleAdminServiceTests");

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        foreach (var categoryCode in BuildComponentCategoryCodes.All)
        {
            var category = new Category(
                Guid.CreateVersion7(), categoryCode, $"slot-{categoryCode.ToLowerInvariant()}", categoryCode, null, now);
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            foreach (var semanticKey in SemanticKeysFor(categoryCode))
            {
                context.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(), category.Id, semanticKey, semanticKey,
                    SpecificationValueType.String, null, isRequired: false, isProtected: true, sortOrder: 0, now));
            }

            await context.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }

    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public static async Task<string> SeedMemberUserIdAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(member);
        await context.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>DEC-BATCH-026 (DEC-P309): EfCompatibilityRuleAdminService's audit actor resolution now requires a real Admin-type account holding one of the roles the CompatibilityRule.* policies allow — mirrors InvoiceAllowanceWriterSqlServerTests' own admin+role seeding.</summary>
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

    /// <summary>Creates one published Sku under the given build-component category with the given semantic-key facts.</summary>
    public static async Task<Sku> SeedComponentSkuAsync(
        DoSelectDbContext context,
        string categoryCode,
        IReadOnlyDictionary<string, object?>? specValues = null)
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

        return sku;
    }

    private static IReadOnlyList<string> SemanticKeysFor(string categoryCode) => categoryCode switch
    {
        _ when categoryCode == BuildComponentCategoryCodes.Cpu =>
        [
            CompatibilitySemanticKeys.CpuSocket, CompatibilitySemanticKeys.CpuGeneration, CompatibilitySemanticKeys.CpuPowerWatts,
        ],
        _ when categoryCode == BuildComponentCategoryCodes.Motherboard =>
        [
            CompatibilitySemanticKeys.BoardSocket, CompatibilitySemanticKeys.BoardChipset,
            CompatibilitySemanticKeys.BoardMemoryGeneration, CompatibilitySemanticKeys.BoardMemorySlotCount,
            CompatibilitySemanticKeys.BoardMaxMemoryCapacityGb, CompatibilitySemanticKeys.BoardFormFactor,
        ],
        _ when categoryCode == BuildComponentCategoryCodes.Memory =>
        [
            CompatibilitySemanticKeys.MemoryGeneration, CompatibilitySemanticKeys.MemoryCapacityGbPerModule,
        ],
        _ when categoryCode == BuildComponentCategoryCodes.GraphicsCard =>
        [
            CompatibilitySemanticKeys.GpuLengthMm, CompatibilitySemanticKeys.GpuRecommendedPsuWatts, CompatibilitySemanticKeys.GpuPowerWatts,
        ],
        _ when categoryCode == BuildComponentCategoryCodes.StorageDevice =>
        [
            CompatibilitySemanticKeys.StorageInterface, CompatibilitySemanticKeys.StoragePowerWatts,
        ],
        _ when categoryCode == BuildComponentCategoryCodes.PowerSupply => [CompatibilitySemanticKeys.PsuWattage],
        _ when categoryCode == BuildComponentCategoryCodes.Case =>
        [
            CompatibilitySemanticKeys.CaseMaxGpuLengthMm, CompatibilitySemanticKeys.CaseMaxCoolerHeightMm,
        ],
        _ when categoryCode == BuildComponentCategoryCodes.Cooler =>
        [
            CompatibilitySemanticKeys.CoolerHeightMm, CompatibilitySemanticKeys.CoolerPowerWatts,
        ],
        _ => [],
    };
}

[CollectionDefinition(nameof(CompatibilityRuleAdminServiceCollection))]
public sealed class CompatibilityRuleAdminServiceCollection : ICollectionFixture<CompatibilityRuleAdminServiceFixture>;
