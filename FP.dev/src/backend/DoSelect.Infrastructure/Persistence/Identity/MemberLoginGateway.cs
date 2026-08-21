using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberLoginGateway(
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IMemberLoginGateway
{
    public async Task<MemberLoginOutcome> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || user.AccountType != AccountType.Member)
        {
            return new MemberLoginOutcome.InvalidCredentials();
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (signInResult.IsLockedOut)
        {
            var lockoutEndUtc = await userManager.GetLockoutEndDateAsync(user) ?? DateTimeOffset.UtcNow;
            return new MemberLoginOutcome.LockedOut(lockoutEndUtc);
        }

        if (!signInResult.Succeeded)
        {
            return new MemberLoginOutcome.InvalidCredentials();
        }

        // Account lifecycle is only disclosed once the password is already known to be correct,
        // so an attacker without the password cannot use it to probe account state.
        switch (user.AccountStatus)
        {
            case AccountStatus.Suspended:
                return new MemberLoginOutcome.Suspended();
            case AccountStatus.PendingEmailVerification:
                return new MemberLoginOutcome.EmailUnverified();
            case AccountStatus.Active:
                break;
            default:
                return new MemberLoginOutcome.InvalidCredentials();
        }

        var displayName = await dbContext.MemberProfiles
            .Where(profile => profile.UserId == user.Id)
            .Select(profile => profile.DisplayName)
            .SingleOrDefaultAsync(cancellationToken) ?? user.Email ?? email;

        return new MemberLoginOutcome.Success(
            user.PublicId,
            user.Id,
            displayName,
            user.Email!,
            user.AccountStatus,
            user.PreferredLocale,
            user.SecurityStamp!);
    }

    public async Task<MemberSessionSnapshot?> FindActiveMemberByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .Where(user => user.Id == userId && user.AccountStatus == AccountStatus.Active)
            .Join(
                dbContext.MemberProfiles,
                user => user.Id,
                profile => profile.UserId,
                (user, profile) => new MemberSessionSnapshot(
                    user.PublicId,
                    profile.DisplayName,
                    user.Email!,
                    user.EmailConfirmed,
                    user.PreferredLocale))
            .SingleOrDefaultAsync(cancellationToken);
}
