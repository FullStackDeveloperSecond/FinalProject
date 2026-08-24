using DoSelect.Application.Storage;
using DoSelect.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests;

public sealed class LocalPrivateAttachmentUploadStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"doselect-upload-{Guid.NewGuid():N}");

    [Fact]
    public async Task TempIsIsolatedAndCommitAtomicallyMovesExactBytesToReadRoot()
    {
        var storage = CreateStorage();
        var temp = await storage.CreateTempFileAsync(default);
        var bytes = new byte[] { 0, 1, 127, 255 };
        await temp.WriteStream.WriteAsync(bytes);
        await temp.WriteStream.DisposeAsync();

        Assert.StartsWith(Path.Combine(_root, "private", "support-uploads-tmp"), temp.TempPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.Combine("private", "support") + Path.DirectorySeparatorChar, temp.TempPath, StringComparison.OrdinalIgnoreCase);
        var key = await storage.CommitAsync(temp.TempPath, default);

        Assert.False(File.Exists(temp.TempPath));
        Assert.DoesNotContain('.', key);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "private", "support", key)));
    }

    [Fact]
    public async Task DeleteFinal_RemovesValidKeyButTraversalCannotDeleteSibling()
    {
        var storage = CreateStorage();
        var finalRoot = Path.Combine(_root, "private", "support");
        var sibling = Path.Combine(_root, "private", "outside.bin");
        await File.WriteAllBytesAsync(sibling, [9]);
        await File.WriteAllBytesAsync(Path.Combine(finalRoot, "valid"), [1]);

        Assert.True(await storage.DeleteFinalFileAsync("valid", default));
        Assert.False(await storage.DeleteFinalFileAsync("../outside.bin", default));

        Assert.False(File.Exists(Path.Combine(finalRoot, "valid")));
        Assert.True(File.Exists(sibling));
    }

    [Fact]
    public async Task CancelledCreateAndCommitDoNotMutateStorage()
    {
        var storage = CreateStorage(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CreateTempFileAsync(cts.Token));
        var temp = await storage.CreateTempFileAsync(default); await temp.WriteStream.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CommitAsync(temp.TempPath, cts.Token));
        Assert.True(File.Exists(temp.TempPath));
    }

    [Fact]
    public async Task InvalidCompensationKey_ReturnsFalseAndLogsOpaqueKeyWithoutPhysicalPath()
    {
        var logger = new RecordingLogger<LocalPrivateAttachmentUploadStorage>();
        var storage = CreateStorage(logger);

        Assert.False(await storage.DeleteFinalFileAsync("../task-owned-invalid", default));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("../task-owned-invalid", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    private LocalPrivateAttachmentUploadStorage CreateStorage(ILogger<LocalPrivateAttachmentUploadStorage>? logger = null) => new(
        Options.Create(new StorageOptions { DataRoot = _root }),
        logger ?? NullLogger<LocalPrivateAttachmentUploadStorage>.Instance);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
