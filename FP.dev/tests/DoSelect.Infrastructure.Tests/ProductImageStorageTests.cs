using DoSelect.Application.Files;
using DoSelect.Infrastructure.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace DoSelect.Infrastructure.Tests;

public sealed class ProductImageStorageTests
{
    [Fact]
    public async Task StoreAsync_WhenImageIsValid_GeneratesExpectedWebpVariantsWithoutPublishingPartialFiles()
    {
        await using var fixture = new ImageStorageFixture(FileScanOutcome.Clean);
        await using var content = await CreatePngAsync(2000, 1000);

        var result = await fixture.Storage.StoreAsync(
            new ProductImageUpload(content, "catalog/product.png", "image/png"));

        Assert.True(result.IsStored);
        Assert.NotNull(result.Image);
        Assert.Equal("product.png", result.Image.OriginalFileName);
        Assert.Equal(2000, result.Image.Width);
        Assert.Equal(1000, result.Image.Height);
        Assert.Equal(3, result.Image.Variants.Count);
        AssertVariant(result.Image, ProductImageVariant.Small320, 320, 160);
        AssertVariant(result.Image, ProductImageVariant.Medium800, 800, 400);
        AssertVariant(result.Image, ProductImageVariant.Large1600, 1600, 800);
        Assert.StartsWith("product-images/", result.Image.StorageKey, StringComparison.Ordinal);
        Assert.DoesNotContain("product.png", result.Image.StorageKey, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fixture.Scanner.ScanCount);
        Assert.Empty(fixture.GetStagingFiles());
        Assert.Empty(fixture.GetQuarantineFiles());

        foreach (var variant in result.Image.Variants)
        {
            await using var stream = await fixture.Storage.OpenReadAsync(
                result.Image.StorageKey,
                variant.Variant);
            Assert.NotNull(stream);
            var info = await Image.IdentifyAsync(stream);
            Assert.Equal(variant.Width, info.Width);
            Assert.Equal(variant.Height, info.Height);
            Assert.Equal(
                "WEBP",
                info.Metadata.DecodedImageFormat?.Name,
                ignoreCase: true);
        }

        await using var original = await fixture.Storage.OpenReadAsync(
            result.Image.StorageKey,
            ProductImageVariant.Original);
        Assert.NotNull(original);
        var originalBytes = new MemoryStream();
        await original.CopyToAsync(originalBytes);
        Assert.Equal(content.ToArray(), originalBytes.ToArray());
    }

    [Fact]
    public async Task StoreAsync_WhenImageIsSmallerThanTargets_DoesNotUpscaleAnyVariant()
    {
        await using var fixture = new ImageStorageFixture(FileScanOutcome.Clean);
        await using var content = await CreatePngAsync(200, 100);

        var result = await fixture.Storage.StoreAsync(
            new ProductImageUpload(content, "small.png", "image/png"));

        Assert.True(result.IsStored);
        Assert.NotNull(result.Image);
        Assert.All(result.Image.Variants, variant =>
        {
            Assert.Equal(200, variant.Width);
            Assert.Equal(100, variant.Height);
        });
    }

    [Fact]
    public async Task StoreAsync_WhenWebpIsValid_AcceptsInputAndPreservesOriginal()
    {
        await using var fixture = new ImageStorageFixture(FileScanOutcome.Clean);
        await using var content = await CreateWebpAsync(640, 480);

        var result = await fixture.Storage.StoreAsync(
            new ProductImageUpload(content, "product.webp", "image/webp"));

        Assert.True(result.IsStored);
        Assert.NotNull(result.Image);
        Assert.Equal(".webp", result.Image.Extension);
        Assert.Equal("image/webp", result.Image.ContentType);
        Assert.Equal(640, result.Image.Width);
        Assert.Equal(480, result.Image.Height);
    }

    [Fact]
    public async Task StoreAsync_WhenHeaderIsValidButImageCannotDecode_ReturnsProcessingFailureAndCleansUp()
    {
        await using var fixture = new ImageStorageFixture(FileScanOutcome.Clean);
        await using var content = new MemoryStream(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04]);

        var result = await fixture.Storage.StoreAsync(
            new ProductImageUpload(content, "forged.png", "image/png"));

