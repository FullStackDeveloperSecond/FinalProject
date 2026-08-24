using DoSelect.Application.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Storage;

/// <summary>
/// Filesystem-backed IPrivateAttachmentUploadStorage. Final files land under the same
/// "private/support" root that LocalPrivateFileStorage reads from; temp files are staged in an
/// isolated sibling directory ("private/support-uploads-tmp") that is never reachable through
/// any StorageKey a read ever issues, so a leftover temp file can never be served back to a
/// client. Both roots live under the same configured Storage:DataRoot volume so the final
/// commit can use a plain, atomic same-volume File.Move.
/// </summary>
public sealed class LocalPrivateAttachmentUploadStorage : IPrivateAttachmentUploadStorage
{
    private readonly string _finalRoot;
    private readonly string _tempRoot;
    private readonly ILogger<LocalPrivateAttachmentUploadStorage> _logger;

    public LocalPrivateAttachmentUploadStorage(
        IOptions<StorageOptions> options,
        ILogger<LocalPrivateAttachmentUploadStorage> logger)
    {
        _logger = logger;
        var dataRoot = options.Value.DataRoot;

        _finalRoot = Path.GetFullPath(Path.Combine(dataRoot, "private", "support"));
        _tempRoot = Path.GetFullPath(Path.Combine(dataRoot, "private", "support-uploads-tmp"));

        Directory.CreateDirectory(_finalRoot);
        Directory.CreateDirectory(_tempRoot);
    }

    public Task<AttachmentTempFile> CreateTempFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.tmp");
        Stream stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true);
        return Task.FromResult(new AttachmentTempFile(tempPath, stream));
    }

    public Task<string> CommitAsync(string tempPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(_finalRoot, storageKey);
        File.Move(tempPath, finalPath);
        return Task.FromResult(storageKey);
    }

    public Task<bool> DeleteFinalFileAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (!TryResolveFinalPath(storageKey, out var fullPath))
        {
            _logger.LogWarning(
                "Attachment compensation delete could not resolve storage key {StorageKey}.",
                storageKey);
            return Task.FromResult(false);
        }

        var deleted = TryDelete(fullPath, storageKey, "final");
        return Task.FromResult(deleted);
    }

    public void DeleteTempFile(string tempPath)
    {
        // tempPath always originates from CreateTempFileAsync above, so it is always rooted
        // under _tempRoot; no containment check is needed for a self-generated handle. The
        // GUID-named file itself carries no user data, so it is safe to include in the log.
        TryDelete(tempPath, Path.GetFileName(tempPath), "temp");
    }

    /// <returns><c>true</c> if the file was deleted or was already absent; <c>false</c> if a
    /// delete attempt was made and failed.</returns>
    private bool TryDelete(string fullPath, string logIdentifier, string context)
    {
        try
        {
            File.Delete(fullPath);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Never surface a physical path or user data in the log — only the opaque
            // storage-key/temp-file identifier and which root it belongs to.
            _logger.LogWarning(
                ex,
                "Failed to delete {Context} attachment upload artifact {Identifier}.",
                context,
                logIdentifier);
            return false;
        }
    }

    private bool TryResolveFinalPath(string storageKey, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains('\0'))
        {
            return false;
        }

        if (Path.IsPathRooted(storageKey) || storageKey.Contains(':'))
        {
            return false;
        }

        var segments = storageKey.Split(['/', '\\'], StringSplitOptions.None);
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(_finalRoot, storageKey));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = _finalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _finalRoot
            : _finalRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, comparison))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}
