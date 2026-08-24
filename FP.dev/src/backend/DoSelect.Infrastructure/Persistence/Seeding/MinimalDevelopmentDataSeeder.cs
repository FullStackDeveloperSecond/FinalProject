using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
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
        await EnsureShippingAsync(counters, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new MinimalDevelopmentSeedResult(
            counters.RolesCreated,
            counters.UsersCreated,
            counters.ProfilesCreated,
            counters.CatalogRecordsCreated,
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

        var skuExists = await dbContext.Skus.AnyAsync(
            entity => entity.SkuCode == "DEV-GPU-001-16G",
            cancellationToken);
        if (skuExists)
        {
            return;
        }

        var sku = new Sku(
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

    /// <summary>
    /// 購物車、訂單、付款與物流.md's fixed, non-negotiable v1 shipping data: 3 ShippingMethod rows
    /// with their exact fees／thresholds, 2 ShippingProviderProfile+PackageLimitVersion pairs
    /// (already Published — there is no "first ever version" draft step), and 100 demo
    /// ConvenienceStore rows split 50/50 between 7-ELEVEN and FamilyMart.
    /// </summary>
    private async Task EnsureShippingAsync(SeedCounters counters, CancellationToken cancellationToken)
    {
        var now = MinimalDevelopmentSeedDefinitions.CreatedAtUtc;

        if (!await dbContext.ShippingMethods.AnyAsync(cancellationToken))
        {
            dbContext.ShippingMethods.AddRange(
                new ShippingMethod(
                    MinimalDevelopmentSeedDefinitions.CvsPickupMethodPublicId,
                    "CVS_PICKUP", "超商取貨",
                    ShippingProviderCodes.ConvenienceStore,
                    baseFee: 60m, freeShippingThreshold: 2000m,
                    allowsCod: false, requiresPrepayment: false, now),
                new ShippingMethod(
                    MinimalDevelopmentSeedDefinitions.HomeDeliveryMethodPublicId,
                    "HOME_DELIVERY", "一般宅配",
                    ShippingProviderCodes.HomeDelivery,
                    baseFee: 150m, freeShippingThreshold: 5000m,
                    allowsCod: true, requiresPrepayment: false, now),
                new ShippingMethod(
                    MinimalDevelopmentSeedDefinitions.HomeDeliveryAssemblyMethodPublicId,
                    "HOME_DELIVERY_ASSEMBLY", "組裝電腦宅配",
                    ShippingProviderCodes.HomeDelivery,
                    baseFee: 300m, freeShippingThreshold: 30000m,
                    allowsCod: false, requiresPrepayment: true, now));
            counters.ShippingRecordsCreated += 3;
        }

        if (!await dbContext.ShippingProviderProfiles.AnyAsync(cancellationToken))
        {
            var cvsProfile = new ShippingProviderProfile(
                MinimalDevelopmentSeedDefinitions.ConvenienceStoreProviderProfilePublicId,
                ShippingProviderCodes.ConvenienceStore,
                version: 1, ShippingProviderProfile.PublishedStatus,
                effectiveFromUtc: now, effectiveToUtc: null,
                configurationJson: "{}", schemaVersion: 1, now);
            var homeProfile = new ShippingProviderProfile(
                MinimalDevelopmentSeedDefinitions.HomeDeliveryProviderProfilePublicId,
                ShippingProviderCodes.HomeDelivery,
                version: 1, ShippingProviderProfile.PublishedStatus,
                effectiveFromUtc: now, effectiveToUtc: null,
                configurationJson: "{}", schemaVersion: 1, now);
            dbContext.ShippingProviderProfiles.AddRange(cvsProfile, homeProfile);
            await dbContext.SaveChangesAsync(cancellationToken);
            counters.ShippingRecordsCreated += 2;

            dbContext.PackageLimitVersions.AddRange(
                new PackageLimitVersion(
                    MinimalDevelopmentSeedDefinitions.ConvenienceStorePackageLimitPublicId,
                    cvsProfile.Id, version: 1,
                    maxWeightKg: 5m, maxLengthCm: 45m, maxWidthCm: 45m, maxHeightCm: 45m,
                    maxTotalCm: 105m, maxDeclaredValue: 100000m,
                    effectiveFromUtc: now, effectiveToUtc: null, now),
                new PackageLimitVersion(
                    MinimalDevelopmentSeedDefinitions.HomeDeliveryPackageLimitPublicId,
                    homeProfile.Id, version: 1,
                    maxWeightKg: 20m, maxLengthCm: 150m, maxWidthCm: 150m, maxHeightCm: 150m,
                    maxTotalCm: 150m, maxDeclaredValue: 200000m,
                    effectiveFromUtc: now, effectiveToUtc: null, now));
            counters.ShippingRecordsCreated += 2;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (!await dbContext.ConvenienceStores.AnyAsync(cancellationToken))
        {
            var stores = new List<ConvenienceStore>(100);
            stores.AddRange(BuildDemoStores("7-ELEVEN", "SEVEN", now));
            stores.AddRange(BuildDemoStores("FamilyMart", "FAMI", now));
            dbContext.ConvenienceStores.AddRange(stores);
            counters.ShippingRecordsCreated += stores.Count;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static readonly (string City, string District)[] DemoStoreDistricts =
    [
        ("台北市", "信義區"), ("台北市", "大安區"), ("新北市", "板橋區"), ("台中市", "西屯區"),
        ("台南市", "東區"), ("高雄市", "三民區"), ("桃園市", "中壢區"), ("新竹市", "東區"),
    ];

    private static IEnumerable<ConvenienceStore> BuildDemoStores(
        string providerCode, string storeCodePrefix, DateTime createdAtUtc)
    {
        for (var index = 1; index <= 50; index++)
        {
            var (city, district) = DemoStoreDistricts[(index - 1) % DemoStoreDistricts.Length];
            var storeCode = $"{storeCodePrefix}-{index:D3}";
            yield return new ConvenienceStore(
                CreateDeterministicGuid($"ConvenienceStore:{providerCode}:{storeCode}"),
                providerCode,
                storeCode,
                $"{providerCode} 專題展示門市 {index:D3}",
                $"{city}{district}展示路{index}號",
                city,
                district,
                isDemoData: true,
                createdAtUtc);
        }
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
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

        public int ShippingRecordsCreated { get; set; }
    }
}

public sealed record MinimalDevelopmentSeedResult(
    int RolesCreated,
    int UsersCreated,
    int ProfilesCreated,
    int CatalogRecordsCreated,
    int ShippingRecordsCreated);