        Assert.Equal(ProductImageStoreStatus.ProcessingFailed, result.Status);
        Assert.Null(result.Image);
        Assert.Empty(fixture.GetPermanentFiles());
        Assert.Empty(fixture.GetStagingFiles());
        Assert.Empty(fixture.GetQuarantineFiles());
    }

    [Fact]
    public async Task StoreAsync_WhenDecodedDimensionsExceedLimit_ReturnsProcessingFailureBeforePersisting()
    {
        await using var fixture = new ImageStorageFixture(FileScanOutcome.Clean);
        await using var content = await CreatePngAsync(
            ProductImageConstraints.MaximumDimension + 1,
            1);

        var result = await fixture.Storage.StoreAsync(
            new ProductImageUpload(content, "too-wide.png", "image/png"));

        Assert.Equal(ProductImageStoreStatus.ProcessingFailed, result.Status);
        Assert.Null(result.Image);
        Assert.Empty(fixture.GetPermanentFiles());
        Assert.Empty(fixture.GetStagingFiles());
        Assert.Empty(fixture.GetQuarantineFiles());
    }

    [Fact]
    public async Task StoreAsync_WhenScannerIsUnavailable_FailsClosedBeforeDecoding()
    {
        await using var fixture = new ImageStorageFixture(FileScanOutcome.Unavailable);
        await using var content = await CreatePngAsync(64, 64);

        var result = await fixture.Storage.StoreAsync(
            new ProductImageUpload(content, "product.png", "image/png"));

        Assert.Equal(ProductImageStoreStatus.ScanUnavailable, result.Status);
        Assert.Null(result.Image);
        Assert.Equal(1, fixture.Scanner.ScanCount);
        Assert.Empty(fixture.GetPermanentFiles());
        Assert.Empty(fixture.GetStagingFiles());
        Assert.Empty(fixture.GetQuarantineFiles());
    }

    [Fact]
    public async Task OpenAndDelete_WhenStorageKeyEscapesImageRoot_RejectWithoutSideEffect()
    {
        await using var fixture = new ImageStorageFixture(FileScanOutcome.Clean);
        var outsideDirectory = Path.Combine(fixture.DataRoot, "outside");
        Directory.CreateDirectory(outsideDirectory);
        var outsideFile = Path.Combine(outsideDirectory, "1600.webp");
        await File.WriteAllTextAsync(outsideFile, "synthetic");

        await using var stream = await fixture.Storage.OpenReadAsync(
            "product-images/../outside",
            ProductImageVariant.Large1600);
        var deleted = await fixture.Storage.DeleteAsync("product-images/../outside");

        Assert.Null(stream);
        Assert.False(deleted);
        Assert.True(File.Exists(outsideFile));
    }

    private static void AssertVariant(
        StoredProductImage image,
        ProductImageVariant variant,
        int width,
        int height)
    {
        var storedVariant = Assert.Single(image.Variants, item => item.Variant == variant);
        Assert.Equal(width, storedVariant.Width);
        Assert.Equal(height, storedVariant.Height);
        Assert.True(storedVariant.FileSizeBytes > 0);
        Assert.Equal(32, storedVariant.Sha256.Length);
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    private static async Task<MemoryStream> CreateWebpAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsync(stream, new WebpEncoder { Quality = 80 });
        stream.Position = 0;
        return stream;
    }

    private sealed class ImageStorageFixture : IAsyncDisposable
    {
        public ImageStorageFixture(FileScanOutcome outcome)
        {
            DataRoot = Path.Combine(
                Path.GetTempPath(),
                "DoSelect.Tests",
                Guid.NewGuid().ToString("N"));
            Scanner = new FakeFileScanner(outcome);
            Storage = new LocalImageStorage(DataRoot, Scanner);
        }

        public string DataRoot { get; }

        public FakeFileScanner Scanner { get; }

        public LocalImageStorage Storage { get; }

        public string[] GetPermanentFiles() => GetFiles("product-images");

        public string[] GetQuarantineFiles() => GetFiles("quarantine");

        public string[] GetStagingFiles() => GetFiles("image-staging");

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private string[] GetFiles(string directoryName)
        {
            var directory = Path.Combine(DataRoot, directoryName);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                : [];
        }
    }

    private sealed class FakeFileScanner : IFileScanner
    {
        private readonly FileScanOutcome _outcome;

        public FakeFileScanner(FileScanOutcome outcome)
        {
            _outcome = outcome;
        }

        public int ScanCount { get; private set; }

        public Task<FileScanResult> ScanAsync(
            string quarantinedFilePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(File.Exists(quarantinedFilePath));
            ScanCount++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new FileScanResult(
                _outcome,
                "Synthetic scanner",
                now,
                now));
        }
    }
}
