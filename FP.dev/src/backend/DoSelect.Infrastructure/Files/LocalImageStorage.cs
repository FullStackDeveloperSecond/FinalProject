using DoSelect.Application.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;

namespace DoSelect.Infrastructure.Files;

public sealed class LocalImageStorage : IImageStorage
{
    private const string ImageDirectoryName = "product-images";
    private const string QuarantineDirectoryName = "quarantine";
    private const string StagingDirectoryName = "image-staging";

    private static readonly IReadOnlyDictionary<ProductImageVariant, int> TargetLongEdges =
        new Dictionary<ProductImageVariant, int>
        {
            [ProductImageVariant.Small320] = 320,
            [ProductImageVariant.Medium800] = 800,
            [ProductImageVariant.Large1600] = 1600,
        };

    private readonly string _imageRoot;
    private readonly string _quarantineRoot;
    private readonly string _stagingRoot;
    private readonly IFileScanner _scanner;
    private readonly Configuration _imageConfiguration;

    public LocalImageStorage(string dataRoot, IFileScanner scanner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(scanner);

        var normalizedDataRoot = Path.GetFullPath(dataRoot);
        _imageRoot = Path.Combine(normalizedDataRoot, ImageDirectoryName);
        _quarantineRoot = Path.Combine(normalizedDataRoot, QuarantineDirectoryName);
        _stagingRoot = Path.Combine(normalizedDataRoot, StagingDirectoryName);
        _scanner = scanner;

        _imageConfiguration = Configuration.Default.Clone();
        _imageConfiguration.MaxDegreeOfParallelism = 2;
        _imageConfiguration.MemoryAllocator = MemoryAllocator.Create(
            new MemoryAllocatorOptions
            {
                MaximumPoolSizeMegabytes = 64,
                AllocationLimitMegabytes = 256,
            });
    }

    public async Task<ProductImageStoreResult> StoreAsync(
        ProductImageUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(upload.Content);

        if (!upload.Content.CanRead)
        {
            throw new ArgumentException("The upload content stream must be readable.", nameof(upload));
        }

        Directory.CreateDirectory(_quarantineRoot);
        Directory.CreateDirectory(_stagingRoot);
        var operationId = Guid.NewGuid().ToString("N");
        var quarantinePath = Path.Combine(_quarantineRoot, $"{operationId}.upload");
        var stagingDirectory = Path.Combine(_stagingRoot, operationId);

        try
        {
            var safeDisplayName = FileStorageUtilities.GetSafeDisplayName(upload.OriginalFileName);
            if (safeDisplayName is null)
            {
                return Failed(ProductImageStoreStatus.FormatInvalid);
            }

            var writeResult = await FileStorageUtilities.WriteLimitedFileAsync(
                upload.Content,
                quarantinePath,
                ProductImageConstraints.MaximumFileSizeBytes,
                cancellationToken);
            if (writeResult.SizeExceeded)
            {
                return Failed(ProductImageStoreStatus.SizeExceeded);
            }

            var validatedFormat = await ProductImageFormatValidator.ValidateAsync(
                quarantinePath,
                safeDisplayName,
                upload.ContentType,
                cancellationToken);
            if (validatedFormat is null)
            {
                return Failed(ProductImageStoreStatus.FormatInvalid);
            }

            var scanResult = await _scanner.ScanAsync(quarantinePath, cancellationToken);
            if (scanResult.Outcome == FileScanOutcome.MalwareDetected)
            {
                return Failed(ProductImageStoreStatus.MalwareDetected);
            }

            if (scanResult.Outcome != FileScanOutcome.Clean)
            {
                return Failed(ProductImageStoreStatus.ScanUnavailable);
            }

            var decoderOptions = new DecoderOptions
            {
                Configuration = _imageConfiguration,
                MaxFrames = 2,
                SkipMetadata = false,
            };
            var imageInfo = await Image.IdentifyAsync(
                decoderOptions,
                quarantinePath,
                cancellationToken);
            if (!IsWithinDecodedImageLimits(imageInfo.Width, imageInfo.Height))
            {
                return Failed(ProductImageStoreStatus.ProcessingFailed);
            }

            using var image = await Image.LoadAsync(
                decoderOptions,
                quarantinePath,
                cancellationToken);
            if (image.Frames.Count != 1)
            {
                return Failed(ProductImageStoreStatus.ProcessingFailed);
            }

            image.Mutate(context => context.AutoOrient());
            if (!IsWithinDecodedImageLimits(image.Width, image.Height))
            {
                return Failed(ProductImageStoreStatus.ProcessingFailed);
            }

            Directory.CreateDirectory(stagingDirectory);
            var originalFileName = $"original{validatedFormat.Extension}";
            File.Copy(quarantinePath, Path.Combine(stagingDirectory, originalFileName));

            var variants = new List<StoredProductImageVariant>(TargetLongEdges.Count);
            foreach (var (variant, targetLongEdge) in TargetLongEdges)
            {
                var variantPath = Path.Combine(stagingDirectory, GetVariantFileName(variant));
                var (width, height) = CalculateDimensions(image.Width, image.Height, targetLongEdge);
                using var variantImage = image.Clone(context =>
                {
                    if (width != image.Width || height != image.Height)
                    {
                        context.Resize(width, height);
                    }
                });
                await variantImage.SaveAsWebpAsync(
                    variantPath,
                    new WebpEncoder
                    {
                        FileFormat = WebpFileFormatType.Lossy,
                        Method = WebpEncodingMethod.Level4,
                        Quality = ProductImageConstraints.WebpQuality,
                        SkipMetadata = true,
                    },
                    cancellationToken);
                var digest = await FileStorageUtilities.CalculateDigestAsync(
                    variantPath,
                    cancellationToken);
                variants.Add(new StoredProductImageVariant(
                    variant,
                    width,
                    height,
                    digest.FileSizeBytes,
                    digest.Sha256));
            }

            var imageId = Guid.NewGuid().ToString("N");
            var storageKey = $"{ImageDirectoryName}/{imageId[..2]}/{imageId}";
            var permanentDirectory = ResolveStorageDirectory(storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(permanentDirectory)!);
            Directory.Move(stagingDirectory, permanentDirectory);

            var storedImage = new StoredProductImage(
                storageKey,
                safeDisplayName,
                validatedFormat.Extension,
                validatedFormat.ContentType,
                writeResult.BytesWritten,
                image.Width,
                image.Height,
                writeResult.Sha256,
                variants);
            return new ProductImageStoreResult(ProductImageStoreStatus.Stored, storedImage);
        }
        catch (Exception exception) when (IsImageProcessingFailure(exception))
        {
            return Failed(ProductImageStoreStatus.ProcessingFailed);
        }
        finally
        {
            FileStorageUtilities.TryDeleteFile(quarantinePath);
            FileStorageUtilities.TryDeleteDirectory(stagingDirectory);
        }
    }

