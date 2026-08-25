using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberRegistrationGateway(
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : IMemberRegistrationGateway
{
    // SQL Server unique-index violation numbers (duplicate key). Identity's own uniqueness
    // pre-check (inside CreateAsync) is a plain SELECT before the INSERT, so two concurrent
    // registrations for the same brand-new email can both pass it and both reach Store.CreateAsync
    // — only one INSERT wins; the loser throws here rather than returning a graceful
    // IdentityResult. Without catching it, that races into a raw 500 instead of the fixed 202
    // every registration is supposed to return (Alex review, 2026-08-25).
    private const int DuplicateKeyOnUniqueIndex = 2601;
    private const int DuplicateKeyOnPrimaryOrUniqueConstraint = 2627;


    public async Task<CreateMemberOutcome> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var user = ApplicationUser.CreateMember(Guid.CreateVersion7(), request.Email, nowUtc);
        user.ChangePreferredLocale(request.Locale, nowUtc);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Deliberately no upfront FindByEmailAsync short-circuit here: UserManager.CreateAsync
        // hashes the password *before* checking uniqueness regardless of outcome, so routing every
        // request through it (rather than pre-checking and returning early for a duplicate) means
        // the expensive part of a fresh registration is already paid identically on the
        // already-registered path. Skipping straight to a rejection here would let response latency
        // itself become an account-enumeration oracle (Alex review, 2026-08-24).
        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException dbUpdateException) when (IsUniqueIndexViolation(dbUpdateException))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CreateMemberOutcome.EmailInUse();
        }

        if (!createResult.Succeeded)
        {
            var isDuplicate = createResult.Errors.Any(
                error => error.Code.Contains("DuplicateEmail", StringComparison.Ordinal) ||
                         error.Code.Contains("DuplicateUserName", StringComparison.Ordinal));

            if (isDuplicate)
            {
                // Match the cost of the token-generation step the success path below performs (a
                // discarded token here, in-memory only) so a duplicate email doesn't short-circuit
                // out before that work the way it would if we returned immediately on failure.
                await userManager.GenerateEmailConfirmationTokenAsync(user);
            }

            await transaction.RollbackAsync(cancellationToken);

            if (isDuplicate)
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

    // Only recognizes the specific unique-index violation this race can produce; any other
    // DbUpdateException (a real constraint violation, connectivity failure, etc.) is left to
    // propagate rather than being silently mapped to EmailInUse.
    private static bool IsUniqueIndexViolation(DbUpdateException exception)
    {
        for (var current = (Exception)exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException &&
                sqlException.Errors.Cast<SqlError>().Any(error =>
                    error.Number is DuplicateKeyOnUniqueIndex or DuplicateKeyOnPrimaryOrUniqueConstraint))
            {
                return true;
            }
        }

        return false;
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

        // EmailConfirmed, AccountStatus, and SecurityStamp must move together: Identity's
        // ConfirmEmailAsync persists EmailConfirmed=true as a side effect of successful token
        // validation, independent of the AccountStatus transition and stamp rotation that follow.
        // Without a shared transaction, a failure or interruption between these calls could leave
        // EmailConfirmed=true stuck on a still-PendingEmailVerification account, or an Active
        // account whose reset-token-invalidating stamp rotation never happened (Alex review,
        // 2026-08-24). Wrapping all three in one transaction makes the confirmation atomic: either
        // every write lands, or none of them do.
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var confirmResult = await userManager.ConfirmEmailAsync(user, token);
        if (!confirmResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ConfirmMemberEmailOutcome.TokenRejected();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        user.ConfirmEmail(nowUtc);
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
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
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to rotate security stamp for user '{user.PublicId}': " +
                string.Join("; ", stampResult.Errors.Select(error => error.Description)));
        }

        await transaction.CommitAsync(cancellationToken);

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
