using DoSelect.Domain.Support;

namespace DoSelect.Application.Support;

/// <summary>
/// Persistence port for accepting a new support-ticket attachment. Split into a cheap preflight
/// lookup (used to reject before any file content is read) and a transactional insert that
/// re-validates ownership/status/count under serializable isolation immediately before writing,
/// so a concurrent upload cannot push the active-attachment count past the limit.
/// </summary>
public interface ISupportAttachmentUploadStore
{
    /// <summary>
    /// Returns null when no ticket with <paramref name="ticketPublicId"/> is owned by
    /// <paramref name="memberUserId"/> — the same signal used for "does not exist" and
    /// "belongs to another member" so the caller cannot distinguish them.
    /// </summary>
    Task<SupportAttachmentUploadPreflight?> LoadPreflightAsync(
        Guid ticketPublicId,
        string memberUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts <paramref name="attachment"/> inside a serializable transaction that re-checks the
    /// owning ticket's current owner (<paramref name="memberUserId"/>), status and
    /// active-attachment count immediately before the write. Throws DomainProblemException
    /// (NotFound/Conflict/file_count_exceeded) if the recheck no longer allows the insert — a
    /// changed or missing owner surfaces the same NotFound as a ticket that never existed. The
    /// caller is responsible for deleting the already-committed physical file when this throws.
    /// </summary>
    Task InsertCleanAttachmentAsync(
        SupportAttachment attachment,
        string memberUserId,
        CancellationToken cancellationToken);
}

public sealed record SupportAttachmentUploadPreflight(
    long TicketId,
    SupportTicketStatus Status,
    int ActiveAttachmentCount);
