namespace DoSelect.Application.Support;

/// <summary>
/// Raised when a support-attachment upload fails to write metadata after the physical file was
/// already committed, and the follow-up compensation delete of that orphaned file also fails (or
/// itself throws). Carries the compensation failure as <see cref="Exception.InnerException"/> so
/// it is never silently dropped, while the caller still surfaces the original metadata-write
/// failure (the client-facing status/code) unchanged.
/// </summary>
public sealed class SupportAttachmentCompensationException : Exception
{
    public SupportAttachmentCompensationException(string storageKey, Exception? cleanupFailure)
        : base(BuildMessage(storageKey, cleanupFailure), cleanupFailure)
    {
        StorageKey = storageKey;
    }

    /// <summary>
    /// The opaque, server-generated storage key of the orphaned final file. Never a physical
    /// path.
    /// </summary>
    public string StorageKey { get; }

    private static string BuildMessage(string storageKey, Exception? cleanupFailure) =>
        cleanupFailure is null
            ? $"Failed to compensate orphaned attachment storage key '{storageKey}' after a metadata write failure."
            : $"Failed to compensate orphaned attachment storage key '{storageKey}' after a metadata write failure: {cleanupFailure.Message}";
}
