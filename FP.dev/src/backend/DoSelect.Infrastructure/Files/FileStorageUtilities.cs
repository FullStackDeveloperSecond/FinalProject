using System.Security.Cryptography;

namespace DoSelect.Infrastructure.Files;

internal static class FileStorageUtilities
{
    public static string? GetSafeDisplayName(string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return null;
        }

        try
        {
            var displayName = Path.GetFileName(originalFileName.Trim());
            return displayName.Length is > 0 and <= 255 &&
                   !displayName.Any(char.IsControl)
                ? displayName
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static async Task<LimitedFileWriteResult> WriteLimitedFileAsync(
        Stream source,
        string destinationPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > maximumBytes)
            {
                return LimitedFileWriteResult.TooLarge;
            }

            hash.AppendData(buffer.AsSpan(0, bytesRead));
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        return new LimitedFileWriteResult(totalBytes, hash.GetHashAndReset(), false);
    }

    public static async Task<FileDigest> CalculateDigestAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new FileDigest(stream.Length, hash);
    }

    public static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
            // A daily maintenance job removes abandoned staging files after 24 hours.
        }
        catch (UnauthorizedAccessException)
        {
            // A daily maintenance job removes abandoned staging files after 24 hours.
        }
    }

    public static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // A daily maintenance job removes abandoned staging directories after 24 hours.
        }
        catch (UnauthorizedAccessException)
        {
            // A daily maintenance job removes abandoned staging directories after 24 hours.
        }
    }
}

internal sealed record LimitedFileWriteResult(
    long BytesWritten,
    byte[] Sha256,
    bool SizeExceeded)
{
    public static LimitedFileWriteResult TooLarge { get; } = new(0, [], true);
}

internal sealed record FileDigest(long FileSizeBytes, byte[] Sha256);
