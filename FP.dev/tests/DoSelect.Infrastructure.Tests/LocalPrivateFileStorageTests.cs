using DoSelect.Application.Storage;
using DoSelect.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests;

public sealed class LocalPrivateFileStorageTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"doselect-attachment-{Guid.NewGuid():N}");

    [Fact]
    public async Task RelativeKeyBelowPrivateSupport_ReturnsExactBytes()
    {
        var directory = Path.Combine(_dataRoot, "private", "support", "unique");
        Directory.CreateDirectory(directory);
        var expected = new byte[] { 0, 17, 128, 255 };
        await File.WriteAllBytesAsync(Path.Combine(directory, "payload.bin"), expected);
        var storage = CreateStorage();

        await using var stream = await storage.OpenReadAsync("unique/payload.bin", CancellationToken.None);
        Assert.NotNull(stream);
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task MissingFile_ReturnsNull() =>
        Assert.Null(await CreateStorage().OpenReadAsync($"missing-{Guid.NewGuid():N}.bin", CancellationToken.None));

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("folder/../../escape.bin")]
    [InlineData("folder\\..\\escape.bin")]
    [InlineData("folder/..\\escape.bin")]
    [InlineData("/rooted.bin")]
    [InlineData("C:\\rooted.bin")]
    [InlineData("folder//file.bin")]
    public async Task UnsafeRootedTraversalAndMixedSeparatorKeys_ReturnNull(string key) =>
        Assert.Null(await CreateStorage().OpenReadAsync(key, CancellationToken.None));

    [Fact]
    public async Task PrefixSiblingCannotEscapeContainment()
    {
        var sibling = Path.Combine(_dataRoot, "private", "support-evil");
        Directory.CreateDirectory(sibling);
        await File.WriteAllBytesAsync(Path.Combine(sibling, "secret.bin"), [42]);

        Assert.Null(await CreateStorage().OpenReadAsync("../support-evil/secret.bin", CancellationToken.None));
    }

    private LocalPrivateFileStorage CreateStorage()
    {
        return new LocalPrivateFileStorage(Options.Create(new StorageOptions { DataRoot = _dataRoot }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true);
    }
}
