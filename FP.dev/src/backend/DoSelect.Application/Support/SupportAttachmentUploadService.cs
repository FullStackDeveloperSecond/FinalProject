using System.Security.Cryptography;
using DoSelect.Application.Common;
using DoSelect.Application.Storage;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support;

public sealed class SupportAttachmentUploadService : ISupportAttachmentUploadService
{
    private const string TicketNotFoundMessage = "The support ticket was not found.";
    private const int CopyBufferSize = 81_920;
    private const int SignaturePrefixLength = 16;

    private static readonly IReadOnlyDictionary<string, AttachmentFormat> FormatsByExtension =
        new Dictionary<string, AttachmentFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = new("image/png", MatchesPngSignature),
            [".jpg"] = new("image/jpeg", MatchesJpegSignature),
            [".jpeg"] = new("image/jpeg", MatchesJpegSignature),
            [".pdf"] = new("application/pdf", MatchesPdfSignature),
        };

    private readonly ISupportAttachmentUploadStore _store;
    private readonly IPrivateAttachmentUploadStorage _storage;
    private readonly IFileScanner _scanner;
    private readonly TimeProvider _timeProvider;

    public SupportAttachmentUploadService(
        ISupportAttachmentUploadStore store,
        IPrivateAttachmentUploadStorage storage,
        IFileScanner scanner,
        TimeProvider timeProvider)
    {
        _store = store;
        _storage = storage;
        _scanner = scanner;
        _timeProvider = timeProvider;
    }

    public async Task<SupportAttachmentDto> UploadAsync(
        string memberUserId,
        Guid ticketPublicId,
        IncomingAttachmentFile file,
        CancellationToken cancellationToken)
    {
        // Ownership, ticket-state and count are all checked before a single byte of the
        // incoming file is read (AC1/AC6) — file.OpenRead() is not invoked anywhere above this
        // point.
        var preflight = await _store.LoadPreflightAsync(ticketPublicId, memberUserId, cancellationToken);
        if (preflight is null)
        {
            // Same 404 whether the ticket does not exist or belongs to someone else.
            throw DomainProblemException.NotFound(TicketNotFoundMessage);
        }

        if (preflight.Status is SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.SupportTicketStateConflict,
                $"An attachment cannot be added while the ticket is {preflight.Status}.");
        }

        if (preflight.ActiveAttachmentCount >= SupportAttachmentUploadLimits.MaxActiveAttachmentsPerTicket)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.FileCountExceeded,
                "A support ticket may have at most three active attachments.");
        }

        if (!file.HasFile)
        {
            // No multipart `file` part was present at all — a request-shape validation failure,
            // not a format mismatch. Nothing has been created yet (no temp/scanner/storage work).
            throw DomainProblemException.Validation("A file must be provided.");
        }

        var displayFileName = SanitizeDisplayFileName(file.OriginalFileName);
        var extension = ExtractExtension(displayFileName);
        if (extension is null || !FormatsByExtension.TryGetValue(extension, out var format))
        {
            throw DomainProblemException.UnsupportedMediaType(
                DomainErrorCodes.FileFormatInvalid,
                "Only PNG, JPEG and PDF attachments are supported.");
        }

        extension = extension.ToLowerInvariant();

        if (!IsClaimedContentTypeAcceptable(file.ClaimedContentType, format.MimeType))
        {
            throw DomainProblemException.UnsupportedMediaType(
                DomainErrorCodes.FileFormatInvalid,
                "The declared content type does not match the file extension.");
        }

        if (file.DeclaredLength is { } declaredLength &&
            (declaredLength < 1 || declaredLength > SupportAttachmentUploadLimits.MaxFileSizeBytes))
        {
            throw DomainProblemException.PayloadTooLarge(
                DomainErrorCodes.FileSizeExceeded,
                "The attachment must be between 1 byte and 10 MiB.");
        }

        var temp = await _storage.CreateTempFileAsync(cancellationToken);
        try
        {
            long size;
            byte[] sha256;
            byte[] signaturePrefix;
            await using (var writeStream = temp.WriteStream)
            {
                await using var source = file.OpenRead();
                (size, sha256, signaturePrefix) = await StreamToTempAsync(source, writeStream, cancellationToken);
            }

            if (!format.SignatureMatches(signaturePrefix))
            {
                throw DomainProblemException.UnsupportedMediaType(
                    DomainErrorCodes.FileFormatInvalid,
                    "The file content does not match the declared format.");
            }

            var scanResult = await _scanner.ScanAsync(temp.TempPath, cancellationToken);
            switch (scanResult)
            {
                case FileScanResult.Malware:
                    throw DomainProblemException.UnprocessableEntity(
                        DomainErrorCodes.FileMalwareDetected,
                        "The attachment failed the malware scan.");
                case FileScanResult.Unavailable:
                    throw DomainProblemException.ServiceUnavailable(
                        DomainErrorCodes.FileScanUnavailable,
                        "The malware scanner is currently unavailable. Please try again later.");
            }

            var storageKey = await _storage.CommitAsync(temp.TempPath, cancellationToken);
            try
            {
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var attachment = new SupportAttachment(
                    Guid.CreateVersion7(),
                    preflight.TicketId,
                    supportMessageId: null,
                    memberUserId,
                    displayFileName,
                    storageKey,
                    extension,
                    format.MimeType,
                    size,
                    sha256,
                    nowUtc);
                attachment.RecordScan(PrivateAttachmentScanStatus.Clean, nowUtc);

                await _store.InsertCleanAttachmentAsync(attachment, memberUserId, cancellationToken);

                return new SupportAttachmentDto(
                    attachment.PublicId,
                    attachment.OriginalFileName,
                    attachment.MimeType,
                    attachment.FileSizeBytes,
                    attachment.CreatedAtUtc);
            }
            catch (Exception insertException)
            {
                // The physical file was already committed but the metadata write failed (or the
                // transactional recheck rejected it — including a changed/missing owner) — delete
                // the orphan immediately rather than leaving an unreferenced private file behind.
                // The compensation outcome is observable (result or exception) rather than
                // silently swallowed: if it also fails, both failure causes are preserved instead
                // of one replacing the other, while the client still sees the original failure's
                // status/code.
                bool compensated;
                Exception? compensationFailure = null;
                try
                {
                    compensated = await _storage.DeleteFinalFileAsync(storageKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    compensated = false;
                    compensationFailure = ex;
                }

                if (compensated)
                {
                    throw;
                }

                var compensationException = new SupportAttachmentCompensationException(storageKey, compensationFailure);
                throw insertException is DomainProblemException domainInsertException
                    ? domainInsertException.WithInnerException(compensationException)
                    : new AggregateException(insertException, compensationException);
            }
        }
        finally
        {
            // A no-op once CommitAsync has moved the file away; otherwise removes the staged
            // temp file for every rejected/failed path (size, format, scan, or cancellation).
            _storage.DeleteTempFile(temp.TempPath);
        }
    }

    private static async Task<(long Size, byte[] Sha256, byte[] SignaturePrefix)> StreamToTempAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var signaturePrefix = new byte[SignaturePrefixLength];
        var signatureCaptured = 0;
        var total = 0L;
        var buffer = new byte[CopyBufferSize];

        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken)) > 0)
        {
            total += read;
            if (total > SupportAttachmentUploadLimits.MaxFileSizeBytes)
            {
                throw DomainProblemException.PayloadTooLarge(
                    DomainErrorCodes.FileSizeExceeded,
                    "The attachment must be between 1 byte and 10 MiB.");
            }

            if (signatureCaptured < SignaturePrefixLength)
            {
                var toCopy = Math.Min(SignaturePrefixLength - signatureCaptured, read);
                Buffer.BlockCopy(buffer, 0, signaturePrefix, signatureCaptured, toCopy);
                signatureCaptured += toCopy;
            }

            hasher.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total < 1)
        {
            throw DomainProblemException.PayloadTooLarge(
                DomainErrorCodes.FileSizeExceeded,
                "The attachment must be between 1 byte and 10 MiB.");
        }

        await destination.FlushAsync(cancellationToken);
        var capturedPrefix = signatureCaptured == SignaturePrefixLength
            ? signaturePrefix
            : signaturePrefix[..signatureCaptured];
        return (total, hasher.GetHashAndReset(), capturedPrefix);
    }

    /// <summary>
    /// Strips any path component from the original filename, removes control characters, and
    /// bounds the result at 255 characters. The result is display-only — it never contributes
    /// to a physical path, so this is a defense-in-depth normalization rather than the primary
    /// path-traversal defense (that is StorageKey being server-generated and opaque).
    /// </summary>
    private static string SanitizeDisplayFileName(string originalFileName)
    {
        var lastSeparator = originalFileName.LastIndexOfAny(['/', '\\']);
        var basename = lastSeparator >= 0 ? originalFileName[(lastSeparator + 1)..] : originalFileName;

        var builder = new System.Text.StringBuilder(basename.Length);
        foreach (var c in basename)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        var sanitized = builder.ToString().Trim();
        return TruncateKeepingExtension(sanitized, 255);
    }

    /// <summary>
    /// Truncates <paramref name="fileName"/> to at most <paramref name="maxLength"/> characters
    /// without cutting into a valid terminal extension — otherwise a long-but-valid filename
    /// (e.g. "…verylongname.png") would have its ".png" truncated away and then be wrongly
    /// rejected as an unsupported format.
    /// </summary>
    private static string TruncateKeepingExtension(string fileName, int maxLength)
    {
        if (fileName.Length <= maxLength)
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Length >= maxLength)
        {
            // Pathological case (no usable extension, or an extremely long trailing segment) —
            // fall back to a flat truncation; format validation will reject it on its own merits.
            return fileName[..maxLength];
        }

        var nameWithoutExtension = fileName[..^extension.Length];
        var allowedNameLength = maxLength - extension.Length;
        return nameWithoutExtension[..allowedNameLength] + extension;
    }

    private static string? ExtractExtension(string displayFileName)
    {
        var extension = Path.GetExtension(displayFileName);
        return string.IsNullOrEmpty(extension) || extension.Length > 10 ? null : extension;
    }

    private static bool IsClaimedContentTypeAcceptable(string claimedContentType, string expectedMimeType)
    {
        var separatorIndex = claimedContentType.IndexOf(';');
        var normalized = (separatorIndex >= 0 ? claimedContentType[..separatorIndex] : claimedContentType).Trim();
        return normalized.Equals(expectedMimeType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPngSignature(byte[] prefix)
    {
        ReadOnlySpan<byte> magic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return prefix.AsSpan().Length >= magic.Length && prefix.AsSpan(0, magic.Length).SequenceEqual(magic);
    }

    private static bool MatchesJpegSignature(byte[] prefix)
    {
        ReadOnlySpan<byte> magic = [0xFF, 0xD8, 0xFF];
        return prefix.AsSpan().Length >= magic.Length && prefix.AsSpan(0, magic.Length).SequenceEqual(magic);
    }

    private static bool MatchesPdfSignature(byte[] prefix)
    {
        ReadOnlySpan<byte> magic = "%PDF-"u8;
        return prefix.AsSpan().Length >= magic.Length && prefix.AsSpan(0, magic.Length).SequenceEqual(magic);
    }

    private sealed record AttachmentFormat(string MimeType, Func<byte[], bool> SignatureCheck)
    {
        public bool SignatureMatches(byte[] prefix) => SignatureCheck(prefix);
    }
}
