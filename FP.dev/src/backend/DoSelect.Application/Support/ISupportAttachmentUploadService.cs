using DoSelect.Application.Support.Dtos;

namespace DoSelect.Application.Support;

/// <summary>
/// Uploads a single private attachment to a support ticket owned by the calling member. Every
/// call performs its own ownership/status/count authorization before any file content is read
/// (Actor Scope) — callers never pre-check authorization separately.
/// </summary>
public interface ISupportAttachmentUploadService
{
    Task<SupportAttachmentDto> UploadAsync(
        string memberUserId,
        Guid ticketPublicId,
        IncomingAttachmentFile file,
        CancellationToken cancellationToken);
}

/// <summary>
/// The multipart file part as seen by the Api layer, decoupled from ASP.NET Core's IFormFile so
/// Application stays framework-agnostic. <see cref="OpenRead"/> is a lazy delegate rather than
/// an already-open stream: Application only ever invokes it after ownership, ticket-state and
/// attachment-count checks have all passed, so a rejected request never triggers a file read.
/// <see cref="HasFile"/> distinguishes "the multipart <c>file</c> part was absent" from "a file
/// part was present but has an empty/unrecognized name" — the former is a validation failure,
/// not a format failure.
/// </summary>
public sealed record IncomingAttachmentFile(
    string OriginalFileName,
    string ClaimedContentType,
    long? DeclaredLength,
    bool HasFile,
    Func<Stream> OpenRead);

public static class SupportAttachmentUploadLimits
{
    public const long MaxFileSizeBytes = 10_485_760;
    public const int MaxActiveAttachmentsPerTicket = 3;
}
