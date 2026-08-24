using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberCleanupGateway(
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
            user.Anonymize(nowUtc);
            var result = await userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                anonymizedCount++;
            }
        }

        return anonymizedCount;
    }
}
