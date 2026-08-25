namespace DoSelect.Application.Members;

public interface IMemberCleanupGateway
{
    /// <summary>
    /// Anonymizes every member account that is still <see cref="Domain.Members.AccountStatus.PendingEmailVerification"/>
    /// with a <c>CreatedAtUtc</c> older than <paramref name="olderThanUtc"/>. Returns the number
    /// of accounts anonymized.
    /// </summary>
    Task<int> AnonymizeStaleUnverifiedMembersAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default);
}
