using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DoSelect.Infrastructure.Persistence.Seeding;

public sealed class MinimalDevelopmentDataSeeder(
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IConfiguration configuration)
{
    public async Task<MinimalDevelopmentSeedResult> SeedAsync(
        CancellationToken cancellationToken = default)
    {
        var passwords = MinimalDevelopmentSeedDefinitions.GetPasswords(configuration);
        var counters = new SeedCounters();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await EnsureRolesAsync(counters);
        await EnsureUsersAndProfilesAsync(passwords, counters, cancellationToken);
        await EnsureCatalogAsync(counters, cancellationToken);
        await EnsureBuildCompatibilityAsync(counters, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new MinimalDevelopmentSeedResult(
            counters.RolesCreated,
            counters.UsersCreated,
            counters.ProfilesCreated,
            counters.CatalogRecordsCreated,
            counters.CompatibilityRecordsCreated);
    }

    private async Task EnsureRolesAsync(SeedCounters counters)
    {
        foreach (var roleName in MinimalDevelopmentSeedDefinitions.RoleNames)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            EnsureSucceeded(
                $"create role '{roleName}'",
                await roleManager.CreateAsync(new IdentityRole(roleName)));
            counters.RolesCreated++;
        }
    }

    private async Task EnsureUsersAndProfilesAsync(
        (string AdminPassword, string MemberPassword) passwords,
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        var admin = await EnsureUserAsync(
            MinimalDevelopmentSeedDefinitions.AdminEmail,
            passwords.AdminPassword,
            AccountType.Admin,
            MinimalDevelopmentSeedDefinitions.AdminPublicId,
            counters);

        if (!await userManager.IsInRoleAsync(admin, "SuperAdmin"))
        {
            EnsureSucceeded(
                "assign the SuperAdmin role",
                await userManager.AddToRoleAsync(admin, "SuperAdmin"));
        }

        if (!await dbContext.AdminProfiles.AnyAsync(
                profile => profile.UserId == admin.Id,
                cancellationToken))
        {
            dbContext.AdminProfiles.Add(new AdminProfile(
                admin.Id,
                MinimalDevelopmentSeedDefinitions.AdminPublicId,
                "DEV-ADMIN-001",
                "DoSelect 開發管理員",
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            counters.ProfilesCreated++;
        }

        var member = await EnsureUserAsync(
            MinimalDevelopmentSeedDefinitions.MemberEmail,
            passwords.MemberPassword,
            AccountType.Member,
            MinimalDevelopmentSeedDefinitions.MemberPublicId,
            counters);

        if (!await dbContext.MemberProfiles.AnyAsync(
                profile => profile.UserId == member.Id,
                cancellationToken))
        {
            dbContext.MemberProfiles.Add(new MemberProfile(
                member.Id,
                MinimalDevelopmentSeedDefinitions.MemberPublicId,
                "DoSelect 測試會員",
                null,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            counters.ProfilesCreated++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ApplicationUser> EnsureUserAsync(
        string email,
        string password,
        AccountType accountType,
        Guid publicId,
        SeedCounters counters)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            if (user.AccountType != accountType)
            {
                throw new InvalidOperationException(
                    $"Seed user '{email}' already exists with a different account type.");
            }

            return user;
        }

        user = accountType == AccountType.Admin
            ? ApplicationUser.CreateAdmin(
                publicId,
                email,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc)
            : ApplicationUser.CreateMember(
                publicId,
                email,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        user.ConfirmEmail(MinimalDevelopmentSeedDefinitions.CreatedAtUtc);

        EnsureSucceeded(
            $"create seed user '{email}'",
            await userManager.CreateAsync(user, password));
        counters.UsersCreated++;
        return user;
    }

    private async Task EnsureCatalogAsync(
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        var brand = await dbContext.Brands.SingleOrDefaultAsync(
            entity => entity.Code == "DOSELECT-DEV",
            cancellationToken);
        if (brand is null)
        {
            brand = new Brand(
                MinimalDevelopmentSeedDefinitions.BrandPublicId,
                "DOSELECT-DEV",
                "懂選開發品牌",
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.Brands.Add(brand);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CatalogRecordsCreated++;
        }

        var category = await dbContext.Categories.SingleOrDefaultAsync(
            entity => entity.Code == "DEV-GRAPHICS-CARDS",
            cancellationToken);
        if (category is null)
        {
            category = new Category(
                MinimalDevelopmentSeedDefinitions.CategoryPublicId,
                "DEV-GRAPHICS-CARDS",
                "dev-graphics-cards",
                "開發用顯示卡",
                null,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.Categories.Add(category);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CatalogRecordsCreated++;
        }

        var product = await dbContext.Products.SingleOrDefaultAsync(
            entity => entity.ProductCode == "DEV-GPU-001",
            cancellationToken);
        if (product is null)
        {
            product = new Product(
                MinimalDevelopmentSeedDefinitions.ProductPublicId,
                "DEV-GPU-001",
                brand.Id,
                category.Id,
                "懂選開發用顯示卡",
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            product.UpdateDetails(
                brand.Id,
                category.Id,
                "懂選開發用顯示卡",
                "僅供本機開發與整合測試使用的虛構商品。",
                36,
                true,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            product.ChangeStatus(
                ProductStatus.Published,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CatalogRecordsCreated++;
        }

        var sku = await dbContext.Skus.SingleOrDefaultAsync(
            entity => entity.SkuCode == "DEV-GPU-001-16G",
            cancellationToken);
        if (sku is null)
        {
            sku = new Sku(
                MinimalDevelopmentSeedDefinitions.SkuPublicId,
                "DEV-GPU-001-16G",
                product.Id,
                "懂選開發用顯示卡 16GB",
                19900m,
                15000m,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            sku.UpdateCommercialDetails(
                "懂選開發用顯示卡 16GB",
                19900m,
                15000m,
                true,
                true,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            sku.ChangeStatus(
                SkuStatus.Published,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.Skus.Add(sku);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CatalogRecordsCreated++;
        }

        var inventoryBalanceExists = await dbContext.InventoryBalances.AnyAsync(
            entity => entity.SkuId == sku.Id,
            cancellationToken);
        if (!inventoryBalanceExists)
        {
            dbContext.InventoryBalances.Add(new InventoryBalance(
                MinimalDevelopmentSeedDefinitions.InventoryBalancePublicId,
                sku.Id,
                onHandQuantity: 10,
                reorderLevel: 2,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CatalogRecordsCreated++;
        }
    }

    /// <summary>
    /// 組長 PR #34 review: SkuCompatibilityAttributes／SkuStorageInterfacePorts had no real
    /// production write path — only test fixtures ever wrote to them, so a fresh deployment's
    /// compatibility engine can never see anything but InsufficientData for GPU clearance, cooler
    /// socket, or storage-interface facts, and a real 8-category build can never reach the cart.
    /// Seeds one representative, mutually-compatible SKU per build-component category (same
    /// proven-compatible values as
    /// DoSelect.Infrastructure.Tests.Builds.EfBuildListServiceTests.SeedCompleteBuildComponentsAsync)
    /// through this real seeder — not a test-only DbContext write — so --seed-minimal leaves a
    /// genuine, addable-to-cart demo build behind.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string SemanticKey, SpecificationValueType ValueType)[]>
        BuildCompatibilitySpecTemplates = new Dictionary<string, (string, SpecificationValueType)[]>
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

    private async Task EnsureBuildCompatibilityAsync(
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        var categoriesByCode = new Dictionary<string, Category>();
        foreach (var categoryCode in BuildComponentCategoryCodes.All)
        {
            var category = await dbContext.Categories.SingleOrDefaultAsync(
                entity => entity.Code == categoryCode, cancellationToken);
            if (category is null)
            {
                category = new Category(
                    Guid.CreateVersion7(),
                    categoryCode,
                    $"slot-{categoryCode.ToLowerInvariant()}",
                    categoryCode,
                    null,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
                dbContext.Categories.Add(category);
                await dbContext.SaveChangesAsync(cancellationToken);
                counters.CompatibilityRecordsCreated++;
            }

            categoriesByCode[categoryCode] = category;

            foreach (var (semanticKey, valueType) in BuildCompatibilitySpecTemplates[categoryCode])
            {
                var definitionExists = await dbContext.SpecificationDefinitions.AnyAsync(
                    definition => definition.CategoryId == category.Id && definition.SemanticKey == semanticKey,
                    cancellationToken);
                if (definitionExists)
                {
                    continue;
                }

                dbContext.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(), category.Id, semanticKey, semanticKey, valueType, null,
                    isRequired: false, isProtected: true, sortOrder: 0,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
                counters.CompatibilityRecordsCreated++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (await dbContext.Skus.AnyAsync(
                entity => entity.SkuCode == "DEV-COMPAT-CPU-001", cancellationToken))
        {
            return;
        }

        var brand = await dbContext.Brands.SingleOrDefaultAsync(
            entity => entity.Code == "DEV-COMPAT-BRAND", cancellationToken);
        if (brand is null)
        {
            brand = new Brand(
                Guid.CreateVersion7(), "DEV-COMPAT-BRAND", "懂選組裝開發品牌",
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.Brands.Add(brand);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CompatibilityRecordsCreated++;
        }

        await CreateComponentSkuAsync(
            "DEV-COMPAT-CPU-001", "懂選開發用 CPU", BuildComponentCategoryCodes.Cpu, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CpuSocket] = "AM5",
                [CompatibilitySemanticKeys.CpuGeneration] = "Ryzen7000",
                [CompatibilitySemanticKeys.CpuPowerWatts] = 105m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-MB-001", "懂選開發用主機板", BuildComponentCategoryCodes.Motherboard, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.BoardSocket] = "AM5",
                [CompatibilitySemanticKeys.BoardChipset] = "X670E",
                [CompatibilitySemanticKeys.BoardMemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.BoardMemorySlotCount] = 4m,
                [CompatibilitySemanticKeys.BoardMaxMemoryCapacityGb] = 128m,
                [CompatibilitySemanticKeys.BoardFormFactor] = "ATX",
            },
            storagePorts: new Dictionary<string, int> { ["NVME"] = 4 },
            cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-MEM-001", "懂選開發用記憶體", BuildComponentCategoryCodes.Memory, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.MemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.MemoryCapacityGbPerModule] = 16m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-PSU-001", "懂選開發用電源供應器", BuildComponentCategoryCodes.PowerSupply, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?> { [CompatibilitySemanticKeys.PsuWattage] = 650m },
            cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-CASE-001", "懂選開發用機殼", BuildComponentCategoryCodes.Case, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CaseMaxGpuLengthMm] = 320m,
                [CompatibilitySemanticKeys.CaseMaxCoolerHeightMm] = 170m,
            },
            attributes: new Dictionary<string, string[]>
            {
                [CompatibilityAttributeKeys.CaseSupportedFormFactors] = ["ATX"],
            },
            cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-GPU-001", "懂選開發用顯示卡（組裝用）", BuildComponentCategoryCodes.GraphicsCard, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.GpuLengthMm] = 280m,
                [CompatibilitySemanticKeys.GpuRecommendedPsuWatts] = 450m,
                [CompatibilitySemanticKeys.GpuPowerWatts] = 200m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-STORAGE-001", "懂選開發用固態硬碟", BuildComponentCategoryCodes.StorageDevice, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.StorageInterface] = "NVME",
                [CompatibilitySemanticKeys.StoragePowerWatts] = 5m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-COOLER-001", "懂選開發用散熱器", BuildComponentCategoryCodes.Cooler, brand, categoriesByCode, counters,
            specValues: new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CoolerHeightMm] = 150m,
                [CompatibilitySemanticKeys.CoolerPowerWatts] = 10m,
            },
            attributes: new Dictionary<string, string[]>
            {
                [CompatibilityAttributeKeys.CoolerSupportedSockets] = ["AM5"],
            },
            cancellationToken: cancellationToken);
    }

    private async Task CreateComponentSkuAsync(
        string skuCode,
        string name,
        string categoryCode,
        Brand brand,
        IReadOnlyDictionary<string, Category> categoriesByCode,
        SeedCounters counters,
        IReadOnlyDictionary<string, object?>? specValues = null,
        IReadOnlyDictionary<string, string[]>? attributes = null,
        IReadOnlyDictionary<string, int>? storagePorts = null,
        CancellationToken cancellationToken = default)
    {
        var category = categoriesByCode[categoryCode];
        var productCode = skuCode.Replace("-001", "-PROD", StringComparison.Ordinal);
        var product = new Product(
            Guid.CreateVersion7(), productCode, brand.Id, category.Id, name,
            MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        product.ChangeStatus(ProductStatus.Published, MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        counters.CompatibilityRecordsCreated++;

        var sku = new Sku(
            Guid.CreateVersion7(), skuCode, product.Id, name, 5000m, 3000m,
            MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        sku.ChangeStatus(SkuStatus.Published, MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        dbContext.Skus.Add(sku);
        await dbContext.SaveChangesAsync(cancellationToken);
        counters.CompatibilityRecordsCreated++;

        if (specValues is not null)
        {
            foreach (var (semanticKey, rawValue) in specValues)
            {
                if (rawValue is null)
                {
                    continue;
                }

                var definition = await dbContext.SpecificationDefinitions.SingleAsync(
                    candidate => candidate.CategoryId == category.Id && candidate.SemanticKey == semanticKey,
                    cancellationToken);
                var stringValue = rawValue as string;
                decimal? decimalValue = rawValue is decimal value ? value : null;

                dbContext.SkuSpecificationValues.Add(new SkuSpecificationValue(
                    sku.Id, definition.Id, stringValue, decimalValue, null, null, null,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (attributes is not null)
        {
            foreach (var (attributeKey, values) in attributes)
            {
                foreach (var value in values)
                {
                    dbContext.SkuCompatibilityAttributes.Add(new SkuCompatibilityAttribute(
                        sku.Id, attributeKey, value, MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (storagePorts is not null)
        {
            foreach (var (interfaceCode, portCount) in storagePorts)
            {
                dbContext.SkuStorageInterfacePorts.Add(new SkuStorageInterfacePort(
                    sku.Id, interfaceCode, portCount, MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity: 50, reorderLevel: 5,
            MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        counters.CompatibilityRecordsCreated++;
    }

    private static void EnsureSucceeded(string action, IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errorCodes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException(
            $"Failed to {action}. Identity error codes: {errorCodes}.");
    }

    private sealed class SeedCounters
    {
        public int RolesCreated { get; set; }

        public int UsersCreated { get; set; }

        public int ProfilesCreated { get; set; }

        public int CatalogRecordsCreated { get; set; }

        public int CompatibilityRecordsCreated { get; set; }
    }
}

public sealed record MinimalDevelopmentSeedResult(
    int RolesCreated,
    int UsersCreated,
    int ProfilesCreated,
    int CatalogRecordsCreated,
    int CompatibilityRecordsCreated);
