namespace DoSelect.Infrastructure.Files;

internal static class ProductImageFormatValidator
{
    private const int SignatureBufferSize = 12;

    private static readonly IReadOnlyDictionary<string, ImageFormatDefinition> Formats =
        new Dictionary<string, ImageFormatDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new(".jpg", "image/jpeg", ImageFormatKind.Jpeg),
            [".jpeg"] = new(".jpeg", "image/jpeg", ImageFormatKind.Jpeg),
            [".png"] = new(".png", "image/png", ImageFormatKind.Png),
            [".webp"] = new(".webp", "image/webp", ImageFormatKind.Webp),
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
            SignatureBufferSize,
            throwOnEndOfStream: false,
            cancellationToken);

        return IsSignatureValid(format.Kind, signature.AsSpan(0, bytesRead))
            ? new ValidatedFileFormat(format.Extension, format.ContentType)
            : null;
    }

    private static bool IsSignatureValid(
        ImageFormatKind kind,
        ReadOnlySpan<byte> signature)
    {
        return kind switch
        {
            ImageFormatKind.Jpeg => IsJpeg(signature),
            ImageFormatKind.Png => IsPng(signature),
            ImageFormatKind.Webp => IsWebp(signature),
            _ => false,
        };
    }

    private static bool IsJpeg(ReadOnlySpan<byte> signature) =>
        signature.Length >= 3 &&
        signature[0] == 0xFF &&
        signature[1] == 0xD8 &&
        signature[2] == 0xFF;

    private static bool IsPng(ReadOnlySpan<byte> signature) =>
        signature.Length >= 8 &&
        signature[..8].SequenceEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static bool IsWebp(ReadOnlySpan<byte> signature) =>
        signature.Length >= 12 &&
        signature[..4].SequenceEqual("RIFF"u8) &&
        signature[8..12].SequenceEqual("WEBP"u8);

    private sealed record ImageFormatDefinition(
        string Extension,
        string ContentType,
        ImageFormatKind Kind);

    private enum ImageFormatKind
    {
        Jpeg,
        Png,
        Webp,
    }
}
