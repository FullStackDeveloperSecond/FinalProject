using DoSelect.Application.Files;

namespace DoSelect.Infrastructure.Files;

public sealed class LocalPrivateFileStorage : IPrivateFileStorage
{
    private const string PrivateDirectoryName = "private-files";
    private const string QuarantineDirectoryName = "quarantine";
    private readonly string _privateRoot;
    private readonly string _quarantineRoot;
    private readonly IFileScanner _scanner;

    public LocalPrivateFileStorage(string dataRoot, IFileScanner scanner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(scanner);

        var normalizedDataRoot = Path.GetFullPath(dataRoot);
        _privateRoot = Path.Combine(normalizedDataRoot, PrivateDirectoryName);
        _quarantineRoot = Path.Combine(normalizedDataRoot, QuarantineDirectoryName);
        _scanner = scanner;
    }

    public async Task<PrivateFileStoreResult> StoreAsync(
        PrivateFileUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(upload.Content);

        if (!upload.Content.CanRead)
        {
            throw new ArgumentException("The upload content stream must be readable.", nameof(upload));
        }

        Directory.CreateDirectory(_quarantineRoot);
        var quarantinePath = Path.Combine(_quarantineRoot, $"{Guid.NewGuid():N}.upload");

        try
        {
            var safeDisplayName = FileStorageUtilities.GetSafeDisplayName(upload.OriginalFileName);
            if (safeDisplayName is null)
            {
                return new PrivateFileStoreResult(PrivateFileStoreStatus.FormatInvalid);
            }

            var writeResult = await FileStorageUtilities.WriteLimitedFileAsync(
                upload.Content,
                quarantinePath,
                PrivateFileConstraints.MaximumFileSizeBytes,
                cancellationToken);
            if (writeResult.SizeExceeded)
            {
                return new PrivateFileStoreResult(PrivateFileStoreStatus.SizeExceeded);
            }

            var validatedFormat = await PrivateFileFormatValidator.ValidateAsync(
                quarantinePath,
                safeDisplayName,
                upload.ContentType,
                cancellationToken);
            if (validatedFormat is null)
            {
                return new PrivateFileStoreResult(PrivateFileStoreStatus.FormatInvalid);
            }

            var scanResult = await _scanner.ScanAsync(quarantinePath, cancellationToken);
            if (scanResult.Outcome == FileScanOutcome.MalwareDetected)
            {
                return new PrivateFileStoreResult(PrivateFileStoreStatus.MalwareDetected);
            }

            if (scanResult.Outcome != FileScanOutcome.Clean)
            {
                return new PrivateFileStoreResult(PrivateFileStoreStatus.ScanUnavailable);
            }

            var fileId = Guid.NewGuid().ToString("N");
            var storageKey = $"{PrivateDirectoryName}/{fileId[..2]}/{fileId}.blob";
            var permanentPath = ResolveStoragePath(storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(permanentPath)!);
            File.Move(quarantinePath, permanentPath);

            var storedFile = new StoredPrivateFile(
                storageKey,
                safeDisplayName,
                validatedFormat.Extension,
                validatedFormat.ContentType,
                writeResult.BytesWritten,
                writeResult.Sha256);
            return new PrivateFileStoreResult(PrivateFileStoreStatus.Stored, storedFile);
        }
        finally
        {
            FileStorageUtilities.TryDeleteFile(quarantinePath);
        }
    }

    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveStoragePath(storageKey, out var filePath) || !File.Exists(filePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            Stream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveStoragePath(storageKey, out var filePath) || !File.Exists(filePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(filePath);
        return Task.FromResult(true);
    }

    private string ResolveStoragePath(string storageKey)
    {
        if (!TryResolveStoragePath(storageKey, out var path))
        {
            throw new InvalidOperationException("The generated storage key is invalid.");
        }

        return path;
    }

    private bool TryResolveStoragePath(string storageKey, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathFullyQualified(storageKey))
        {
            return false;
        }

        try
        {
            if (!TryParseStorageKey(storageKey, out var normalizedKey))
            {
                return false;
            }

            var candidate = Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(_privateRoot)!,
                    normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = Path.TrimEndingDirectorySeparator(_privateRoot) +
                             Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryParseStorageKey(string storageKey, out string normalizedKey)
    {
        normalizedKey = string.Empty;
        if (storageKey.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        var segments = storageKey.Split('/', StringSplitOptions.None);
        if (segments.Length != 3 ||
            !string.Equals(segments[0], PrivateDirectoryName, StringComparison.Ordinal) ||
            segments[1].Length != 2 ||
            !segments[2].EndsWith(".blob", StringComparison.Ordinal))
        {
            return false;
        }

        var fileId = segments[2][..^".blob".Length];
        if (!Guid.TryParseExact(fileId, "N", out _) ||
            !fileId.StartsWith(segments[1], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedKey = string.Join('/', segments);
        return true;
    }

}
