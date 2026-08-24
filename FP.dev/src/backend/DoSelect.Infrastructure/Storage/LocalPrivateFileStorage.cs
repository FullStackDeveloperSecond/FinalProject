using DoSelect.Application.Storage;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Storage;

/// <summary>
/// Filesystem-backed IPrivateFileStorage rooted at a fixed "private/support" subdirectory of
/// Storage:DataRoot (validated absolute and non-root at host startup by the Api layer). Every
/// StorageKey is resolved beneath that root and rejected if it cannot be proven to stay inside
/// it — nothing above the root is ever readable through this port, regardless of what the
/// caller supplies as a key.
/// </summary>
public sealed class LocalPrivateFileStorage : IPrivateFileStorage
{
    private readonly string _rootPath;

    public LocalPrivateFileStorage(IOptions<StorageOptions> options)
    {
        _rootPath = Path.GetFullPath(Path.Combine(options.Value.DataRoot, "private", "support"));
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (!TryResolvePath(storageKey, out var fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            Stream stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Missing, locked, or otherwise inaccessible — never reveal which, to the caller.
            return Task.FromResult<Stream?>(null);
        }
    }

    private bool TryResolvePath(string storageKey, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains('\0'))
        {
            return false;
        }

        // Reject absolute keys (drive-rooted, UNC, or leading separator) and alternate-drive
        // syntax up front, before any path combination happens.
        if (Path.IsPathRooted(storageKey) || storageKey.Contains(':'))
        {
            return false;
        }

        // Reject "." / ".." segments and empty segments (doubled or alternate separators)
        // explicitly, rather than relying solely on GetFullPath's normalization.
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
            candidate = Path.GetFullPath(Path.Combine(_rootPath, storageKey));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, comparison))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}
