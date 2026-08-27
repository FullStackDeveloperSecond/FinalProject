using DoSelect.Application.Common;
using DoSelect.Application.Storage;
using DoSelect.Application.Support;

namespace DoSelect.Application.Tests.Support;

public sealed class SupportAttachmentReadServiceTests
{
    [Fact]
    public async Task AuthorizedRecord_IsLookedUpBeforeStorageAndReturnsExactMetadata()
    {
        var events = new List<string>();
        var store = new StubStore(events)
        {
            Record = new("folder/file.bin", "safe.bin", "application/x-test"),
        };
        var bytes = new byte[] { 0, 1, 2, 255 };
        var storage = new StubStorage(events) { Stream = new MemoryStream(bytes) };
        var service = new SupportAttachmentReadService(store, storage);

        var result = await service.GetContentAsync(
            new(SupportAttachmentActorType.Member, "member-unique"), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(["store", "storage"], events);
        Assert.Equal("application/x-test", result.ContentType);
        Assert.Equal("safe.bin", result.DownloadFileName);
        using var output = new MemoryStream();
        await result.Content.CopyToAsync(output);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task UnauthorizedOrMissingMetadata_ThrowsSameNotFoundWithoutOpeningStorage()
    {
        var events = new List<string>();
        var storage = new StubStorage(events) { Stream = new MemoryStream([1]) };
        var service = new SupportAttachmentReadService(new StubStore(events), storage);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.GetContentAsync(
            new(SupportAttachmentActorType.Member, "other-member"), Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ResourceNotFound, exception.Code);
        Assert.Equal(["store"], events);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task MissingPhysicalFile_UsesSameNotFoundAfterAuthorization()
    {
        var events = new List<string>();
        var store = new StubStore(events) { Record = new("missing", "name.txt", "text/plain") };
        var service = new SupportAttachmentReadService(store, new StubStorage(events));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.GetContentAsync(
            new(SupportAttachmentActorType.SupportHandler, "admin-unique"), Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ResourceNotFound, exception.Code);
        Assert.Equal(["store", "storage"], events);
    }

    private sealed class StubStore(List<string> events) : ISupportAttachmentReadStore
    {
        public SupportAttachmentReadRecord? Record { get; init; }
        public Task<SupportAttachmentReadRecord?> FindReadableAsync(Guid attachmentPublicId,
            SupportAttachmentActor actor, CancellationToken cancellationToken)
        {
            events.Add("store");
            return Task.FromResult(Record);
        }
    }

    private sealed class StubStorage(List<string> events) : IPrivateFileStorage
    {
        public Stream? Stream { get; init; }
        public int Calls { get; private set; }
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            Calls++;
            events.Add("storage");
            return Task.FromResult(Stream);
        }
    }
}
