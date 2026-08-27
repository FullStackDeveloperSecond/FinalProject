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

        await transaction.CommitAsync(cancellationToken);
        return new MinimalDevelopmentSeedResult(
            counters.RolesCreated,
            counters.UsersCreated,
            counters.ProfilesCreated,
            counters.CatalogRecordsCreated);
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
    }
}

public sealed record MinimalDevelopmentSeedResult(
    int RolesCreated,
    int UsersCreated,
    int ProfilesCreated,
    int CatalogRecordsCreated);
