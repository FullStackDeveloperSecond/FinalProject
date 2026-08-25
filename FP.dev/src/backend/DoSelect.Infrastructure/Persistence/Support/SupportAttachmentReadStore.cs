using DoSelect.Application.Support;
using DoSelect.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support;

public sealed class SupportAttachmentReadStore : ISupportAttachmentReadStore
{
    private readonly DoSelectDbContext _dbContext;

    public SupportAttachmentReadStore(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SupportAttachmentReadRecord?> FindReadableAsync(
        Guid attachmentPublicId,
        SupportAttachmentActor actor,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.SupportAttachments
            .Where(a =>
                a.PublicId == attachmentPublicId &&
                a.DeletedAtUtc == null &&
                a.ScanStatus == PrivateAttachmentScanStatus.Clean)
            .Join(
                _dbContext.SupportTickets,
                attachment => attachment.SupportTicketId,
                ticket => ticket.Id,
                (attachment, ticket) => new { attachment, ticket });

        // A member may only read an attachment on a ticket that member owns; a support handler
        // (CustomerService/CustomerServiceSupervisor with MFA, already established by the Api
        // layer) is limited to the shared queue/current assignment unless supervisor scope applies.
        if (actor.Type == SupportAttachmentActorType.Member)
        {
            query = query.Where(x => x.ticket.MemberUserId == actor.UserId);
        }
        else if (!actor.CanSupervise)
        {
            query = query.Where(x =>
                x.ticket.AssigneeAdminUserId == null ||
                x.ticket.AssigneeAdminUserId == actor.UserId);
        }

        return await query
            .Select(x => new SupportAttachmentReadRecord(
                x.attachment.StorageKey,
                x.attachment.OriginalFileName,
                x.attachment.MimeType))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
