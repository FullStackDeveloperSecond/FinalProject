namespace DoSelect.Application.Files;

public static class PrivateFileConstraints
{
    public const long MaximumFileSizeBytes = 10 * 1024 * 1024;
}

public static class ProductImageConstraints
{
    public const long MaximumFileSizeBytes = 10 * 1024 * 1024;
    public const long MaximumPixelCount = 25_000_000;
    public const int MaximumDimension = 10_000;
    public const int WebpQuality = 80;
}

public enum PrivateFileStoreStatus
{
    Stored = 0,
    SizeExceeded = 1,
    FormatInvalid = 2,
    MalwareDetected = 3,
    ScanUnavailable = 4,
}

public enum ProductImageStoreStatus
{
    Stored = 0,
    SizeExceeded = 1,
    FormatInvalid = 2,
    MalwareDetected = 3,
    ScanUnavailable = 4,
    ProcessingFailed = 5,
}

public enum ProductImageVariant
{
    Original = 0,
    Small320 = 1,
    Medium800 = 2,
    Large1600 = 3,
}

public enum FileScanOutcome
{
    Clean = 0,
    MalwareDetected = 1,
    Unavailable = 2,
}

public sealed record FileScanResult(
    FileScanOutcome Outcome,
    string ScannerName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int? ExitCode = null,
    string? DetectionName = null,
    string? FailureCode = null);

public sealed record PrivateFileUpload(
    Stream Content,
    string OriginalFileName,
    string ContentType);

public sealed record ProductImageUpload(
    Stream Content,
    string OriginalFileName,
    string ContentType);

public sealed record StoredPrivateFile(
    string StorageKey,
    string OriginalFileName,
    string Extension,
    string ContentType,
    long FileSizeBytes,
    byte[] Sha256);

public sealed record StoredProductImageVariant(
    ProductImageVariant Variant,
    int Width,
    int Height,
    long FileSizeBytes,
    byte[] Sha256);

public sealed record StoredProductImage(
    string StorageKey,
    string OriginalFileName,
    string Extension,
    string ContentType,
    long OriginalFileSizeBytes,
    int Width,
    int Height,
    byte[] Sha256,
    IReadOnlyList<StoredProductImageVariant> Variants);

public sealed record PrivateFileStoreResult(
    PrivateFileStoreStatus Status,
    StoredPrivateFile? File = null)
{
    public bool IsStored => Status == PrivateFileStoreStatus.Stored && File is not null;
}

public sealed record ProductImageStoreResult(
    ProductImageStoreStatus Status,
    StoredProductImage? Image = null)
{
    public bool IsStored => Status == ProductImageStoreStatus.Stored && Image is not null;
}

public interface IFileScanner
{
    Task<FileScanResult> ScanAsync(
        string quarantinedFilePath,
        CancellationToken cancellationToken = default);
}

public interface IPrivateFileStorage
{
    Task<PrivateFileStoreResult> StoreAsync(
        PrivateFileUpload upload,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}

public interface IImageStorage
{
    Task<ProductImageStoreResult> StoreAsync(
        ProductImageUpload upload,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string storageKey,
        ProductImageVariant variant,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}