    public Task<Stream?> OpenReadAsync(
        string storageKey,
        ProductImageVariant variant,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveStorageDirectory(storageKey, out var directory) ||
            !Directory.Exists(directory))
        {
            return Task.FromResult<Stream?>(null);
        }

        var filePath = variant == ProductImageVariant.Original
            ? FindOriginalFile(directory)
            : Path.Combine(directory, GetVariantFileName(variant));
        if (filePath is null || !File.Exists(filePath))
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
        if (!TryResolveStorageDirectory(storageKey, out var directory) ||
            !Directory.Exists(directory))
        {
            return Task.FromResult(false);
        }

        Directory.Delete(directory, recursive: true);
        return Task.FromResult(true);
    }

    private static ProductImageStoreResult Failed(ProductImageStoreStatus status) => new(status);

    private static bool IsWithinDecodedImageLimits(int width, int height)
    {
        if (width <= 0 || height <= 0 ||
            width > ProductImageConstraints.MaximumDimension ||
            height > ProductImageConstraints.MaximumDimension)
        {
            return false;
        }

        return (long)width * height <= ProductImageConstraints.MaximumPixelCount;
    }

    private static (int Width, int Height) CalculateDimensions(
        int width,
        int height,
        int targetLongEdge)
    {
        var longEdge = Math.Max(width, height);
        if (longEdge <= targetLongEdge)
        {
            return (width, height);
        }

        var scale = targetLongEdge / (double)longEdge;
        return (
            Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
    }

    private static string GetVariantFileName(ProductImageVariant variant) => variant switch
    {
        ProductImageVariant.Small320 => "320.webp",
        ProductImageVariant.Medium800 => "800.webp",
        ProductImageVariant.Large1600 => "1600.webp",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    private static string? FindOriginalFile(string directory)
    {
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var candidate = Path.Combine(directory, $"original{extension}");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsImageProcessingFailure(Exception exception) =>
        exception is UnknownImageFormatException or
            InvalidImageContentException or
            ImageProcessingException or
            InvalidMemoryOperationException or
            NotSupportedException;

    private string ResolveStorageDirectory(string storageKey)
    {
        if (!TryResolveStorageDirectory(storageKey, out var directory))
        {
            throw new InvalidOperationException("The generated image storage key is invalid.");
        }

        return directory;
    }

    private bool TryResolveStorageDirectory(string storageKey, out string directory)
    {
        directory = string.Empty;
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
                    Path.GetDirectoryName(_imageRoot)!,
                    normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = Path.TrimEndingDirectorySeparator(_imageRoot) +
                             Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            directory = candidate;
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
            !string.Equals(segments[0], ImageDirectoryName, StringComparison.Ordinal) ||
            segments[1].Length != 2 ||
            !Guid.TryParseExact(segments[2], "N", out _))
        {
            return false;
        }

        if (!segments[2].StartsWith(segments[1], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedKey = string.Join('/', segments);
        return true;
    }
}
