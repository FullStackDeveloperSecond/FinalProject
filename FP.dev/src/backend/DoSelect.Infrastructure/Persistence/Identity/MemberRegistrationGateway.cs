using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberRegistrationGateway(
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : IMemberRegistrationGateway
{
    public async Task<CreateMemberOutcome> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return new CreateMemberOutcome.EmailInUse();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var user = ApplicationUser.CreateMember(Guid.CreateVersion7(), request.Email, nowUtc);
        user.ChangePreferredLocale(request.Locale, nowUtc);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            if (createResult.Errors.Any(error => error.Code.Contains("DuplicateEmail", StringComparison.Ordinal) ||
                                                   error.Code.Contains("DuplicateUserName", StringComparison.Ordinal)))
            {
                return new CreateMemberOutcome.EmailInUse();
            }

            return new CreateMemberOutcome.PasswordRejected(
                createResult.Errors.Select(error => error.Description).ToArray());
        }

        dbContext.MemberProfiles.Add(new MemberProfile(
            user.Id,
            user.PublicId,
            request.DisplayName,
            null,
            nowUtc));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        await transaction.CommitAsync(cancellationToken);

        return new CreateMemberOutcome.Success(
            user.PublicId,
            user.Email!,
            user.AccountStatus,
            token);
    }

    public async Task<ConfirmMemberEmailOutcome> ConfirmEmailAsync(
        Guid userPublicId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(
            candidate => candidate.PublicId == userPublicId,
            cancellationToken);

        // Guard on status before touching UserManager.ConfirmEmailAsync at all: that call sets
        // EmailConfirmed=true and persists as a side effect of successful token validation,
        // independent of AccountStatus. A token issued while pending verification must not be
        // able to reactivate a Suspended (or otherwise non-pending) account, so ineligible status
        // is rejected up front rather than relying on the domain method's guard as the only line
        // of defense.
        if (user is null || user.AccountStatus != AccountStatus.PendingEmailVerification)
        {
            return new ConfirmMemberEmailOutcome.TokenRejected();
        }

        var confirmResult = await userManager.ConfirmEmailAsync(user, token);
        if (!confirmResult.Succeeded)
        {
            return new ConfirmMemberEmailOutcome.TokenRejected();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        user.ConfirmEmail(nowUtc);
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to persist email confirmation for user '{user.PublicId}': " +
                string.Join("; ", updateResult.Errors.Select(error => error.Description)));
        }

        // Rotate the security stamp so the token cannot be replayed: DataProtectorTokenProvider
        // binds the protected payload to the user's SecurityStamp, so any further
        // ConfirmEmailAsync call with the same token fails validation before it reaches this
        // method once the stamp changes.
        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to rotate security stamp for user '{user.PublicId}': " +
                string.Join("; ", stampResult.Errors.Select(error => error.Description)));
        }

        return new ConfirmMemberEmailOutcome.Success(user.AccountStatus);
    }

    public async Task<RequestMemberEmailVerificationOutcome> RequestEmailVerificationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null ||
            user.AccountType != AccountType.Member ||
            user.AccountStatus != AccountStatus.PendingEmailVerification)
        {
            return new RequestMemberEmailVerificationOutcome.NotEligible();
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return new RequestMemberEmailVerificationOutcome.Issued(user.PublicId, user.Email!, token);
    }
}
