using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberCleanupGateway(
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<MemberCleanupGateway> logger) : IMemberCleanupGateway
{
    public async Task<int> AnonymizeStaleUnverifiedMembersAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default)
    {
        var staleUserIds = await userManager.Users
            .Where(user =>
                user.AccountType == AccountType.Member &&
                user.AccountStatus == AccountStatus.PendingEmailVerification &&
                user.CreatedAtUtc < olderThanUtc)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var anonymizedCount = 0;

        foreach (var userId in staleUserIds)
        {
            // Each account gets its own DI scope — and therefore its own DbContext/UserManager —
            // rather than sharing the one this gateway was constructed with. A transaction
            // rollback only undoes what was actually sent to the database; it does not revert the
            // EF Core ChangeTracker's in-memory Modified state on the entities this method mutated
            // (profile.Anonymize/user.Anonymize below). Sharing one DbContext across the whole
            // batch meant a single failed account left stale tracked changes that the *next*
            // account's SaveChanges could flush right alongside its own, silently anonymizing an
            // account whose own UpdateAsync had actually failed (Alex review, 2026-08-25 — verified
            // live: reverting to a shared DbContext reproduces exactly this, caught by
            // MemberCleanupTests.PurgeAsync_WhenOneAccountsUpdateFails_StillAnonymizesTheOtherAccountIndependently).
            if (await TryAnonymizeOneAsync(userId, nowUtc, cancellationToken))
            {
                anonymizedCount++;
            }
        }

        return anonymizedCount;
    }

    private async Task<bool> TryAnonymizeOneAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedDbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var scopedUserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await scopedUserManager.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        // A PendingEmailVerification account can never sign in (MemberLoginGateway rejects it
        // with EmailUnverified), so it should never have been able to place an order. This is a
        // defensive guard against that invariant being violated (e.g. by a future change) — not
        // the primary line of defense. "沒有訂單或其他必須保存的資料" (會員、驗證與通知.md): any
        // account that somehow does have one is skipped rather than anonymized.
        var hasOrder = await scopedDbContext.Orders
            .AnyAsync(order => order.MemberUserId == user.Id, cancellationToken);
        if (hasOrder)
        {
            return false;
        }

        await using var transaction = await scopedDbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await scopedDbContext.MemberProfiles
            .SingleOrDefaultAsync(candidate => candidate.UserId == user.Id, cancellationToken);
        profile?.Anonymize(nowUtc);

        user.Anonymize(nowUtc);

        // UserManager.UpdateAsync saves through the same (scoped) DbContext, so it flushes the
        // profile mutation above together with the user mutation inside the same transaction.
        var result = await scopedUserManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            // No PII here — just the opaque PublicId and Identity's generic error codes, so this
            // is safe to log even though the background service's own success log only reports an
            // aggregate count. Without this, a persistent per-account failure was only visible
            // indirectly, as a lower-than-expected anonymized count (Alex review, 2026-08-25).
            logger.LogWarning(
                "Failed to anonymize unverified member {PublicId} during cleanup: {ErrorCodes}.",
                user.PublicId,
                string.Join(", ", result.Errors.Select(error => error.Code)));

            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
