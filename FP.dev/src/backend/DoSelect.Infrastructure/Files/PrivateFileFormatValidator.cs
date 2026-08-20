using DoSelect.Application.Files;

namespace DoSelect.Infrastructure.Files;

internal static class PrivateFileFormatValidator
{
    private const int SignatureBufferSize = 8;

    private static readonly IReadOnlyDictionary<string, FileFormatDefinition> Formats =
        new Dictionary<string, FileFormatDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new(".jpg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
            [".jpeg"] = new(".jpeg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
            [".png"] = new(".png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            [".pdf"] = new(".pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46, 0x2D]),
        };

    public static async Task<ValidatedFileFormat?> ValidateAsync(
        string filePath,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(originalFileName) ||
            string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var extension = Path.GetExtension(originalFileName);
        if (!Formats.TryGetValue(extension, out var format) ||
            !string.Equals(contentType.Trim(), format.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var signature = new byte[SignatureBufferSize];
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytesRead = await stream.ReadAtLeastAsync(
            signature,
            format.Signature.Length,
            throwOnEndOfStream: false,
            cancellationToken);

        return bytesRead >= format.Signature.Length &&
               signature.AsSpan(0, format.Signature.Length).SequenceEqual(format.Signature)
            ? new ValidatedFileFormat(format.Extension, format.ContentType)
            : null;
    }

    private sealed record FileFormatDefinition(
        string Extension,
        string ContentType,
        byte[] Signature);
}

internal sealed record ValidatedFileFormat(string Extension, string ContentType);
