namespace DoSelect.Application.Members;

/// <summary>
/// Anonymizes member accounts that never completed email verification within the retention
/// window (M-01: 未驗證帳號滿 7 天且無必要關聯後清理). A PendingEmailVerification account can
/// never sign in (MemberLoginGateway rejects it with EmailUnverified), so it cannot have placed
/// an order, added to a cart, left a review, or accumulated any other business data — the account
/// status itself is the "no necessary associations" guarantee, no further cross-table check is
/// needed.
/// </summary>
public sealed class PurgeStaleUnverifiedMembersService(
    IMemberCleanupGateway gateway,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan UnverifiedRetentionPeriod = TimeSpan.FromDays(7);

    public Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime - UnverifiedRetentionPeriod;
        return gateway.AnonymizeStaleUnverifiedMembersAsync(cutoffUtc, cancellationToken);
    }
}
