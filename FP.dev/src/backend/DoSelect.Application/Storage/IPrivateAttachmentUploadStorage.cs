namespace DoSelect.Application.Storage;

/// <summary>
/// Write-side port onto private, non-public file storage used while accepting a new attachment
/// upload. Mirrors <see cref="IPrivateFileStorage"/>'s read-only counterpart: Infrastructure
/// resolves every physical path (both the isolated temp area and the final private root) and
/// this port never exposes a resolved path to Application beyond the opaque temp handle needed
/// to hand the staged file to <see cref="IFileScanner"/>.
/// </summary>
public interface IPrivateAttachmentUploadStorage
{
    /// <summary>
    /// Creates a uniquely named file in an isolated private temp area (never reachable through
    /// <see cref="IPrivateFileStorage"/>) and returns a writable stream over it together with
    /// the opaque path handle to pass to <see cref="IFileScanner.ScanAsync"/> and back into
    /// <see cref="CommitAsync"/>/<see cref="DeleteTempFile"/>.
    /// </summary>
    Task<AttachmentTempFile> CreateTempFileAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically moves the temp file into the final private support root under a fresh,
    /// server-generated opaque key that carries no trace of the original filename, and returns
    /// that relative StorageKey. Only ever called after a Clean scan result.
    /// </summary>
    Task<string> CommitAsync(string tempPath, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the committed final file addressed by <paramref name="storageKey"/>. Used to
    /// compensate for a database insert failure that happens after the physical file was
    /// already committed, so no orphaned file is ever left behind. Never throws — every failure
    /// (unresolvable key, locked/inaccessible file, etc.) is captured, logged server-side, and
    /// reported back as a deterministic <c>false</c> result so the caller can decide how to
    /// surface a failed compensation rather than the outcome being silently swallowed.
    /// </summary>
    /// <returns><c>true</c> if the file was deleted or was already absent; <c>false</c> if a
    /// deletion attempt was made and failed, or the key could not be resolved.</returns>
    Task<bool> DeleteFinalFileAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort cleanup of a temp file that was never committed (rejected upload, failed
    /// validation/scan, or the temp file was already moved away by <see cref="CommitAsync"/>,
    /// in which case this is a safe no-op). Never throws.
    /// </summary>
    void DeleteTempFile(string tempPath);
}

/// <summary>
/// A freshly created, writable private temp file. <see cref="TempPath"/> is an opaque handle —
/// Application never interprets it beyond passing it to the scanner and back into this port.
/// </summary>
public sealed record AttachmentTempFile(string TempPath, Stream WriteStream);
