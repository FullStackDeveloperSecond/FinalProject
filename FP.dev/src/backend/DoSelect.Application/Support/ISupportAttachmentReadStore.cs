namespace DoSelect.Application.Support;

/// <summary>
/// Persistence port for authorized support-attachment metadata lookups. Ownership/role
/// authorization is applied inside the query itself (not after loading), so a caller can never
/// accidentally use an unauthorized record.
/// </summary>
public interface ISupportAttachmentReadStore
{
    /// <summary>
    /// Returns the attachment identified by <paramref name="attachmentPublicId"/> only when it
    /// is non-deleted, has a Clean scan result, and <paramref name="actor"/> is authorized to
    /// read it (the owning member, or a support handler for any ticket). Returns null for every
    /// other case — missing, soft-deleted, not yet/never Clean, or belonging to another member —
    /// without distinguishing between them.
    /// </summary>
    Task<SupportAttachmentReadRecord?> FindReadableAsync(
        Guid attachmentPublicId,
        SupportAttachmentActor actor,
        CancellationToken cancellationToken);
}

/// <summary>
/// Only the fields the read flow actually needs to authorize storage access and build the HTTP
/// response. StorageKey stops at the Application service that opens the file — it is never
/// copied into PrivateAttachmentContent or any type that reaches the Api layer. Never carries
/// the uploader identity or the SHA-256 hash.
/// </summary>
public sealed record SupportAttachmentReadRecord(string StorageKey, string OriginalFileName, string MimeType);
