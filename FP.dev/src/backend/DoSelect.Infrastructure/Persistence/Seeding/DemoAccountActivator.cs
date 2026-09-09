using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DoSelect.Infrastructure.Persistence.Seeding;

public sealed class DemoAccountActivator(
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IConfiguration configuration)
{
    public const string AdminEmail = "demo-admin@example.invalid";
    public const string MemberEmail = "member-0001@example.invalid";

    private const string AdminUserId = "demo-admin-0001";
    private static readonly string MemberUserId = DemoDataSeeder.MemberUserId(0);

    private static readonly string[] AdminRoles =
    [
        "SuperAdmin",
        "CustomerServiceSupervisor",
    ];

    // Local CLI only. Never expose this payload from an HTTP route or write it to application logs.
    public async Task ExportLoginPackAsync(string path, CancellationToken cancellationToken = default)
    {
        DemoDatabaseSafety.EnsureAllowedIsolatedLocalDatabase(dbContext);
        var passwords = MinimalDevelopmentSeedDefinitions.GetPasswords(configuration);
        var admin = await FindRequiredUserAsync(AdminUserId, AdminEmail, AccountType.Admin, cancellationToken);
        var member = await FindRequiredUserAsync(MemberUserId, MemberEmail, AccountType.Member, cancellationToken);
        if (!await userManager.CheckPasswordAsync(admin, passwords.AdminPassword) || !await userManager.CheckPasswordAsync(member, passwords.MemberPassword))
            throw new InvalidOperationException("Demo account passwords do not match the configured seed credentials. No pack was exported.");
        var pack = new
        {
            version = 1,
            database = dbContext.Database.GetDbConnection().Database,
            accounts = new[]
            {
                new { label = "展示會員", accountType = "member", email = MemberEmail, password = passwords.MemberPassword },
                new { label = "展示管理員", accountType = "admin", email = AdminEmail, password = passwords.AdminPassword },
            },
        };
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, pack, cancellationToken: cancellationToken);
    }

    public async Task<DemoAccountActivationResult> ActivateAsync(
        CancellationToken cancellationToken = default)
    {
        DemoDatabaseSafety.EnsureAllowedIsolatedLocalDatabase(dbContext);

        var countsBefore = await DemoDataSnapshotReader.ReadCountsAsync(
            dbContext,
            cancellationToken);
        if (!DemoDataSnapshotReader.CountsMatchManifest(countsBefore) ||
            !await dbContext.Brands.AnyAsync(
                brand => brand.Code == DemoSeedManifest.MarkerBrandCode,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Demo account activation requires a complete, matching 10,000-record demo seed.");
        }

        var passwords = MinimalDevelopmentSeedDefinitions.GetPasswords(configuration);
        var admin = await FindRequiredUserAsync(
            AdminUserId,
            AdminEmail,
            AccountType.Admin,
            cancellationToken);
        var member = await FindRequiredUserAsync(
            MemberUserId,
            MemberEmail,
            AccountType.Member,
            cancellationToken);

        if (!await dbContext.AdminProfiles.AnyAsync(
                profile => profile.UserId == admin.Id && profile.IsActive,
                cancellationToken) ||
            !await dbContext.MemberProfiles.AnyAsync(
                profile => profile.UserId == member.Id,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "The fixed demo account profiles are missing or inactive.");
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rolesAssigned = 0;
        foreach (var roleName in AdminRoles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                EnsureSucceeded(
                    $"create role '{roleName}'",
                    await roleManager.CreateAsync(new IdentityRole(roleName)));
            }

            if (!await userManager.IsInRoleAsync(admin, roleName))
            {
                EnsureSucceeded(
                    $"assign role '{roleName}'",
                    await userManager.AddToRoleAsync(admin, roleName));
                rolesAssigned++;
            }
        }

        await SetPasswordAsync(admin, passwords.AdminPassword);
        await SetPasswordAsync(member, passwords.MemberPassword);

        var countsAfter = await DemoDataSnapshotReader.ReadCountsAsync(
            dbContext,
            cancellationToken);
        if (!DemoDataSnapshotReader.CountsMatchManifest(countsAfter))
        {
            throw new InvalidOperationException(
                "Demo account activation changed the fixed 10,000-record allocation.");
        }

        await transaction.CommitAsync(cancellationToken);
        return new DemoAccountActivationResult(
            AdminPasswordUpdated: true,
            MemberPasswordUpdated: true,
            RolesAssigned: rolesAssigned,
            MainBusinessRecordTotal: countsAfter.Values.Sum(),
            AdminPublicId: admin.PublicId);
    }

    private async Task<ApplicationUser> FindRequiredUserAsync(
        string userId,
        string email,
        AccountType accountType,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId && candidate.Email == email,
            cancellationToken);
        if (user is null || user.AccountType != accountType ||
            user.AccountStatus != AccountStatus.Active || !user.EmailConfirmed)
        {
            throw new InvalidOperationException(
                $"The fixed demo {accountType.ToString().ToLowerInvariant()} identity is missing or inactive.");
        }

        return user;
    }

    private async Task SetPasswordAsync(ApplicationUser user, string password)
    {
        if (await userManager.HasPasswordAsync(user))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            EnsureSucceeded(
                "reset the demo account password",
                await userManager.ResetPasswordAsync(user, resetToken, password));
            return;
        }

        EnsureSucceeded(
            "add the demo account password",
            await userManager.AddPasswordAsync(user, password));
    }

    private static void EnsureSucceeded(string operation, IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unable to {operation}: " +
            string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}

public sealed record DemoAccountActivationResult(
    bool AdminPasswordUpdated,
    bool MemberPasswordUpdated,
    int RolesAssigned,
    int MainBusinessRecordTotal,
    Guid AdminPublicId);
