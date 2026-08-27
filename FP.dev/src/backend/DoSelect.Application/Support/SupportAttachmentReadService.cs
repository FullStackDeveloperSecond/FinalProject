using DoSelect.Application.Common;
using DoSelect.Application.Storage;

namespace DoSelect.Application.Support;

public sealed class SupportAttachmentReadService : ISupportAttachmentReadService
{
    // A single fixed message for every rejection branch (missing attachment, wrong member,
    // disallowed admin role/MFA, soft-deleted, non-Clean scan, missing/unsafe physical file) so
    // the HTTP response is byte-for-byte identical no matter which branch fired. Never reveal
    // which branch matched.
    private const string NotFoundMessage = "The attachment was not found.";

    private readonly ISupportAttachmentReadStore _store;
    private readonly IPrivateFileStorage _storage;

    public SupportAttachmentReadService(ISupportAttachmentReadStore store, IPrivateFileStorage storage)
    {
        _store = store;
        _storage = storage;
    }

    public async Task<PrivateAttachmentContent> GetContentAsync(
        SupportAttachmentActor actor,
        Guid attachmentPublicId,
        CancellationToken cancellationToken)
    {
        var record = await _store.FindReadableAsync(attachmentPublicId, actor, cancellationToken);
        if (record is null)
        {
            throw DomainProblemException.NotFound(NotFoundMessage);
        }

        // Authorization is fully settled by the store lookup above; the physical file is only
        // ever opened afterward, for a request already known to be allowed.
        var stream = await _storage.OpenReadAsync(record.StorageKey, cancellationToken);
        if (stream is null)
        {
            throw DomainProblemException.NotFound(NotFoundMessage);
        }

        return new PrivateAttachmentContent(stream, record.MimeType, record.OriginalFileName);
    }
}
