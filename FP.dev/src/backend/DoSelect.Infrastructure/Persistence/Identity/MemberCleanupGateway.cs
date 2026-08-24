using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberCleanupGateway(
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : IMemberCleanupGateway
{
    public async Task<int> AnonymizeStaleUnverifiedMembersAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default)
    {
        var staleUsers = await userManager.Users
            .Where(user =>
                user.AccountType == AccountType.Member &&
                user.AccountStatus == AccountStatus.PendingEmailVerification &&
                user.CreatedAtUtc < olderThanUtc)
            .ToListAsync(cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var anonymizedCount = 0;

        foreach (var user in staleUsers)
        {
            // A PendingEmailVerification account can never sign in (MemberLoginGateway rejects it
            // with EmailUnverified), so it should never have been able to place an order. This is
            // a defensive guard against that invariant being violated (e.g. by a future change) —
            // not the primary line of defense. "沒有訂單或其他必須保存的資料" (會員、驗證與通知.md):
            // any account that somehow does have one is skipped rather than anonymized.
            var hasOrder = await dbContext.Orders
                .AnyAsync(order => order.MemberUserId == user.Id, cancellationToken);
            if (hasOrder)
            {
                continue;
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var profile = await dbContext.MemberProfiles
                .SingleOrDefaultAsync(candidate => candidate.UserId == user.Id, cancellationToken);
            profile?.Anonymize(nowUtc);

            user.Anonymize(nowUtc);

            // UserManager.UpdateAsync saves through the same DbContext, so it flushes the profile
            // mutation above together with the user mutation inside the same transaction.
            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            await transaction.CommitAsync(cancellationToken);
            anonymizedCount++;
        }

        return anonymizedCount;
    }
}
