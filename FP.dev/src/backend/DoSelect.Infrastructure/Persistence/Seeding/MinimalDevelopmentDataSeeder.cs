using System.Security.Cryptography;
using System.Text;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shopping;
using DoSelect.Domain.Shipping;
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
        await EnsureShippingMethodsAsync(counters, cancellationToken);
        await EnsureShippingProvidersAsync(counters, cancellationToken);
        await EnsureConvenienceStoresAsync(counters, cancellationToken);
        await EnsureCoreTransactionJourneyAsync(cancellationToken);
        await EnsureRefundJourneyOrderAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new MinimalDevelopmentSeedResult(
            counters.RolesCreated,
            counters.UsersCreated,
            counters.ProfilesCreated,
            counters.CatalogRecordsCreated,
            counters.CompatibilityRecordsCreated,
            counters.ShippingRecordsCreated);
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

        // The primary --seed-minimal SKU is used by the real guest Checkout E2E journey. Checkout
        // deliberately rejects items whose package facts are incomplete, so a catalog-only seed
        // could be added to a cart but could never become an order. Keep these deterministic,
        // fictional dimensions within both seeded provider profiles' limits; also repair databases
        // that were seeded before this requirement was represented here.
        if (sku.WeightKg is null || sku.LengthCm is null || sku.WidthCm is null || sku.HeightCm is null)
        {
            sku.UpdatePackageDimensions(
                weightKg: 1.2m,
                lengthCm: 35m,
                widthCm: 20m,
                heightCm: 8m,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
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
    /// PR #34 round-7 review (DEC-BATCH-027): the canonical compatibility source
    /// (SpecificationDefinition／SpecificationOption／SkuSpecificationOptionSelection with a
    /// reviewed SpecificationSource) had no seed data for a full 8-category build, so a fresh
    /// deployment's compatibility engine could only ever see InsufficientData. Seeds one
    /// representative, mutually-compatible SKU per <see cref="CompatibilityCatalogContract.Categories"/>
    /// role through this real seeder — not a test-only DbContext write — so --seed-minimal leaves a
    /// genuine, addable-to-cart demo build behind. Values are hand-picked to pass every
    /// <see cref="CompatibilityEvaluator"/> rule with no warnings (AM5 CPU socket, X670E chipset —
    /// a real RYZEN_7000-compatible pairing per <see cref="CompatibilityRuleCatalog.CreateVersion1"/>,
    /// ATX form factor throughout, ~345W estimated draw against a 650W PSU).
    /// </summary>
    private sealed record CompatibilitySpecDefinitionTemplate(
        string SemanticKey, SpecificationValueType ValueType, bool AllowsMultiple);

    private static readonly IReadOnlyDictionary<string, CompatibilitySpecDefinitionTemplate[]>
        BuildCompatibilitySpecTemplates = new Dictionary<string, CompatibilitySpecDefinitionTemplate[]>
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

    private async Task EnsureBuildCompatibilityAsync(
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        var categoriesByCode = new Dictionary<string, Category>();
        foreach (var categoryCode in CompatibilityCatalogContract.Categories.All)
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

            foreach (var template in BuildCompatibilitySpecTemplates[categoryCode])
            {
                var existing = await dbContext.SpecificationDefinitions.SingleOrDefaultAsync(
                    definition => definition.CategoryId == category.Id && definition.SemanticKey == template.SemanticKey,
                    cancellationToken);
                if (existing is not null)
                {
                    // 組長 PR #77 裁定 A1：IsProtected 的正式語意是「必須保持啟用且必填」。早期的
                    // seed 用 isRequired: false 建過這些列，管理端因此連編輯都存不回去（受保護的
                    // 定義不准取消必填，而它們的現值就是未必填）。這裡順手把既有列修回一致狀態，
                    // 不然「只修新建資料」對已經 seed 過的資料庫等於沒修。
                    if (existing.IsProtected && (!existing.IsRequired || !existing.IsActive))
                    {
                        existing.UpdateDetails(
                            existing.DisplayNameZhTw, isRequired: true, existing.SortOrder,
                            MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
                        existing.SetActive(true, MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
                        counters.CompatibilityRecordsCreated++;
                    }

                    continue;
                }

                dbContext.SpecificationDefinitions.Add(new SpecificationDefinition(
                    Guid.CreateVersion7(), category.Id, template.SemanticKey, template.SemanticKey,
                    template.ValueType, null,
                    isRequired: true, isProtected: true, sortOrder: 0,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc, allowsMultiple: template.AllowsMultiple));
                counters.CompatibilityRecordsCreated++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (await dbContext.Skus.AnyAsync(
                entity => entity.SkuCode == "DEV-COMPAT-CPU-001", cancellationToken))
        {
            await EnsureBuildComponentSkusAreDefaultAsync(cancellationToken);
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

        // DEC-P315: every hard-rule fact the reader picks up needs a reviewed SpecificationSource
        // — one shared demo/seed source is enough here, real Catalog admin data gets its own
        // per-value provenance through the normal admin flow.
        var reviewer = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == "dev-compat-reviewer@doselect.test", cancellationToken);
        if (reviewer is null)
        {
            reviewer = ApplicationUser.CreateAdmin(
                Guid.CreateVersion7(), "dev-compat-reviewer@doselect.test", MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.Users.Add(reviewer);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CompatibilityRecordsCreated++;
        }

        var source = await dbContext.SpecificationSources.SingleOrDefaultAsync(
            candidate => candidate.ProviderName == "DoSelect Dev Seed", cancellationToken);
        if (source is null)
        {
            source = new SpecificationSource(
                Guid.CreateVersion7(),
                SpecificationSourceType.SystemEstimate,
                "DoSelect Dev Seed",
                "https://doselect.dev/seed/build-compatibility",
                "--seed-minimal demo build",
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc,
                reviewer.Id,
                "v1",
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.SpecificationSources.Add(source);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.CompatibilityRecordsCreated++;
        }

        await CreateComponentSkuAsync(
            "DEV-COMPAT-CPU-001", "懂選開發用 CPU", CompatibilityCatalogContract.Categories.Cpu,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [CompatibilityCatalogContract.SemanticKeys.CpuGeneration] = "RYZEN_7000",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 105m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-MB-001", "懂選開發用主機板", CompatibilityCatalogContract.Categories.Motherboard,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
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
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-MEM-001", "懂選開發用記憶體", CompatibilityCatalogContract.Categories.Memory,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
            {
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemoryModuleCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryKitCapacityGb] = 16m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-PSU-001", "懂選開發用電源供應器", CompatibilityCatalogContract.Categories.Psu,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
            {
                [CompatibilityCatalogContract.SemanticKeys.PsuRatedWatts] = 650m,
                [CompatibilityCatalogContract.SemanticKeys.PsuFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.PsuPcie62PinCount] = 2m,
                [CompatibilityCatalogContract.SemanticKeys.Psu12VhpwrCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PsuCpuEps8PinCount] = 2m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-CASE-001", "懂選開發用機殼", CompatibilityCatalogContract.Categories.Case,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
            {
                [CompatibilityCatalogContract.SemanticKeys.CaseGpuMaxLengthMm] = 320m,
                [CompatibilityCatalogContract.SemanticKeys.CaseCoolerMaxHeightMm] = 170m,
            },
            multiValues: new Dictionary<string, string[]>
            {
                [CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor] = ["ATX"],
                [CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor] = ["ATX"],
            },
            cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-GPU-001", "懂選開發用顯示卡（組裝用）", CompatibilityCatalogContract.Categories.Gpu,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
            {
                [CompatibilityCatalogContract.SemanticKeys.GpuLengthMm] = 280m,
                [CompatibilityCatalogContract.SemanticKeys.GpuRecommendedPsuWatts] = 450m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 200m,
                [CompatibilityCatalogContract.SemanticKeys.GpuPcie62PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.Gpu12VhpwrRequiredCount] = 0m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-STORAGE-001", "懂選開發用固態硬碟", CompatibilityCatalogContract.Categories.Storage,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
            {
                [CompatibilityCatalogContract.SemanticKeys.StorageInterface] = "M2_NVME",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            }, cancellationToken: cancellationToken);

        await CreateComponentSkuAsync(
            "DEV-COMPAT-COOLER-001", "懂選開發用散熱器", CompatibilityCatalogContract.Categories.CpuCooler,
            brand, categoriesByCode, source, counters,
            specValues: new Dictionary<string, object>
            {
                [CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm] = 150m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 10m,
            },
            multiValues: new Dictionary<string, string[]>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = ["AM5"],
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Seeds one demo SKU under <paramref name="categoryCode"/> and its hard-rule facts through
    /// the canonical multi-value model: a Decimal value writes straight to
    /// <see cref="SkuSpecificationValue.DecimalValue"/>; an Option value first gets-or-creates a
    /// <see cref="SpecificationOption"/> scoped to that category's own definition (options are not
    /// shared across categories even when the code string matches, e.g. CPU's own "AM5" option is
    /// a different row from the Motherboard's), then links via
    /// <see cref="SkuSpecificationValue.OptionId"/> (single-select, <paramref name="specValues"/>)
    /// or <see cref="SkuSpecificationOptionSelection"/> (multi-select,
    /// <paramref name="multiValues"/>) — never a bare string, which
    /// <see cref="DoSelect.Infrastructure.Catalog.EfCompatibilityCatalogReader"/> does not read for
    /// Option-typed definitions.
    /// </summary>
    private async Task CreateComponentSkuAsync(
        string skuCode,
        string name,
        string categoryCode,
        Brand brand,
        IReadOnlyDictionary<string, Category> categoriesByCode,
        SpecificationSource source,
        SeedCounters counters,
        IReadOnlyDictionary<string, object>? specValues = null,
        IReadOnlyDictionary<string, string[]>? multiValues = null,
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
        sku.UpdateCommercialDetails(
            sku.NameZhTw,
            sku.ListPrice,
            sku.UnitCost,
            isDefault: true,
            requiresPrepayment: false,
            MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        sku.UpdatePackageDimensions(
            weightKg: 1m,
            lengthCm: 30m,
            widthCm: 20m,
            heightCm: 5m,
            MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        dbContext.Skus.Add(sku);
        await dbContext.SaveChangesAsync(cancellationToken);
        counters.CompatibilityRecordsCreated++;

        if (specValues is not null)
        {
            foreach (var (semanticKey, rawValue) in specValues)
            {
                var definition = await dbContext.SpecificationDefinitions.SingleAsync(
                    candidate => candidate.CategoryId == category.Id && candidate.SemanticKey == semanticKey,
                    cancellationToken);

                if (rawValue is decimal decimalValue)
                {
                    dbContext.SkuSpecificationValues.Add(new SkuSpecificationValue(
                        sku.Id, definition.Id, null, decimalValue, null, null, source.Id,
                        MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
                    continue;
                }

                var optionCode = (string)rawValue;
                var option = await GetOrCreateOptionAsync(definition.Id, optionCode, cancellationToken);
                dbContext.SkuSpecificationValues.Add(new SkuSpecificationValue(
                    sku.Id, definition.Id, null, null, null, option.Id, source.Id,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (multiValues is not null)
        {
            foreach (var (semanticKey, optionCodes) in multiValues)
            {
                var definition = await dbContext.SpecificationDefinitions.SingleAsync(
                    candidate => candidate.CategoryId == category.Id && candidate.SemanticKey == semanticKey,
                    cancellationToken);

                foreach (var optionCode in optionCodes)
                {
                    var option = await GetOrCreateOptionAsync(definition.Id, optionCode, cancellationToken);
                    dbContext.SkuSpecificationOptionSelections.Add(new SkuSpecificationOptionSelection(
                        sku.Id, option.Id, MinimalDevelopmentSeedDefinitions.CreatedAtUtc, source.Id));
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity: 50, reorderLevel: 5,
            MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        counters.CompatibilityRecordsCreated++;
    }

    private async Task EnsureBuildComponentSkusAreDefaultAsync(CancellationToken cancellationToken)
    {
        var skuCodes = new[]
        {
            "DEV-COMPAT-CPU-001",
            "DEV-COMPAT-MB-001",
            "DEV-COMPAT-MEM-001",
            "DEV-COMPAT-GPU-001",
            "DEV-COMPAT-STORAGE-001",
            "DEV-COMPAT-PSU-001",
            "DEV-COMPAT-CASE-001",
            "DEV-COMPAT-COOLER-001",
        };
        var skus = await dbContext.Skus
            .Where(sku => skuCodes.Contains(sku.SkuCode))
            .ToListAsync(cancellationToken);
        foreach (var sku in skus.Where(sku => !sku.IsDefault))
        {
            sku.UpdateCommercialDetails(
                sku.NameZhTw,
                sku.ListPrice,
                sku.UnitCost,
                isDefault: true,
                sku.RequiresPrepayment,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        }

        foreach (var sku in skus)
        {
            // Checkout requires complete package facts. These fictional per-item packages combine
            // safely inside the seeded home-delivery profile while still exercising the real
            // package calculator.
            if (sku.WeightKg is null || sku.LengthCm is null || sku.WidthCm is null || sku.HeightCm is null)
            {
                sku.UpdatePackageDimensions(
                    weightKg: 1m,
                    lengthCm: 30m,
                    widthCm: 20m,
                    heightCm: 5m,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<SpecificationOption> GetOrCreateOptionAsync(
        long specificationDefinitionId, string code, CancellationToken cancellationToken)
    {
        var option = await dbContext.SpecificationOptions.SingleOrDefaultAsync(
            candidate => candidate.SpecificationDefinitionId == specificationDefinitionId && candidate.Code == code,
            cancellationToken);
        if (option is not null)
        {
            return option;
        }

        option = new SpecificationOption(
            Guid.CreateVersion7(), specificationDefinitionId, code, code, sortOrder: 0,
            MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
        dbContext.SpecificationOptions.Add(option);
        await dbContext.SaveChangesAsync(cancellationToken);
        return option;
    }

    /// <summary>
    /// Unlike the rest of this seeder's catalog data (a single fake dev-only product), the
    /// three <see cref="ShippingMethod"/> rows below are real, fixed business reference data —
    /// 購物車、訂單、付款與物流.md's 配送方式與費用 table names exactly these three, with these fees
    /// and thresholds, as the whole v1 shipping surface (no admin-creatable methods exist).
    /// They live here for now because this seeder is the only idempotent "ensure this row
    /// exists" mechanism in the codebase; worth confirming with the team whether reference data
    /// that must exist in every environment (not just local dev) deserves its own seeding path
    /// instead of riding along on a seeder gated behind `--seed-minimal`.
    /// </summary>
    private async Task EnsureShippingMethodsAsync(SeedCounters counters, CancellationToken cancellationToken)
    {
        var existingCodes = await dbContext.ShippingMethods
            .Select(method => method.Code)
            .ToListAsync(cancellationToken);

        var definitions = new[]
        {
            (
                PublicId: MinimalDevelopmentSeedDefinitions.StorePickupMethodPublicId,
                Code: "StorePickup",
                NameZhTw: "超商取貨",
                Kind: ShippingMethodKinds.StorePickup,
                BaseFee: 60m,
                FreeShippingThreshold: (decimal?)2000m,
                AllowsCod: true,
                RequiresPrepayment: false,
                ProviderCode: ShippingProviderCodes.StorePickup),
            (
                PublicId: MinimalDevelopmentSeedDefinitions.HomeDeliveryMethodPublicId,
                Code: "HomeDelivery",
                NameZhTw: "一般宅配",
                Kind: ShippingMethodKinds.HomeDelivery,
                BaseFee: 150m,
                FreeShippingThreshold: (decimal?)5000m,
                AllowsCod: true,
                RequiresPrepayment: false,
                ProviderCode: ShippingProviderCodes.HomeDelivery),
            (
                PublicId: MinimalDevelopmentSeedDefinitions.HomeDeliveryAssemblyMethodPublicId,
                Code: "HomeDeliveryAssembly",
                NameZhTw: "組裝電腦宅配",
                Kind: ShippingMethodKinds.HomeDeliveryAssembly,
                BaseFee: 300m,
                FreeShippingThreshold: (decimal?)30000m,
                AllowsCod: false,
                RequiresPrepayment: true,
                ProviderCode: ShippingProviderCodes.HomeDelivery),
        };

        foreach (var definition in definitions)
        {
            if (existingCodes.Contains(definition.Code))
            {
                continue;
            }

            dbContext.ShippingMethods.Add(new ShippingMethod(
                definition.PublicId,
                definition.Code,
                definition.NameZhTw,
                definition.Kind,
                definition.BaseFee,
                definition.FreeShippingThreshold,
                definition.AllowsCod,
                definition.RequiresPrepayment,
                definition.ProviderCode,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            counters.ShippingRecordsCreated++;
        }

        if (counters.ShippingRecordsCreated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// dev's Checkout (#52) resolves ShippingMethod.ProviderCode to exactly one Published
    /// ShippingProviderProfile and exactly one effective PackageLimitVersion, refusing checkout
    /// otherwise - so methods seeded without these rows would exist but never be usable. Providers
    /// follow the spec's transport classes (購物車、訂單、付款與物流.md: "超商與宅配使用不同 Provider
    /// Profile"), not the store chains: both home-delivery methods share the HomeDelivery profile.
    /// Defaults come from PackageLimitSafeRanges: the store-pickup ceiling doubles as its default,
    /// and home delivery seeds its safe-range ceiling as the explicit seed default the doc requires.
    /// </summary>
    private async Task EnsureShippingProvidersAsync(SeedCounters counters, CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            (
                ProfilePublicId: MinimalDevelopmentSeedDefinitions.StorePickupProviderProfilePublicId,
                LimitPublicId: MinimalDevelopmentSeedDefinitions.StorePickupPackageLimitPublicId,
                ProviderCode: ShippingProviderCodes.StorePickup,
                MaxWeightKg: PackageLimitSafeRanges.StorePickupDefault.MaxWeightKg,
                MaxLengthCm: PackageLimitSafeRanges.StorePickupDefault.MaxLengthCm,
                MaxWidthCm: PackageLimitSafeRanges.StorePickupDefault.MaxWidthCm,
                MaxHeightCm: PackageLimitSafeRanges.StorePickupDefault.MaxHeightCm,
                MaxTotalCm: PackageLimitSafeRanges.StorePickupDefault.MaxTotalCm,
                MaxDeclaredValue: 20_000m),
            (
                ProfilePublicId: MinimalDevelopmentSeedDefinitions.HomeDeliveryProviderProfilePublicId,
                LimitPublicId: MinimalDevelopmentSeedDefinitions.HomeDeliveryPackageLimitPublicId,
                ProviderCode: ShippingProviderCodes.HomeDelivery,
                MaxWeightKg: PackageLimitSafeRanges.HomeDelivery.MaxWeightKg,
                MaxLengthCm: PackageLimitSafeRanges.HomeDelivery.MaxSideCm,
                MaxWidthCm: PackageLimitSafeRanges.HomeDelivery.MaxSideCm,
                MaxHeightCm: PackageLimitSafeRanges.HomeDelivery.MaxSideCm,
                MaxTotalCm: PackageLimitSafeRanges.HomeDelivery.MaxTotalCm,
                MaxDeclaredValue: 50_000m),
        };

        foreach (var definition in definitions)
        {
            var profile = await dbContext.ShippingProviderProfiles
                .SingleOrDefaultAsync(candidate => candidate.ProviderCode == definition.ProviderCode, cancellationToken);
            if (profile is null)
            {
                profile = new ShippingProviderProfile(
                    definition.ProfilePublicId,
                    definition.ProviderCode,
                    version: 1,
                    status: "Published",
                    effectiveFromUtc: null,
                    effectiveToUtc: null,
                    configurationJson: "{}",
                    schemaVersion: 1,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
                dbContext.ShippingProviderProfiles.Add(profile);
                await dbContext.SaveChangesAsync(cancellationToken);
                counters.ShippingRecordsCreated++;
            }

            var hasLimit = await dbContext.PackageLimitVersions
                .AnyAsync(candidate => candidate.ProviderProfileId == profile.Id, cancellationToken);
            if (!hasLimit)
            {
                dbContext.PackageLimitVersions.Add(new PackageLimitVersion(
                    definition.LimitPublicId,
                    profile.Id,
                    version: 1,
                    definition.MaxWeightKg,
                    definition.MaxLengthCm,
                    definition.MaxWidthCm,
                    definition.MaxHeightCm,
                    definition.MaxTotalCm,
                    definition.MaxDeclaredValue,
                    effectiveFromUtc: null,
                    effectiveToUtc: null,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
                await dbContext.SaveChangesAsync(cancellationToken);
                counters.ShippingRecordsCreated++;
            }
        }
    }

    /// <summary>
    /// UC-ADM-STORE-01's "重建示範超商門市" acceptance criteria (50 7-ELEVEN + 50 FamilyMart fake
    /// stores) has no dedicated HTTP endpoint in API Endpoint目錄.md — only plain admin CRUD
    /// (GET/POST/PUT) is contracted there. Idempotent bulk seeding fits this seeder's existing
    /// "ensure this reference data exists" role better than inventing an unlisted endpoint; only
    /// adds missing rows, never deletes, so it can't violate the "referenced store can't be hard
    /// deleted" rule even if this runs again after real orders exist.
    /// </summary>
    private async Task EnsureConvenienceStoresAsync(SeedCounters counters, CancellationToken cancellationToken)
    {
        var existingCount = await dbContext.ConvenienceStores.CountAsync(cancellationToken);
        if (existingCount > 0)
        {
            return;
        }

        var cityDistricts = new (string City, string District)[]
        {
            ("台北市", "大安區"), ("台北市", "信義區"), ("台北市", "中山區"), ("台北市", "士林區"), ("台北市", "內湖區"),
            ("新北市", "板橋區"), ("新北市", "三重區"), ("新北市", "中和區"), ("新北市", "新莊區"), ("新北市", "永和區"),
            ("桃園市", "桃園區"), ("桃園市", "中壢區"), ("台中市", "西區"), ("台中市", "北屯區"), ("台中市", "南屯區"),
            ("台南市", "東區"), ("台南市", "中西區"), ("高雄市", "苓雅區"), ("高雄市", "三民區"), ("新竹市", "東區"),
        };

        foreach (var (providerCode, prefix) in new[] { ("7-11", "SEVEN"), ("FamilyMart", "FAMILY") })
        {
            for (var i = 1; i <= 50; i++)
            {
                var (city, district) = cityDistricts[(i - 1) % cityDistricts.Length];
                dbContext.ConvenienceStores.Add(new ConvenienceStore(
                    Guid.CreateVersion7(),
                    providerCode,
                    $"{prefix}-{i:000}",
                    $"{providerCode} {city}{district}{i:000}門市",
                    $"{city}{district}示範路{i}號",
                    city,
                    district,
                    isDemoData: true,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
                counters.ShippingRecordsCreated++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the fixed, fictional starting state used by the WP-A04 browser journey. The
    /// journey deliberately starts at Cart (matching the Demo runbook's prebuilt-cart fallback),
    /// then exercises the real Checkout, reservation, payment, guest-access and invoice paths.
    /// This data is created only when the explicit <c>--seed-minimal</c> command runs and is safe
    /// to rebuild idempotently; no runtime startup path creates it.
    /// </summary>
    private async Task EnsureCoreTransactionJourneyAsync(CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Coupons.SingleOrDefaultAsync(
            candidate => candidate.Code == "CREATOR10",
            cancellationToken);
        if (coupon is null)
        {
            coupon = new Coupon(
                MinimalDevelopmentSeedDefinitions.Creator10CouponPublicId,
                new CouponCreation(
                    "CREATOR10",
                    "創作者指定分類九折",
                    CouponDiscountType.Percentage,
                    0.10m,
                    20_000m,
                    2_000m,
                    MinimalDevelopmentSeedDefinitions.CreatedAtUtc,
                    new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    10_000,
                    1,
                    false,
                    false,
                    CouponScopeType.Restricted),
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            coupon.ActivateNow(
                CouponUsageState.Unused,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc);
            dbContext.Coupons.Add(coupon);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var eligibleCategoryCodes = new[]
        {
            CompatibilityCatalogContract.Categories.Cpu,
            CompatibilityCatalogContract.Categories.Gpu,
            CompatibilityCatalogContract.Categories.Memory,
        };
        var eligibleCategoryIds = await dbContext.Categories
            .Where(category => eligibleCategoryCodes.Contains(category.Code))
            .Select(category => category.Id)
            .ToListAsync(cancellationToken);
        if (eligibleCategoryIds.Count != eligibleCategoryCodes.Length)
        {
            throw new InvalidOperationException(
                "The core transaction seed requires the CPU, GPU, and memory categories.");
        }

        var existingCategoryIds = await dbContext.CouponCategories
            .Where(link => link.CouponId == coupon.Id)
            .Select(link => link.CategoryId)
            .ToListAsync(cancellationToken);
        dbContext.CouponCategories.AddRange(eligibleCategoryIds
            .Except(existingCategoryIds)
            .Select(categoryId => new CouponCategory(
                coupon.Id,
                categoryId,
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc)));

        if (await dbContext.Carts.AnyAsync(
                cart => cart.PublicId == MinimalDevelopmentSeedDefinitions.CoreTransactionGuestCartPublicId,
                cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        var cart = Cart.CreateForGuest(
            MinimalDevelopmentSeedDefinitions.CoreTransactionGuestCartPublicId,
            SHA256.HashData(Encoding.UTF8.GetBytes(
                MinimalDevelopmentSeedDefinitions.CoreTransactionGuestCartKey)),
            now.AddDays(30),
            now);
        dbContext.Carts.Add(cart);
        await dbContext.SaveChangesAsync(cancellationToken);

        var assemblySkuCodes = new[]
        {
            "DEV-COMPAT-CPU-001",
            "DEV-COMPAT-MB-001",
            "DEV-COMPAT-MEM-001",
            "DEV-COMPAT-GPU-001",
            "DEV-COMPAT-STORAGE-001",
            "DEV-COMPAT-PSU-001",
            "DEV-COMPAT-CASE-001",
            "DEV-COMPAT-COOLER-001",
        };
        var assemblySkus = await dbContext.Skus
            .Where(sku => assemblySkuCodes.Contains(sku.SkuCode))
            .ToListAsync(cancellationToken);
        if (assemblySkus.Count != assemblySkuCodes.Length)
        {
            throw new InvalidOperationException(
                "The core transaction seed requires all eight compatible component SKUs.");
        }

        var standaloneGpu = assemblySkus.Single(sku => sku.SkuCode == "DEV-COMPAT-GPU-001");

        dbContext.CartItems.AddRange(assemblySkus.Select(sku => new CartItem(
            Guid.CreateVersion7(),
            cart.Id,
            sku.Id,
            1,
            MinimalDevelopmentSeedDefinitions.CoreTransactionAssemblyGroupKey,
            now)));
        // The extra standalone GPU keeps the compatibility catalogue's established NT$5,000
        // per-component price contract intact while bringing CPU／GPU／Memory coupon-eligible
        // subtotal to CREATOR10's NT$20,000 threshold.
        dbContext.CartItems.Add(new CartItem(
            Guid.CreateVersion7(),
            cart.Id,
            standaloneGpu.Id,
            1,
            assemblyGroupKey: null,
            now));

        cart.Touch(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// M-13 WP4（alex 2026-09-05 #98 A1 裁定）：退款 E2E 需要一筆已付款、已送達的訂單才能
    /// 建立退貨申請，但目前 production 沒有任何 HTTP 可達的路徑把 FulfillmentStatus 推進
    /// Delivered——物流狀態命令屬於另一個範圍，尚未落地。這裡只頂住「訂單、付款、出貨」這段
    /// 裁定明確允許 seed 的前置資料；從建立退貨申請開始，E2E 一律走 production API／UI，
    /// 不得再往後 seed 任何 Return／Refund 狀態。
    /// </summary>
    private async Task EnsureRefundJourneyOrderAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Orders.AnyAsync(
                order => order.PublicId == MinimalDevelopmentSeedDefinitions.RefundJourneyOrderPublicId,
                cancellationToken))
        {
            return;
        }

        var homeDeliveryProfile = await dbContext.ShippingProviderProfiles.SingleAsync(
            profile => profile.PublicId ==
                MinimalDevelopmentSeedDefinitions.HomeDeliveryProviderProfilePublicId,
            cancellationToken);
        var homeDeliveryPackageLimit = await dbContext.PackageLimitVersions.SingleAsync(
            limit => limit.PublicId ==
                MinimalDevelopmentSeedDefinitions.HomeDeliveryPackageLimitPublicId,
            cancellationToken);
        var sku = await dbContext.Skus.SingleAsync(
            candidate => candidate.PublicId == MinimalDevelopmentSeedDefinitions.SkuPublicId,
            cancellationToken);

        var createdAtUtc = MinimalDevelopmentSeedDefinitions.CreatedAtUtc;
        var deliveredAtUtc = createdAtUtc.AddDays(3);

        var order = Order.Create(
            MinimalDevelopmentSeedDefinitions.RefundJourneyOrderPublicId,
            new OrderCreation(
                MinimalDevelopmentSeedDefinitions.RefundJourneyOrderNumber,
                null,
                MinimalDevelopmentSeedDefinitions.RefundJourneyBuyerEmail,
                OrderStatus.Completed,
                PaymentStatus.Paid,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                19_900m, 0m, 100m, 0m, 20_000m,
                "退款 E2E 收件人", "0912345678", MinimalDevelopmentSeedDefinitions.RefundJourneyBuyerEmail,
                "100", "台北市", "中正區", "測試路 1 號", null,
                "HomeDelivery", homeDeliveryProfile.Id, null, null, null,
                1, 1, null, null, "e2e-refund-journey-seed", null,
                1, 1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    MinimalDevelopmentSeedDefinitions.RefundJourneyBuyerEmail,
                    null, null, null, null),
                null,
                null,
                new OrderPackageSnapshot(homeDeliveryPackageLimit.Id, 1.2m, 40m, 30m, 20m, 90m, 20_000m)),
            createdAtUtc);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        // InvoiceCalculator 核對發票行項與 Order.PaidAmount 是否一致（IssueInvoiceService 走
        // production 手動開立路徑，不是這支 seed 自己模擬付款成功事件），沒有這行金額對不上，
        // 開立發票會丟 ArgumentException。
        order.ApplyPaymentProjection(PaymentStatus.Paid, 20_000m, createdAtUtc);
        order.ApplyFulfillmentProjection(FulfillmentStatus.Delivered, deliveredAtUtc);

        dbContext.OrderItems.Add(new OrderItem(
            MinimalDevelopmentSeedDefinitions.RefundJourneyOrderItemPublicId,
            order.Id,
            sku.Id,
            sku.SkuCode,
            "懂選開發用顯示卡",
            "16GB",
            quantity: 1,
            listUnitPrice: 19_900m,
            saleUnitPrice: 19_900m,
            finalUnitPrice: 19_900m,
            unitCostSnapshot: 15_000m,
            lineSubtotal: 19_900m,
            discountAllocation: 0m,
            lineTotal: 19_900m,
            assemblyGroupKey: null,
            returnableQuantity: 1,
            createdAtUtc,
            isCouponEligible: false,
            new OrderItemSpecificationSnapshot("E2E 退款旅程測試品項", "{}", 1)));

        var paymentAttempt = new PaymentAttempt(
            Guid.CreateVersion7(), order.Id, PaymentMethod.CreditCard, 20_000m,
            "SIMULATED", "e2e-refund-journey-seed-payment", null, createdAtUtc);
        paymentAttempt.Transition(PaymentAttemptStatus.AwaitingPayment, createdAtUtc);
        paymentAttempt.Transition(PaymentAttemptStatus.Processing, createdAtUtc);
        paymentAttempt.Transition(PaymentAttemptStatus.Paid, createdAtUtc);
        dbContext.PaymentAttempts.Add(paymentAttempt);

        await dbContext.SaveChangesAsync(cancellationToken);
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
        public int ShippingRecordsCreated { get; set; }
    }
}

public sealed record MinimalDevelopmentSeedResult(
    int RolesCreated,
    int UsersCreated,
    int ProfilesCreated,
    int CatalogRecordsCreated,
    int CompatibilityRecordsCreated,
    int ShippingRecordsCreated);
