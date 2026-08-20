using DoSelect.Application.Support.Admin;
using DoSelect.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support.Admin;

public sealed class AdminSupportTicketStore : IAdminSupportTicketStore
{
    private readonly DoSelectDbContext _dbContext;

    public AdminSupportTicketStore(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SupportTicketClaimResult> ClaimAsync(
        Guid ticketPublicId,
        string adminUserId,
        byte[] expectedRowVersion,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        // The claimant must resolve to an active AdminProfile before the ticket is touched;
        // a missing/inactive profile must not update the ticket or append assignment history.
        var adminProfile = await _dbContext.AdminProfiles
            .AsNoTracking()
            .Where(p => p.UserId == adminUserId)
            .Select(p => new { p.PublicId, p.DisplayName, p.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (adminProfile is null || !adminProfile.IsActive)
        {
            return SupportTicketClaimResult.AdminNotEligible;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // A tracked SaveChangesAsync cannot tell "already claimed by someone else" apart
            // from "some other field went stale" after the fact. This conditional UPDATE
            // encodes the full claimability rule (unassigned + Open + matching RowVersion) in
            // the WHERE clause so its affected-row count is the single source of truth.
            var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE SupportTickets
                 SET AssigneeAdminUserId = {adminUserId},
                     Status = {nameof(SupportTicketStatus.Assigned)},
                     UpdatedAtUtc = {occurredAtUtc},
                     LastActivityAtUtc = {occurredAtUtc}
                 WHERE PublicId = {ticketPublicId}
                   AND AssigneeAdminUserId IS NULL
                   AND Status = {nameof(SupportTicketStatus.Open)}
                   AND RowVersion = {expectedRowVersion}
                 """,
                cancellationToken);

            if (affected == 0)
            {
                var current = await _dbContext.SupportTickets
                    .AsNoTracking()
                    .Where(t => t.PublicId == ticketPublicId)
                    .Select(t => new { t.AssigneeAdminUserId, t.Status })
                    .SingleOrDefaultAsync(cancellationToken);

                await transaction.RollbackAsync(cancellationToken);

                if (current is null)
                {
                    return SupportTicketClaimResult.NotFound;
                }

                // Still unassigned and Open here means the row itself matched the business
                // rule but the caller's RowVersion did not — some other field went stale, not
                // the claimability of the ticket.
                var stillClaimable = current.AssigneeAdminUserId is null
                    && current.Status == SupportTicketStatus.Open;
                return stillClaimable
                    ? SupportTicketClaimResult.ConcurrencyConflict
                    : SupportTicketClaimResult.AssignmentConflict;
            }

            var row = await (
                from t in _dbContext.SupportTickets.AsNoTracking()
                where t.PublicId == ticketPublicId
                select new
                {
                    t.Id,
                    t.PublicId,
                    t.TicketNumber,
                    t.Category,
                    t.Subject,
                    t.Status,
                    t.Priority,
                    t.OrderId,
                    t.CreatedAtUtc,
                    t.LastActivityAtUtc,
                    t.FirstResponseDueAtUtc,
                    t.ResolutionDueAtUtc,
                    t.FirstHumanResponseAtUtc,
                    t.ResolvedAtUtc,
                    t.ClosedAtUtc,
                    t.ReopenCount,
                    t.RowVersion,
                }).SingleAsync(cancellationToken);

            Guid? orderPublicId = row.OrderId is null
                ? null
                : await _dbContext.Orders
                    .AsNoTracking()
                    .Where(o => o.Id == row.OrderId)
                    .Select(o => (Guid?)o.PublicId)
                    .SingleOrDefaultAsync(cancellationToken);

            await _dbContext.SupportAssignmentHistories.AddAsync(
                new SupportAssignmentHistory(
                    row.Id,
                    fromAdminUserId: null,
                    toAdminUserId: adminUserId,
                    AssignmentAction.Claim,
                    reason: null,
                    actorUserId: adminUserId,
                    occurredAtUtc),
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var claimed = new ClaimedSupportTicket(
                row.PublicId,
                row.TicketNumber,
                row.Category,
                row.Subject,
                row.Status,
                row.Priority,
                orderPublicId,
                adminProfile.PublicId,
                adminProfile.DisplayName,
                row.CreatedAtUtc,
                row.LastActivityAtUtc,
                row.FirstResponseDueAtUtc,
                row.ResolutionDueAtUtc,
                row.FirstHumanResponseAtUtc,
                row.ResolvedAtUtc,
                row.ClosedAtUtc,
                row.ReopenCount,
                row.RowVersion);
            return SupportTicketClaimResult.Claimed(claimed);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AdminSupportTicketDetail?> GetDetailAsync(
        Guid ticketPublicId,
        CancellationToken cancellationToken)
    {
        // A single query with left joins onto Orders and active AdminProfiles keeps the ticket
        // shell + assignee + order lookup at one round trip; messages are a second, separately
        // bounded query. Neither scales with the number of messages or historical assignees, so
        // this is a constant query count rather than N+1.
        var row = await (
            from t in _dbContext.SupportTickets.AsNoTracking()
            where t.PublicId == ticketPublicId
            join o in _dbContext.Orders.AsNoTracking() on t.OrderId equals (long?)o.Id into orderGroup
            from o in orderGroup.DefaultIfEmpty()
            join a in _dbContext.AdminProfiles.AsNoTracking().Where(p => p.IsActive)
                on t.AssigneeAdminUserId equals a.UserId into assigneeGroup
            from a in assigneeGroup.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.PublicId,
                t.TicketNumber,
                t.Category,
                t.Subject,
                t.Status,
                t.Priority,
                OrderPublicId = o == null ? (Guid?)null : o.PublicId,
                AssigneePublicId = a == null ? (Guid?)null : a.PublicId,
                AssigneeDisplayName = a == null ? null : a.DisplayName,
                t.CreatedAtUtc,
                t.LastActivityAtUtc,
                t.FirstResponseDueAtUtc,
                t.ResolutionDueAtUtc,
                t.FirstHumanResponseAtUtc,
                t.ResolvedAtUtc,
                t.ClosedAtUtc,
                t.ReopenCount,
                t.RowVersion,
            }).SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // Deterministic order for admin readers: chronological, with PublicId as the stable
        // unique tie-break for messages sharing the same SentAtUtc.
        var messages = await _dbContext.SupportMessages
            .AsNoTracking()
            .Where(m => m.SupportTicketId == row.Id)
            .OrderBy(m => m.SentAtUtc)
            .ThenBy(m => m.PublicId)
            .Select(m => new AdminSupportMessageProjection(
                m.PublicId,
                m.SenderType,
                m.AiGenerated,
                m.IsInternal,
                m.Body,
                m.Language,
                m.SentAtUtc))
            .ToListAsync(cancellationToken);

        return new AdminSupportTicketDetail(
            row.PublicId,
            row.TicketNumber,
            row.Category,
            row.Subject,
            row.Status,
            row.Priority,
            row.OrderPublicId,
            row.AssigneePublicId,
            row.AssigneeDisplayName,
            row.CreatedAtUtc,
            row.LastActivityAtUtc,
            row.FirstResponseDueAtUtc,
            row.ResolutionDueAtUtc,
            row.FirstHumanResponseAtUtc,
            row.ResolvedAtUtc,
            row.ClosedAtUtc,
            row.ReopenCount,
            row.RowVersion,
            messages);
    }
}
