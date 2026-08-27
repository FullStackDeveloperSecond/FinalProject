namespace DoSelect.Application.Support;

/// <summary>
/// Streams a single, already-authorized support attachment. Every method call performs the
/// full ownership/role check itself before touching storage — callers never pre-check
/// authorization separately, so there is exactly one place that can get it wrong.
/// </summary>
public interface ISupportAttachmentReadService
{
    Task<PrivateAttachmentContent> GetContentAsync(
        SupportAttachmentActor actor,
        Guid attachmentPublicId,
        CancellationToken cancellationToken);
}

public enum SupportAttachmentActorType
{
    Member,
    SupportHandler,
}

/// <summary>
/// Caller identity resolved once at the Api boundary from whichever cookie scheme actually
/// authenticated the request (member, or an admin who satisfies SupportTicket.Handle). Carrying
/// this instead of a ClaimsPrincipal keeps Application free of ASP.NET Core identity types and
/// removes any chance of Application re-deriving account type from ambiguous merged claims.
/// </summary>
public sealed record SupportAttachmentActor(
    SupportAttachmentActorType Type,
    string UserId,
    bool CanSupervise = false);

/// <summary>
/// The minimal, already-authorized payload needed to stream a private attachment back to the
/// caller. Deliberately excludes StorageKey, physical paths, uploader identity and the
/// SHA-256 hash — nothing beyond what the HTTP response itself needs ever leaves this type.
/// </summary>
public sealed record PrivateAttachmentContent(Stream Content, string ContentType, string DownloadFileName);
