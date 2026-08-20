using DoSelect.Application.Files;
using DoSelect.Infrastructure.Files;

namespace DoSelect.Infrastructure.Tests;

public sealed class PrivateFileStorageTests
{
    [Fact]
    public async Task StoreAsync_WhenPngIsValidAndClean_StoresOpaqueFileAndReturnsMetadata()
    {
        await using var fixture = new StorageFixture(FileScanOutcome.Clean);
        await using var content = new MemoryStream(CreatePng());

        var result = await fixture.Storage.StoreAsync(
            new PrivateFileUpload(content, "folder/screenshot.png", "image/png"));

        Assert.True(result.IsStored);
        Assert.Equal(PrivateFileStoreStatus.Stored, result.Status);
        Assert.NotNull(result.File);
        Assert.Equal("screenshot.png", result.File.OriginalFileName);
        Assert.Equal(".png", result.File.Extension);
        Assert.Equal("image/png", result.File.ContentType);
        Assert.Equal(content.Length, result.File.FileSizeBytes);
        Assert.Equal(32, result.File.Sha256.Length);
        Assert.StartsWith("private-files/", result.File.StorageKey, StringComparison.Ordinal);
        Assert.DoesNotContain("screenshot", result.File.StorageKey, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fixture.Scanner.ScanCount);

        await using var storedContent = await fixture.Storage.OpenReadAsync(result.File.StorageKey);
        Assert.NotNull(storedContent);
        using var copy = new MemoryStream();
        await storedContent.CopyToAsync(copy);
        Assert.Equal(CreatePng(), copy.ToArray());
    }

    [Fact]
    public async Task StoreAsync_WhenExtensionMimeAndSignatureDisagree_RejectsBeforeScan()
    {
        await using var fixture = new StorageFixture(FileScanOutcome.Clean);
        await using var content = new MemoryStream(CreatePdf());

        var result = await fixture.Storage.StoreAsync(
            new PrivateFileUpload(content, "evidence.png", "image/png"));

        Assert.Equal(PrivateFileStoreStatus.FormatInvalid, result.Status);
        Assert.Null(result.File);
        Assert.Equal(0, fixture.Scanner.ScanCount);
        Assert.Empty(fixture.GetPermanentFiles());
        Assert.Empty(fixture.GetQuarantineFiles());
    }

    [Fact]
    public async Task StoreAsync_WhenActualStreamExceedsLimit_RejectsAndRemovesQuarantineFile()
    {
        await using var fixture = new StorageFixture(FileScanOutcome.Clean);
        await using var content = new MemoryStream(
            new byte[PrivateFileConstraints.MaximumFileSizeBytes + 1]);

        var result = await fixture.Storage.StoreAsync(
            new PrivateFileUpload(content, "oversized.png", "image/png"));

        Assert.Equal(PrivateFileStoreStatus.SizeExceeded, result.Status);
        Assert.Equal(0, fixture.Scanner.ScanCount);
        Assert.Empty(fixture.GetPermanentFiles());
        Assert.Empty(fixture.GetQuarantineFiles());
    }

    [Fact]
    public async Task StoreAsync_WhenScannerDetectsMalware_DoesNotPersistFile()
    {
        await using var fixture = new StorageFixture(FileScanOutcome.MalwareDetected);
        await using var content = new MemoryStream(CreatePdf());

        var result = await fixture.Storage.StoreAsync(
            new PrivateFileUpload(content, "evidence.pdf", "application/pdf"));

        Assert.Equal(PrivateFileStoreStatus.MalwareDetected, result.Status);
        Assert.Empty(fixture.GetPermanentFiles());
        Assert.Empty(fixture.GetQuarantineFiles());
    }

    [Fact]
    public async Task StoreAsync_WhenScannerIsUnavailable_FailsClosedAndDoesNotPersistFile()
    {
        await using var fixture = new StorageFixture(FileScanOutcome.Unavailable);
        await using var content = new MemoryStream(CreateJpeg());

        var result = await fixture.Storage.StoreAsync(
            new PrivateFileUpload(content, "evidence.jpg", "image/jpeg"));

        Assert.Equal(PrivateFileStoreStatus.ScanUnavailable, result.Status);
        Assert.Empty(fixture.GetPermanentFiles());
        Assert.Empty(fixture.GetQuarantineFiles());
    }

    [Fact]
    public async Task OpenAndDelete_WhenStorageKeyEscapesPrivateRoot_RejectWithoutSideEffect()
    {
        await using var fixture = new StorageFixture(FileScanOutcome.Clean);
        var outsideFile = Path.Combine(fixture.DataRoot, "outside.txt");
        Directory.CreateDirectory(fixture.DataRoot);
        await File.WriteAllTextAsync(outsideFile, "synthetic");

        await using var stream = await fixture.Storage.OpenReadAsync(
            "private-files/../outside.txt");
        var deleted = await fixture.Storage.DeleteAsync("private-files/../outside.txt");

        Assert.Null(stream);
        Assert.False(deleted);
        Assert.True(File.Exists(outsideFile));
    }

    [Theory]
    [InlineData("Threat detected: Synthetic.Test")]
    [InlineData("Threats found during scan")]
    [InlineData("偵測到威脅：Synthetic.Test")]
    public void IndicatesMalware_WhenDefenderReportsThreat_ReturnsTrue(string output)
    {
        Assert.True(MicrosoftDefenderFileScanner.IndicatesMalware(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Scan failed with an unknown error")]
    [InlineData("Found no threats")]
    public void IndicatesMalware_WhenOutputIsNotExplicitDetection_ReturnsFalse(string output)
    {
        Assert.False(MicrosoftDefenderFileScanner.IndicatesMalware(output));
    }

    private static byte[] CreatePng() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private static byte[] CreatePdf() =>
        "%PDF-1.7\nsynthetic"u8.ToArray();

    private static byte[] CreateJpeg() =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02];

    private sealed class StorageFixture : IAsyncDisposable
    {
        public StorageFixture(FileScanOutcome outcome)
        {
            DataRoot = Path.Combine(
                Path.GetTempPath(),
                "DoSelect.Tests",
                Guid.NewGuid().ToString("N"));
            Scanner = new FakeFileScanner(outcome);
            Storage = new LocalPrivateFileStorage(DataRoot, Scanner);
        }

        public string DataRoot { get; }

        public FakeFileScanner Scanner { get; }

        public LocalPrivateFileStorage Storage { get; }

        public string[] GetPermanentFiles() => GetFiles("private-files");

        public string[] GetQuarantineFiles() => GetFiles("quarantine");

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
