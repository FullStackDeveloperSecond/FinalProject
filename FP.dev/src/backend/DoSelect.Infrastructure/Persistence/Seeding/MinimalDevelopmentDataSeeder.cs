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
        await EnsureShippingMethodsAsync(counters, cancellationToken);

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
                Kind: "StorePickup",
                BaseFee: 60m,
                FreeShippingThreshold: (decimal?)2000m,
                AllowsCod: true,
                RequiresPrepayment: false),
            (
                PublicId: MinimalDevelopmentSeedDefinitions.HomeDeliveryMethodPublicId,
                Code: "HomeDelivery",
                NameZhTw: "一般宅配",
                Kind: "HomeDelivery",
                BaseFee: 150m,
                FreeShippingThreshold: (decimal?)5000m,
                AllowsCod: true,
                RequiresPrepayment: false),
            (
                PublicId: MinimalDevelopmentSeedDefinitions.HomeDeliveryAssemblyMethodPublicId,
                Code: "HomeDeliveryAssembly",
                NameZhTw: "組裝電腦宅配",
                Kind: "HomeDeliveryAssembly",
                BaseFee: 300m,
                FreeShippingThreshold: (decimal?)30000m,
                AllowsCod: false,
                RequiresPrepayment: true),
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
                MinimalDevelopmentSeedDefinitions.CreatedAtUtc));
            counters.ShippingRecordsCreated++;
        }

        if (counters.ShippingRecordsCreated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
