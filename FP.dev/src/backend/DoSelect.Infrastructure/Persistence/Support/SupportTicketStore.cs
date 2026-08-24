using DoSelect.Application.Common;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support;

public sealed class SupportTicketStore : ISupportTicketStore
{
    // SQL Server error numbers for a unique index/constraint violation. The index name itself
    // is also checked so a violation of some *other* unique constraint on the same statement
    // batch is never mistaken for a TicketNumber collision.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const string TicketNumberUniqueIndexName = "UX_SupportTickets_TicketNumber";

    private readonly DoSelectDbContext _dbContext;

    public SupportTicketStore(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> TicketNumberExistsAsync(string ticketNumber, CancellationToken cancellationToken) =>
        _dbContext.SupportTickets.AnyAsync(t => t.TicketNumber == ticketNumber, cancellationToken);

    public async Task<SupportTicketCreationResult> CreateTicketWithArtifactsAsync(
        SupportTicket ticket,
        Func<long, SupportMessage> firstMessageFactory,
        Func<long, SupportStatusHistory> statusHistoryFactory,
        Func<long, SupportSlaEvent> slaEventFactory,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SupportTickets.AddAsync(ticket, cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken); // assigns ticket.Id for dependent rows
            }
            catch (DbUpdateException ex) when (IsTicketNumberCollision(ex))
            {
                // The failed insert leaves `ticket` tracked as Added with no assigned Id.
                // Detach it so a caller retrying with a fresh SupportTicket instance (and a
                // fresh candidate number) on this same DbContext does not accumulate dead
                // tracked entities across attempts.
                _dbContext.Entry(ticket).State = EntityState.Detached;
                throw new SupportTicketNumberCollisionException(ticket.TicketNumber, ex);
            }

            var firstMessage = firstMessageFactory(ticket.Id);
            var statusHistory = statusHistoryFactory(ticket.Id);
            var slaEvent = slaEventFactory(ticket.Id);

            await _dbContext.SupportMessages.AddAsync(firstMessage, cancellationToken);
            await _dbContext.SupportStatusHistories.AddAsync(statusHistory, cancellationToken);
            await _dbContext.SupportSlaEvents.AddAsync(slaEvent, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new SupportTicketCreationResult(ticket.Id, firstMessage, statusHistory, slaEvent);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsTicketNumberCollision(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sqlException &&
        sqlException.Message.Contains(TicketNumberUniqueIndexName, StringComparison.Ordinal);

    public Task<SupportTicket?> FindOwnedTicketAsync(
        Guid ticketPublicId,
        string memberUserId,
        CancellationToken cancellationToken) =>
        _dbContext.SupportTickets
            .SingleOrDefaultAsync(
                t => t.PublicId == ticketPublicId && t.MemberUserId == memberUserId,
                cancellationToken);

    public async Task<(IReadOnlyList<SupportTicket> Items, int TotalCount)> ListOwnedAsync(
        string memberUserId,
        SupportTicketQuery query,
        CancellationToken cancellationToken)
    {
        var filtered = _dbContext.SupportTickets
            .Where(t => t.MemberUserId == memberUserId);

        if (query.Statuses is { Count: > 0 } statuses)
        {
            filtered = filtered.Where(t => statuses.Contains(t.Status));
        }

        if (query.Category is { } category)
        {
            filtered = filtered.Where(t => t.Category == category);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(t => t.LastActivityAtUtc)
            .ThenByDescending(t => t.PublicId)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<SupportMessage>> ListPublicMessagesAsync(
        long ticketId,
        CancellationToken cancellationToken) =>
        await _dbContext.SupportMessages
            .Where(m => m.SupportTicketId == ticketId && !m.IsInternal)
            .OrderBy(m => m.SentAtUtc)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupportAttachmentDto>> ListCleanAttachmentsAsync(
        long ticketId,
        CancellationToken cancellationToken) =>
        await _dbContext.SupportAttachments
            .AsNoTracking()
            .Where(a => a.SupportTicketId == ticketId &&
                a.DeletedAtUtc == null &&
                a.ScanStatus == PrivateAttachmentScanStatus.Clean)
            .OrderBy(a => a.CreatedAtUtc)
            .ThenBy(a => a.PublicId)
            .Select(a => new SupportAttachmentDto(
                a.PublicId,
                a.OriginalFileName,
                a.MimeType,
                a.FileSizeBytes,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<Guid?> FindOrderPublicIdAsync(long orderId, CancellationToken cancellationToken) =>
        await _dbContext.Orders
            .Where(o => o.Id == orderId)
            .Select(o => (Guid?)o.PublicId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task SaveTicketChangesAsync(
        SupportTicket ticket,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        _dbContext.Entry(ticket).Property(t => t.RowVersion).OriginalValue = expectedRowVersion;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The ticket was modified by another request. Reload and try again.");
        }
    }

    public async Task AddMessageAsync(SupportMessage message, CancellationToken cancellationToken) =>
        await _dbContext.SupportMessages.AddAsync(message, cancellationToken);

    public async Task AddStatusHistoryAsync(SupportStatusHistory history, CancellationToken cancellationToken) =>
        await _dbContext.SupportStatusHistories.AddAsync(history, cancellationToken);
}
