using DoSelect.Application.Common;
using DoSelect.Application.Storage;
using DoSelect.Application.Support;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Tests.Support;

public sealed class SupportAttachmentUploadServiceTests
{
    private static readonly Guid TicketPublicId = Guid.NewGuid();
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2];
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 1, 2];
    private static readonly byte[] Pdf = "%PDF-1.7 payload"u8.ToArray();

    [Theory]
    [InlineData(false, SupportTicketStatus.Open, 0, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(true, SupportTicketStatus.Closed, 0, 409, DomainErrorCodes.SupportTicketStateConflict)]
    [InlineData(true, SupportTicketStatus.Cancelled, 0, 409, DomainErrorCodes.SupportTicketStateConflict)]
    [InlineData(true, SupportTicketStatus.Open, 3, 400, DomainErrorCodes.FileCountExceeded)]
    public async Task PreflightRejection_DoesNotOpenReadCreateTempOrScan(
        bool exists, SupportTicketStatus status, int count, int expectedStatus, string expectedCode)
    {
        var fixture = new Fixture(exists ? new(42, status, count) : null, useDefaultPreflight: false);
        var opened = false;

        var ex = await Assert.ThrowsAsync<DomainProblemException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, File("a.png", "image/png", Png, () => opened = true), default));

        Assert.Equal((expectedStatus, expectedCode), (ex.StatusCode, ex.Code));
        Assert.False(opened);
        Assert.Empty(fixture.Storage.Events);
        Assert.Empty(fixture.Scanner.Paths);
    }

    [Theory]
    [MemberData(nameof(ValidFormats))]
    public async Task ValidFile_UsesRequiredOrderAndPersistsCleanPublicSafeMetadata(
        string name, string mime, byte[] bytes)
    {
        var fixture = new Fixture();
        await fixture.Service.UploadAsync("member-a", TicketPublicId, File(name, mime, bytes, () => fixture.Events.Add("open")), default);

        Assert.Equal(["preflight", "temp", "open", "scan", "commit", "insert", "temp-delete"], fixture.Events);
        var saved = Assert.Single(fixture.Store.Inserted);
        Assert.Equal("member-a", saved.UploadedByUserId);
        Assert.Equal(PrivateAttachmentScanStatus.Clean, saved.ScanStatus);
        Assert.Equal(bytes.LongLength, saved.FileSizeBytes);
        Assert.Equal(bytes, fixture.Storage.CommittedBytes);
        Assert.DoesNotContain(Path.GetFileName(name), saved.StorageKey, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string, string, byte[]> ValidFormats() => new()
    {
        { "x.png", "image/png", Png }, { "x.jpg", "image/jpeg", Jpeg },
        { "x.jpeg", "image/jpeg; charset=binary", Jpeg }, { "x.pdf", "application/pdf", Pdf },
    };

    [Theory]
    [InlineData("x.exe", "application/octet-stream")]
    [InlineData("x.png", "image/jpeg")]
    [InlineData("x.jpg", "image/png")]
    [InlineData("x.pdf", "text/plain")]
    public async Task ExtensionOrMimeMismatch_Returns415BeforeOpeningOrStorage(string name, string mime)
    {
        var fixture = new Fixture(); var opened = false;
        var ex = await Assert.ThrowsAsync<DomainProblemException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, File(name, mime, Png, () => opened = true), default));
        Assert.Equal((415, DomainErrorCodes.FileFormatInvalid), (ex.StatusCode, ex.Code));
        Assert.False(opened); Assert.DoesNotContain("temp", fixture.Events); Assert.Empty(fixture.Scanner.Paths);
    }

    [Theory]
    [MemberData(nameof(BadMagic))]
    public async Task BadMagic_Returns415BeforeScannerAndCommit(string name, string mime, byte[] bytes)
    {
        var fixture = new Fixture();
        var ex = await Assert.ThrowsAsync<DomainProblemException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, File(name, mime, bytes), default));
        Assert.Equal((415, DomainErrorCodes.FileFormatInvalid), (ex.StatusCode, ex.Code));
        Assert.Empty(fixture.Scanner.Paths); Assert.DoesNotContain("commit", fixture.Events);
        Assert.Contains("temp-delete", fixture.Events);
    }

    public static TheoryData<string, string, byte[]> BadMagic() => new()
    {
        { "fake.png", "image/png", Jpeg }, { "fake.jpg", "image/jpeg", Pdf },
        { "fake.pdf", "application/pdf", Png },
    };

    [Theory]
    [InlineData(0)]
    [InlineData(10485761)]
    public async Task InvalidDeclaredLength_Returns413WithoutReadingOrTemp(long length)
    {
        var fixture = new Fixture(); var opened = false;
        var ex = await Assert.ThrowsAsync<DomainProblemException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, new("x.png", "image/png", length, HasFile: true, () => { opened = true; return new MemoryStream(Png); }), default));
        Assert.Equal((413, DomainErrorCodes.FileSizeExceeded), (ex.StatusCode, ex.Code));
        Assert.False(opened); Assert.DoesNotContain("temp", fixture.Events);
    }

    [Fact]
    public async Task LyingStreamOverLimit_Returns413AndCleansTempWithoutScanCommitOrInsert()
    {
        var fixture = new Fixture();
        var bytes = new byte[SupportAttachmentUploadLimits.MaxFileSizeBytes + 1];
        Png.CopyTo(bytes, 0);
        var ex = await Assert.ThrowsAsync<DomainProblemException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, File("x.png", "image/png", bytes, declared: 1), default));
        Assert.Equal(413, ex.StatusCode); Assert.Contains("temp-delete", fixture.Events);
        Assert.Empty(fixture.Scanner.Paths); Assert.Empty(fixture.Store.Inserted);
    }

    [Theory]
    [InlineData(FileScanResult.Malware, 422, DomainErrorCodes.FileMalwareDetected)]
    [InlineData(FileScanResult.Unavailable, 503, DomainErrorCodes.FileScanUnavailable)]
    public async Task NonCleanScan_FailsClosedAndCleansTemp(FileScanResult result, int status, string code)
    {
        var fixture = new Fixture(); fixture.Scanner.Result = result;
        var ex = await Assert.ThrowsAsync<DomainProblemException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, File("x.png", "image/png", Png), default));
        Assert.Equal((status, code), (ex.StatusCode, ex.Code));
        Assert.DoesNotContain("commit", fixture.Events); Assert.Empty(fixture.Store.Inserted);
        Assert.Contains("temp-delete", fixture.Events);
    }

    [Fact]
    public async Task InsertFailure_AfterCommitAttemptsFinalCompensationAndRethrows()
    {
        var fixture = new Fixture(); fixture.Store.InsertException = new InvalidOperationException("db");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, File("x.png", "image/png", Png), default));
        Assert.Equal("db", ex.Message);
        Assert.Equal(["commit", "insert", "final-delete", "temp-delete"], fixture.Events.Where(x => x is "commit" or "insert" or "final-delete" or "temp-delete"));
    }

    [Fact]
    public async Task CompensationDeleteFailure_IsObservableAndDoesNotMasqueradeAsSuccessfulUpload()
    {
        var fixture = new Fixture();
        fixture.Store.InsertException = new InvalidOperationException("db");
        fixture.Storage.FinalDeleteResult = false;
        var ex = await Assert.ThrowsAsync<AggregateException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, File("x.png", "image/png", Png), default));
        Assert.Contains(ex.InnerExceptions, error => error is InvalidOperationException { Message: "db" });
        Assert.Contains(ex.InnerExceptions, error => error is SupportAttachmentCompensationException);
        Assert.Contains("final-delete", fixture.Events);
    }

    [Fact]
    public async Task CancellationWhileReading_DisposesSourceAndCleansTemp()
    {
        var fixture = new Fixture(); using var cts = new CancellationTokenSource();
        var stream = new CancelOnReadStream(cts);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.UploadAsync(
            "member-a", TicketPublicId, new("x.png", "image/png", 1, HasFile: true, () => stream), cts.Token));
        Assert.True(stream.Disposed); Assert.Contains("temp-delete", fixture.Events);
    }

    [Fact]
    public async Task FilenameTraversalAndControls_AreDisplayOnlySanitizedAndBounded()
    {
        var fixture = new Fixture();
        var name = "../bad\0\r\nname.png";
        await fixture.Service.UploadAsync("member-a", TicketPublicId, File(name, "image/png", Png), default);
        var saved = Assert.Single(fixture.Store.Inserted);
        Assert.DoesNotContain('/', saved.OriginalFileName); Assert.DoesNotContain('\\', saved.OriginalFileName);
        Assert.DoesNotContain(saved.OriginalFileName, saved.StorageKey); Assert.True(saved.OriginalFileName.Length <= 255);
        Assert.DoesNotContain(saved.OriginalFileName, char.IsControl);
    }

    private static IncomingAttachmentFile File(string name, string mime, byte[] bytes, Action? opened = null, long? declared = null) =>
        new(name, mime, declared ?? bytes.LongLength, HasFile: true, () => { opened?.Invoke(); return new MemoryStream(bytes, writable: false); });

    private sealed class Fixture
    {
        public List<string> Events { get; } = [];
        public FakeStore Store { get; }
        public FakeStorage Storage { get; }
        public FakeScanner Scanner { get; }
        public SupportAttachmentUploadService Service { get; }
        public Fixture(SupportAttachmentUploadPreflight? preflight = null, bool useDefaultPreflight = true)
        {
            Store = new(Events) { Preflight = useDefaultPreflight ? preflight ?? new(42, SupportTicketStatus.Open, 0) : preflight };
            Storage = new(Events); Scanner = new(Events);
            Service = new(Store, Storage, Scanner, new FixedTimeProvider(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)));
        }
    }

    private sealed class FakeStore(List<string> events) : ISupportAttachmentUploadStore
    {
        public SupportAttachmentUploadPreflight? Preflight { get; set; }
        public Exception? InsertException { get; set; }
        public List<SupportAttachment> Inserted { get; } = [];
        public Task<SupportAttachmentUploadPreflight?> LoadPreflightAsync(Guid id, string user, CancellationToken ct) { events.Add("preflight"); return Task.FromResult(Preflight); }
        public Task InsertCleanAttachmentAsync(SupportAttachment attachment, string memberUserId, CancellationToken ct) { events.Add("insert"); Assert.Equal(memberUserId, attachment.UploadedByUserId); if (InsertException is not null) throw InsertException; Inserted.Add(attachment); return Task.CompletedTask; }
    }
    private sealed class FakeStorage(List<string> events) : IPrivateAttachmentUploadStorage
    {
        private readonly MemoryStream _temp = new();
        public List<string> Events { get; } = [];
        public byte[] CommittedBytes { get; private set; } = [];
        public bool FinalDeleteResult { get; set; } = true;
        public Task<AttachmentTempFile> CreateTempFileAsync(CancellationToken ct) { events.Add("temp"); Events.Add("temp"); return Task.FromResult(new AttachmentTempFile("temp", _temp)); }
        public Task<string> CommitAsync(string path, CancellationToken ct) { events.Add("commit"); CommittedBytes = _temp.ToArray(); return Task.FromResult(Guid.NewGuid().ToString("N")); }
        public Task<bool> DeleteFinalFileAsync(string key, CancellationToken ct) { events.Add("final-delete"); Events.Add("final-delete"); return Task.FromResult(FinalDeleteResult); }
        public void DeleteTempFile(string path) { events.Add("temp-delete"); Events.Add("temp-delete"); }
    }
    private sealed class FakeScanner(List<string> events) : IFileScanner
    {
        public FileScanResult Result { get; set; } = FileScanResult.Clean;
        public List<string> Paths { get; } = [];
        public Task<FileScanResult> ScanAsync(string path, CancellationToken ct) { events.Add("scan"); Paths.Add(path); return Task.FromResult(Result); }
    }
    private sealed class CancelOnReadStream(CancellationTokenSource cts) : Stream
    {
        public bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { cts.Cancel(); await Task.Yield(); ct.ThrowIfCancellationRequested(); return 0; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
