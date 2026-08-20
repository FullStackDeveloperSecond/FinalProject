namespace DoSelect.Application.Storage;

/// <summary>
/// Read-only port onto private, non-public file storage rooted outside wwwroot. Infrastructure
/// resolves StorageKey beneath its own fixed private root and rejects anything that would
/// escape it; this port never exposes the resolved physical path to callers.
/// </summary>
public interface IPrivateFileStorage
{
    /// <summary>
    /// Opens the file addressed by <paramref name="storageKey"/> for reading, or returns null
    /// when the key is unsafe (blank, rooted, traversal) or the file does not exist/cannot be
    /// opened. Never throws for a missing or unsafe key.
    /// </summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
}
