using System.Data;
using DoSelect.Application.Common;
using DoSelect.Application.Support;
using DoSelect.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support;

public sealed class SupportAttachmentUploadStore : ISupportAttachmentUploadStore
{
    private const string TicketNotFoundMessage = "The support ticket was not found.";

    private readonly DoSelectDbContext _dbContext;

    public SupportAttachmentUploadStore(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SupportAttachmentUploadPreflight?> LoadPreflightAsync(
        Guid ticketPublicId,
        string memberUserId,
        CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.SupportTickets
            .Where(t => t.PublicId == ticketPublicId && t.MemberUserId == memberUserId)
            .Select(t => new { t.Id, t.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (ticket is null)
        {
            return null;
        }

        var activeCount = await _dbContext.SupportAttachments
            .CountAsync(a => a.SupportTicketId == ticket.Id && a.DeletedAtUtc == null, cancellationToken);

        return new SupportAttachmentUploadPreflight(ticket.Id, ticket.Status, activeCount);
    }

    public async Task InsertCleanAttachmentAsync(
        SupportAttachment attachment,
        string memberUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            // Re-validate ownership, status and re-count active attachments under serializable
            // isolation, immediately before the write. Re-checking ownership here (not just at
            // the earlier Application-layer preflight) closes the window where the ticket could
            // have been reassigned/transferred between preflight and this transaction; a missing
            // or changed owner is indistinguishable from a ticket that never existed.
            var status = await _dbContext.SupportTickets
                .Where(t => t.Id == attachment.SupportTicketId && t.MemberUserId == memberUserId)
                .Select(t => (SupportTicketStatus?)t.Status)
                .SingleOrDefaultAsync(cancellationToken);

            if (status is null)
            {
                throw DomainProblemException.NotFound(TicketNotFoundMessage);
            }

            if (status is SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
            {
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.SupportTicketStateConflict,
                    $"An attachment cannot be added while the ticket is {status}.");
            }

            var activeCount = await _dbContext.SupportAttachments
                .CountAsync(
                    a => a.SupportTicketId == attachment.SupportTicketId && a.DeletedAtUtc == null,
                    cancellationToken);

            if (activeCount >= SupportAttachmentUploadLimits.MaxActiveAttachmentsPerTicket)
            {
                throw DomainProblemException.BadRequest(
                    DomainErrorCodes.FileCountExceeded,
                    "A support ticket may have at most three active attachments.");
            }

            await _dbContext.SupportAttachments.AddAsync(attachment, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
